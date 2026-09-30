using UnityEngine;

namespace CleanerBot
{
    /// <summary>
    /// Tracks where the robot's brush has been and paints it on the floor.
    /// Runs after the house (NavMesh) and robot have initialised.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class CoverageManager : MonoBehaviour
    {
        [Header("References")]
        public HouseInstance house;
        public RobotController robot;
        public Shader overlayShader;

        [Header("Grid")]
        [Tooltip("Cell size in metres")] public float cellSize = 0.05f;
        [Tooltip("Cleaning (brush) radius in metres; must be <= NavMesh agent radius")] public float brushRadius = 0.16f;

        [Tooltip("Sweep from the robot transform every frame. SimulationRunner turns this off and sweeps inside the simulation.")]
        public bool autoSweepFromTransform = true;

        [Header("Display")]
        public bool showRemaining = true;
        public float overlayHeight = 0.004f;
        [Tooltip("Texture refresh interval (real-time seconds)")] public float refreshInterval = 0.1f;

        public CoverageGrid Grid { get; private set; }
        public CoveragePainter Painter { get; private set; }
        public bool Ready { get; private set; }
        public float InitMs { get; private set; }

        Vector3 _lastStamp;
        float _nextRefresh;

        void Start()
        {
            if (house == null) house = FindAnyObjectByType<HouseInstance>();
            if (robot == null) robot = FindAnyObjectByType<RobotController>();
            Initialize();
        }

        /// <summary>(Re)build grid + overlay for the current house. Call again after switching houses.</summary>
        public void Initialize()
        {
            Ready = false;
            Painter?.Dispose();
            if (house == null || robot == null || house.floor == null)
            {
                Debug.LogWarning("[CleanerBot] CoverageManager: missing house/robot/floor reference.");
                return;
            }
            if (overlayShader == null) overlayShader = Shader.Find("CleanerBot/CoverageOverlay");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var b = house.floor.GetComponent<Renderer>().bounds;
            Grid = new CoverageGrid(new Vector2(b.min.x, b.min.z), new Vector2(b.size.x, b.size.z), cellSize);
            Grid.BuildCleanableMask(robot.transform.position, brushRadius);
            Painter = new CoveragePainter(Grid, overlayShader, transform, overlayHeight);
            InitMs = (float)sw.Elapsed.TotalMilliseconds;

            _lastStamp = robot.transform.position;
            Grid.Stamp(_lastStamp, brushRadius);
            Painter.Refresh(Grid, showRemaining);
            Grid.Dirty = false;
            Ready = true;
        }

        public void ResetCoverage()
        {
            if (!Ready) return;
            Grid.Clear();
            _lastStamp = robot.transform.position;
            Painter.Refresh(Grid, showRemaining);
        }

        public CoverageStats GetStats() => Ready ? Grid.GetStats() : default;

        void LateUpdate()
        {
            if (!Ready) return;

            var p = robot.transform.position;
            float dx = p.x - _lastStamp.x, dz = p.z - _lastStamp.z;
            if (autoSweepFromTransform && dx * dx + dz * dz >= (cellSize * 0.5f) * (cellSize * 0.5f))
            {
                Grid.Sweep(_lastStamp, p, brushRadius);
                _lastStamp = p;
            }

            if (Grid.Dirty && Time.unscaledTime >= _nextRefresh)
            {
                Painter.Refresh(Grid, showRemaining);
                Grid.Dirty = false;
                _nextRefresh = Time.unscaledTime + refreshInterval;
            }
        }

        /// <summary>Repaint the overlay immediately.</summary>
        public void RefreshNow()
        {
            if (!Ready) return;
            Painter.Refresh(Grid, showRemaining);
            Grid.Dirty = false;
        }

        public void SetShowRemaining(bool show)
        {
            showRemaining = show;
            if (Ready) Painter.Refresh(Grid, showRemaining);
        }

        void OnDestroy() => Painter?.Dispose();
    }
}
