using System.Collections.Generic;
using UnityEngine;

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

    [System.Serializable]
    public class SimStats
    {
        public string planner;
        public string endReason = "";
        public float simTime;           // s
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
        public List<Vector2> timeline = new List<Vector2>();   // (simTime, coverage%) samples
    }

    /// <summary>
    /// Fixed-step cleaning simulation: robot kinematics + coverage + planner.
    /// Has no MonoBehaviour dependency, so it can run headless (CLI batch) or be ticked by SimulationRunner.
    /// </summary>
    public class SimulationCore
    {
        public readonly CoverageGrid Grid;
        public readonly RobotSim Robot;
        public readonly ICleaningPlanner Planner;
        public readonly SimStats Stats = new SimStats();

        public SimState State { get; private set; } = SimState.Idle;
        public float MaxSimTime = 3 * 3600f;        // safety stop (s)
        public float TargetCoverage = 99.5f;        // %
        public float TimelineInterval = 10f;        // s

        Vector3 _lastStamp;
        Vector3? _pendingGoal;
        float _distAtPlan;
        float _nextSample;
        int _consecutiveFailures;
        readonly List<Vector3> _waypoints = new List<Vector3>();
        readonly System.Diagnostics.Stopwatch _planWatch = new System.Diagnostics.Stopwatch();

        public SimulationCore(CoverageGrid grid, RobotSim robot, ICleaningPlanner planner)
        {
            Grid = grid;
            Robot = robot;
            Planner = planner;
            Stats.planner = planner.Name;
        }

        public void Start()
        {
            Grid.Clear();
            _lastStamp = Robot.Position;
            Grid.Stamp(_lastStamp, Robot.Spec.brushRadius);
            _nextSample = 0f;
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
        }

        public void Step(float dt)
        {
            if (State != SimState.Running) return;

            if (!Robot.HasPath && !Plan()) return;

            Robot.Step(dt);
            Stats.simTime += dt;

            var p = Robot.Position;
            float dx = p.x - _lastStamp.x, dz = p.z - _lastStamp.z;
            float h = Grid.CellSize * 0.5f;
            if (dx * dx + dz * dz >= h * h)
            {
                Grid.Sweep(_lastStamp, p, Robot.Spec.brushRadius);
                _lastStamp = p;
            }

            UpdateStats();                                  // O(1): counters are maintained by the grid
            if (Stats.simTime >= _nextSample) Sample();
            if (Stats.coveragePercent >= TargetCoverage) Stop("target coverage");
            else if (Stats.simTime >= MaxSimTime) Stop("time limit");
        }

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

            if (!more || _waypoints.Count == 0) { Stop("complete"); return false; }

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
            if (Stats.timeTo50 < 0 && s.coveragePercent >= 50) Stats.timeTo50 = Stats.simTime;
            if (Stats.timeTo90 < 0 && s.coveragePercent >= 90) Stats.timeTo90 = Stats.simTime;
            if (Stats.timeTo95 < 0 && s.coveragePercent >= 95) Stats.timeTo95 = Stats.simTime;
        }
    }
}
