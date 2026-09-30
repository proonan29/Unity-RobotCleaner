using System;
using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    /// <summary>
    /// Play-mode driver: advances SimulationCore in fixed steps (speedMultiplier x real time)
    /// and mirrors the simulated robot onto the robot GameObject. The NavMeshAgent is disabled while this runs.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class SimulationRunner : MonoBehaviour
    {
        [Header("References")]
        public CoverageManager coverage;
        public RobotController robot;

        [Header("Simulation")]
        public string planner = "Frontier";
        public int seed = 1;
        [Range(0.1f, 100f)] public float speedMultiplier = 4f;
        [Tooltip("Simulation step (s)")] public float fixedStep = 0.05f;
        public bool autoStart = true;
        public float maxSimMinutes = 180f;
        public RobotSpec spec = RobotSpec.Default;

        public SimulationCore Core { get; private set; }
        public event Action<SimulationCore> Finished;

        Vector3 _home;
        float _homeHeading;
        float _accum;
        bool _finishedRaised;
        LineRenderer _line;

        void Start()
        {
            if (coverage == null) coverage = FindAnyObjectByType<CoverageManager>();
            if (robot == null) robot = FindAnyObjectByType<RobotController>();
            if (robot == null || coverage == null)
            {
                Debug.LogWarning("[CleanerBot] SimulationRunner: missing robot/coverage.");
                enabled = false;
                return;
            }

            _home = robot.transform.position;
            _homeHeading = robot.transform.eulerAngles.y;
            var agent = robot.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;       // the simulation owns the transform
            robot.enabled = false;
            coverage.autoSweepFromTransform = false;
            _line = robot.GetComponentInChildren<LineRenderer>();

            if (autoStart) StartCleaning();
        }

        /// <summary>(Re)start cleaning from the dock position with the given planner.</summary>
        public void StartCleaning(string plannerName = null)
        {
            if (!string.IsNullOrEmpty(plannerName)) planner = plannerName;
            if (coverage == null || !coverage.Ready) return;

            var start = NavMesh.SamplePosition(_home, out var hit, 1f, NavMesh.AllAreas) ? hit.position : _home;
            var sim = new RobotSim(spec, start, _homeHeading);
            Core = new SimulationCore(coverage.Grid, sim, PlannerFactory.Create(planner, seed))
            {
                MaxSimTime = maxSimMinutes * 60f,
            };
            Core.Start();
            _accum = 0f;
            _finishedRaised = false;
            coverage.Grid.Dirty = true;
            ApplyTransform();
        }

        public void SetPaused(bool paused) => Core?.Pause(paused);

        void Update()
        {
            if (Core == null) return;
            if (Core.State == SimState.Running)
            {
                _accum += Time.deltaTime * speedMultiplier;
                int budget = 20000;
                while (_accum >= fixedStep && budget-- > 0)
                {
                    Core.Step(fixedStep);
                    _accum -= fixedStep;
                    if (Core.State != SimState.Running) break;
                }
                if (budget <= 0) _accum = 0f;
                ApplyTransform();
            }

            if (Core.State == SimState.Done && !_finishedRaised)
            {
                _finishedRaised = true;
                Debug.Log($"[CleanerBot] Cleaning finished ({Core.Stats.endReason}): " +
                          $"{Core.Stats.coveragePercent:0.0}% in {Core.Stats.simTime / 60f:0.0} min, {Core.Stats.distance:0.0} m");
                Finished?.Invoke(Core);
            }
        }

        /// <summary>Advance the simulation synchronously by simSeconds (works even when the player loop is stalled).</summary>
        public void Advance(float simSeconds)
        {
            if (Core == null) return;
            int n = Mathf.CeilToInt(simSeconds / fixedStep);
            for (int i = 0; i < n && Core.State == SimState.Running; i++) Core.Step(fixedStep);
            ApplyTransform();
            coverage.RefreshNow();
        }

        void ApplyTransform()
        {
            var r = Core.Robot;
            robot.transform.SetPositionAndRotation(r.Position, Quaternion.Euler(0f, r.Heading, 0f));

            if (_line == null) return;
            var path = r.Path;
            int from = r.PathIndex;
            if (!r.HasPath) { _line.positionCount = 0; return; }
            _line.positionCount = path.Count - from + 1;
            _line.SetPosition(0, r.Position + Vector3.up * 0.02f);
            for (int i = from; i < path.Count; i++)
                _line.SetPosition(i - from + 1, path[i] + Vector3.up * 0.02f);
        }
    }
}
