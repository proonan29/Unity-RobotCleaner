using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    public interface ICleaningPlanner
    {
        string Name { get; }
        /// <summary>Append the next waypoint(s) for the robot centre. Return false when nothing is left to clean.</summary>
        bool NextGoals(SimulationCore sim, List<Vector3> waypoints);
        /// <summary>Called when a goal was unreachable or produced no motion.</summary>
        void OnGoalFailed(Vector3 goal);
    }

    public enum SimState { Idle, Running, Paused, Done }

    /// <summary>What the robot is doing right now.</summary>
    public enum RobotTask { Cleaning, ReturningToDock, Charging, ReturningToWork, Docked }

    [System.Serializable]
    public class SimStats
    {
        public string planner;
        public string endReason = "";
        public float simTime;           // s (includes trips to the dock and charging)
        public float distance;          // m
        public float rotationDeg;
        public int goals;
        public int failedGoals;
        public float coveragePercent;
        public float coveredM2;
        public float cleanableM2;
        public float averagePasses;
        public float overlapPercent;
        public float timeTo50 = -1, timeTo90 = -1, timeTo95 = -1;
        public float planningMs;        // wall-clock time spent in the planner
        public List<Vector2> timeline = new List<Vector2>();          // (simTime, coverage%)

        // battery
        public bool batteryEnabled;
        public float batteryPercent = 100f;
        public float minBattery = 100f;
        public int charges;             // number of charging sessions (mid-run)
        public float chargingTime;      // s
        public List<Vector2> batteryTimeline = new List<Vector2>();   // (simTime, battery%)
    }

    /// <summary>
    /// Fixed-step cleaning simulation: robot kinematics + coverage + planner + battery/dock behaviour.
    /// Has no MonoBehaviour dependency, so it can run headless (CLI batch) or be ticked by SimulationRunner.
    ///
    /// Task flow:  Cleaning --(battery low)--> ReturningToDock --> Charging --> ReturningToWork --> Cleaning
    ///             Cleaning --(done)--> ReturningToDock --> Docked (run ends)
    /// The brush only cleans while the task is Cleaning.
    /// </summary>
    public class SimulationCore
    {
        public readonly CoverageGrid Grid;
        public readonly RobotSim Robot;
        public readonly ICleaningPlanner Planner;
        public readonly BatterySim Battery;
        public readonly SimStats Stats = new SimStats();

        public Vector3 Dock;
        public float DockHeading;

        public SimState State { get; private set; } = SimState.Idle;
        public RobotTask Task { get; private set; } = RobotTask.Cleaning;
        public float MaxSimTime = 3 * 3600f;        // safety stop (s)
        public float TargetCoverage = 99.5f;        // %
        public float TimelineInterval = 10f;        // s
        public float ReturnCheckInterval = 1f;      // s
        /// <summary>True while driving home because the job is finished (not to recharge).</summary>
        public bool IsFinalReturn => _finalReturn;

        Vector3 _lastStamp;
        Vector3? _pendingGoal;
        Vector3 _resumePoint;
        bool _finalReturn;
        string _pendingEndReason;
        float _distAtPlan;
        float _nextSample, _nextReturnCheck;
        int _consecutiveFailures;
        readonly List<Vector3> _waypoints = new List<Vector3>();
        readonly List<Vector3> _single = new List<Vector3>(1);
        readonly NavMeshPath _homePath = new NavMeshPath();
        readonly System.Diagnostics.Stopwatch _planWatch = new System.Diagnostics.Stopwatch();

        public SimulationCore(CoverageGrid grid, RobotSim robot, ICleaningPlanner planner, BatterySpec? battery = null)
        {
            Grid = grid;
            Robot = robot;
            Planner = planner;
            var spec = battery ?? BatterySpec.Default;
            if (!battery.HasValue) spec.enabled = false;
            Battery = new BatterySim(spec);
            Dock = robot.Position;
            DockHeading = robot.Heading;
            Stats.planner = planner.Name;
            Stats.batteryEnabled = spec.enabled;
        }

        public void Start()
        {
            Grid.Clear();
            _lastStamp = Robot.Position;
            Grid.Stamp(_lastStamp, Robot.Spec.brushRadius);
            _nextSample = 0f;
            _nextReturnCheck = 0f;
            Task = RobotTask.Cleaning;
            State = SimState.Running;
            Sample();
        }

        public void Pause(bool paused)
        {
            if (State == SimState.Running && paused) State = SimState.Paused;
            else if (State == SimState.Paused && !paused) State = SimState.Running;
        }

        public void Stop(string reason)
        {
            if (State == SimState.Done) return;
            State = SimState.Done;
            Stats.endReason = reason;
            UpdateStats();
            Stats.timeline.Add(new Vector2(Stats.simTime, Stats.coveragePercent));
            Stats.batteryTimeline.Add(new Vector2(Stats.simTime, Battery.Percent));
        }

        public void Step(float dt)
        {
            if (State != SimState.Running) return;

            float dist0 = Robot.Distance, rot0 = Robot.RotationDeg;

            switch (Task)
            {
                case RobotTask.Cleaning:
                    if (Battery.Spec.enabled && Stats.simTime >= _nextReturnCheck)
                    {
                        _nextReturnCheck = Stats.simTime + ReturnCheckInterval;
                        if (ShouldReturnToCharge()) { BeginReturn(false, null); break; }
                    }
                    if (!Robot.HasPath && !Plan()) break;
                    if (Task == RobotTask.Cleaning) Robot.Step(dt);
                    break;

                case RobotTask.ReturningToDock:
                case RobotTask.ReturningToWork:
                    if (!Robot.HasPath) { ArriveTransit(); break; }
                    Robot.Step(dt);
                    break;

                case RobotTask.Charging:
                    Battery.Charge(dt);
                    Stats.chargingTime += dt;
                    if (Battery.Percent >= Battery.Spec.resumeAtPercent - 1e-3f) ResumeWork();
                    break;
            }
            if (State != SimState.Running) return;

            Stats.simTime += dt;

            // battery drain from what the robot actually did this step
            if (Task != RobotTask.Charging)
            {
                float drive = (Robot.Distance - dist0) / Robot.Spec.speed;
                float turn = Mathf.Min((Robot.RotationDeg - rot0) / Robot.Spec.angularSpeed, Mathf.Max(0f, dt - drive));
                Battery.Drain(drive, turn, Mathf.Max(0f, dt - drive - turn));
                if (Battery.Spec.enabled && Battery.Percent <= 0f) { Stop("battery depleted"); return; }
            }

            // the brush only runs while cleaning
            var p = Robot.Position;
            if (Task == RobotTask.Cleaning)
            {
                float dx = p.x - _lastStamp.x, dz = p.z - _lastStamp.z;
                float h = Grid.CellSize * 0.5f;
                if (dx * dx + dz * dz >= h * h)
                {
                    Grid.Sweep(_lastStamp, p, Robot.Spec.brushRadius);
                    _lastStamp = p;
                }
            }
            else _lastStamp = p;

            UpdateStats();                                  // O(1): counters are maintained by the grid
            if (Stats.simTime >= _nextSample) Sample();
            if (Task == RobotTask.Cleaning && Stats.coveragePercent >= TargetCoverage) BeginReturn(true, "target coverage");
            else if (Stats.simTime >= MaxSimTime) Stop("time limit");
        }

        // ---- battery / dock ----

        /// <summary>Length of the NavMesh path from the robot to the dock (m), or -1.</summary>
        public float DistanceToDock()
        {
            if (!NavMesh.CalculatePath(Robot.Position, Dock, NavMesh.AllAreas, _homePath) ||
                _homePath.status != NavMeshPathStatus.PathComplete) return -1f;
            var c = _homePath.corners;
            float len = 0f;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        bool ShouldReturnToCharge()
        {
            if (Battery.Percent > 60f) return false;                 // cheap early-out
            float d = DistanceToDock();
            if (d < 0f) return false;
            return Battery.Percent <= Battery.PercentNeededFor(d, Robot.Spec.speed);
        }

        void BeginReturn(bool final, string endReason)
        {
            _finalReturn = final;
            _pendingEndReason = endReason;
            if (!final) _resumePoint = Robot.Position;
            _pendingGoal = null;
            Robot.ClearPath();

            var d = Robot.Position - Dock; d.y = 0f;
            if (d.sqrMagnitude < 0.05f * 0.05f) { ArriveDock(); return; }

            _single.Clear(); _single.Add(Dock);
            if (!Robot.SetPathThrough(_single))
            {
                Stop(final ? endReason : "dock unreachable");
                return;
            }
            Task = RobotTask.ReturningToDock;
        }

        void ArriveTransit()
        {
            if (Task == RobotTask.ReturningToDock) { ArriveDock(); return; }
            // back at the resume point
            Task = RobotTask.Cleaning;
            _lastStamp = Robot.Position;
            _nextReturnCheck = Stats.simTime;
        }

        void ArriveDock()
        {
            Robot.Heading = DockHeading;
            if (_finalReturn)
            {
                Task = RobotTask.Docked;
                Stop(_pendingEndReason);
                return;
            }
            Task = RobotTask.Charging;
            Stats.charges++;
        }

        void ResumeWork()
        {
            _single.Clear(); _single.Add(_resumePoint);
            if (Robot.SetPathThrough(_single)) Task = RobotTask.ReturningToWork;
            else { Task = RobotTask.Cleaning; _lastStamp = Robot.Position; }
        }

        // ---- planning ----

        /// <summary>Ask the planner for work. Returns false if the step should end (done / failed goal).</summary>
        bool Plan()
        {
            // make sure the exact arrival point is cleaned before asking for more work
            var here = Robot.Position;
            if ((here - _lastStamp).sqrMagnitude > 1e-8f)
            {
                Grid.Sweep(_lastStamp, here, Robot.Spec.brushRadius);
                _lastStamp = here;
            }

            // a goal that produced no motion counts as a failure (prevents livelock)
            if (_pendingGoal.HasValue)
            {
                if (Robot.Distance - _distAtPlan < 1e-3f)
                {
                    Stats.failedGoals++;
                    Planner.OnGoalFailed(_pendingGoal.Value);
                    if (++_consecutiveFailures > 200) { Stop("stuck"); return false; }
                }
                else _consecutiveFailures = 0;
                _pendingGoal = null;
            }

            _planWatch.Start();
            _waypoints.Clear();
            bool more = Planner.NextGoals(this, _waypoints);
            _planWatch.Stop();
            Stats.planningMs = (float)_planWatch.Elapsed.TotalMilliseconds;

            if (!more || _waypoints.Count == 0) { BeginReturn(true, "complete"); return false; }

            if (Robot.SetPathThrough(_waypoints))
            {
                Stats.goals++;
                _pendingGoal = _waypoints[_waypoints.Count - 1];
                _distAtPlan = Robot.Distance;
                return true;
            }

            Stats.failedGoals++;
            Planner.OnGoalFailed(_waypoints[0]);
            if (++_consecutiveFailures > 200) Stop("stuck");
            return false;
        }

        /// <summary>Run synchronously (headless). Returns wall-clock milliseconds.</summary>
        public float RunToEnd(float dt = 0.05f, float maxSimSeconds = -1f)
        {
            if (maxSimSeconds > 0) MaxSimTime = maxSimSeconds;
            if (State == SimState.Idle) Start();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (State == SimState.Running) Step(dt);
            return (float)sw.Elapsed.TotalMilliseconds;
        }

        void Sample()
        {
            UpdateStats();
            Stats.timeline.Add(new Vector2(Stats.simTime, Stats.coveragePercent));
            Stats.batteryTimeline.Add(new Vector2(Stats.simTime, Battery.Percent));
            _nextSample = Stats.simTime + TimelineInterval;
        }

        void UpdateStats()
        {
            var s = Grid.GetStats();
            Stats.coveragePercent = s.coveragePercent;
            Stats.coveredM2 = s.coveredM2;
            Stats.cleanableM2 = s.cleanableM2;
            Stats.averagePasses = s.averagePasses;
            Stats.overlapPercent = s.overlapPercent;
            Stats.distance = Robot.Distance;
            Stats.rotationDeg = Robot.RotationDeg;
            Stats.batteryPercent = Battery.Percent;
            if (Battery.Percent < Stats.minBattery) Stats.minBattery = Battery.Percent;
            if (Stats.timeTo50 < 0 && s.coveragePercent >= 50) Stats.timeTo50 = Stats.simTime;
            if (Stats.timeTo90 < 0 && s.coveragePercent >= 90) Stats.timeTo90 = Stats.simTime;
            if (Stats.timeTo95 < 0 && s.coveragePercent >= 95) Stats.timeTo95 = Stats.simTime;
        }
    }
}
