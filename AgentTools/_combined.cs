using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System;
using Unity.AI.Navigation;
using Unity.Pipeline.Commands;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace CleanerBot.EditorTools
{
    /// <summary>Material provider that persists materials as assets (so scenes/prefabs reference real files).</summary>
    public class AssetMaterialProvider : IMaterialProvider
    {
        public const string Folder = "Assets/CleanerBot/Materials";

        public Material Get(string key, Color color)
        {
            EnsureFolder(Folder);
            string path = $"{Folder}/{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Prims.FindLitShader()) { name = key };
                m.SetColor("_BaseColor", color);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>
    /// CLI commands for the CleanerBot simulator. Discover with `unity list`, run with `unity command cb_*`.
    /// </summary>
    public static class CleanerBotCommands
    {
        const string NavMeshFolder = "Assets/CleanerBot/NavMesh";
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        [CliCommand("cb_list_houses", "List the built-in (prefilled) house presets.")]
        public static string ListHouses() => string.Join(", ", HousePresets.Names);

        [CliCommand("cb_setup_scene", "Create a CleanerBot scene: light, camera, house preset, baked NavMesh and the robot. Saves the scene.")]
        public static string SetupScene(
            [CliArg("preset", "House preset name (see cb_list_houses)")] string preset = "Apartment_A",
            [CliArg("scene_path", "Scene asset path")] string scenePath = "Assets/CleanerBot/Scenes/CleanerBot.unity")
        {
            var layout = HousePresets.Get(preset);
            var mats = new AssetMaterialProvider();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Light
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.50f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.26f);

            // House + NavMesh (robot-sized agent first)
            ConfigureAgent();
            var house = HouseGenerator.Build(layout, mats);
            var bake = BakeAndSave(house);

            // Camera framing the whole house
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            camGo.AddComponent<AudioListener>();
            FrameHouse(cam, house);

            // Robot
            var robot = RobotFactory.Create(house.robotSpawn, mats);
            EnsureCoverage(house, robot);

            AssetMaterialProvider.EnsureFolder(Path.GetDirectoryName(scenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, scenePath);
            AddSceneToBuild(scenePath);
            AssetDatabase.SaveAssets();

            return $"scene={scenePath} house={layout.name} furniture={layout.furniture.Count} {bake} robot={robot.name}@({F(house.robotSpawn.x)},{F(house.robotSpawn.z)})";
        }

        [CliCommand("cb_rebake", "Rebake the house NavMesh in the active scene and save it as an asset.")]
        public static string Rebake()
        {
            var house = UnityEngine.Object.FindAnyObjectByType<HouseInstance>();
            if (house == null) return "error: no HouseInstance in the active scene";
            var r = BakeAndSave(house);
            EditorSceneManager.MarkSceneDirty(house.gameObject.scene);
            EditorSceneManager.SaveScene(house.gameObject.scene);
            return r;
        }

        [CliCommand("cb_navmesh_info", "Report walkable NavMesh area and agent settings.")]
        public static string NavMeshInfo()
        {
            float area = HouseInstance.NavMeshArea(out int tris);
            var s = NavMesh.GetSettingsByID(0);
            return $"walkable_m2={F(area)} triangles={tris} agentRadius={F(s.agentRadius)} agentHeight={F(s.agentHeight)} agentClimb={F(s.agentClimb)}";
        }

        [CliCommand("cb_frame_camera", "Re-frame Main Camera over the house.")]
        public static string FrameCamera(
            [CliArg("pitch", "Camera pitch in degrees (90 = top-down)")] float pitch = 60f)
        {
            var house = UnityEngine.Object.FindAnyObjectByType<HouseInstance>();
            var cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (house == null || cam == null) return "error: need a HouseInstance and a Camera";
            FrameHouse(cam, house, pitch);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
            return $"camera pos={cam.transform.position} rot={cam.transform.eulerAngles}";
        }

        [CliCommand("cb_configure_agent", "Set the default NavMesh agent type (id 0) to robot-vacuum dimensions and rename it 'CleanerBot'.")]
        public static string ConfigureAgent(
            [CliArg("radius", "Agent radius (m)")] float radius = 0.17f,
            [CliArg("height", "Agent height (m)")] float height = 0.1f,
            [CliArg("climb", "Max step height (m)")] float climb = 0.02f,
            [CliArg("slope", "Max slope (deg)")] float slope = 10f)
        {
            var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (objs == null || objs.Length == 0) return "error: NavMeshAreas.asset not found";
            var so = new SerializedObject(objs[0]);
            var settings = so.FindProperty("m_Settings");
            var names = so.FindProperty("m_SettingNames");
            bool found = false;
            for (int i = 0; i < settings.arraySize; i++)
            {
                var el = settings.GetArrayElementAtIndex(i);
                if (el.FindPropertyRelative("agentTypeID").intValue != 0) continue;
                el.FindPropertyRelative("agentRadius").floatValue = radius;
                el.FindPropertyRelative("agentHeight").floatValue = height;
                el.FindPropertyRelative("agentClimb").floatValue = climb;
                el.FindPropertyRelative("agentSlope").floatValue = slope;
                el.FindPropertyRelative("minRegionArea").floatValue = 0.5f;
                if (names != null && i < names.arraySize) names.GetArrayElementAtIndex(i).stringValue = "CleanerBot";
                found = true;
            }
            if (!found) return "error: agent type 0 not found";
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            var s = NavMesh.GetSettingsByID(0);
            return $"agent0 name={NavMesh.GetSettingsNameFromID(0)} radius={F(s.agentRadius)} height={F(s.agentHeight)} climb={F(s.agentClimb)} slope={F(s.agentSlope)}";
        }

        // ---- Coverage (phase 2) ----

        const string CoverageShaderPath = "Assets/CleanerBot/Shaders/CoverageOverlay.shader";

        static CoverageManager EnsureCoverage(HouseInstance house, RobotController robot)
        {
            var cm = UnityEngine.Object.FindAnyObjectByType<CoverageManager>();
            if (cm == null) cm = new GameObject("Coverage").AddComponent<CoverageManager>();
            cm.house = house;
            cm.robot = robot;
            cm.overlayShader = AssetDatabase.LoadAssetAtPath<Shader>(CoverageShaderPath);
            EditorUtility.SetDirty(cm);
            return cm;
        }

        [CliCommand("cb_add_coverage", "Add/refresh the CoverageManager in the active scene (edit mode) and save the scene.")]
        public static string AddCoverage()
        {
            if (Application.isPlaying) return "error: exit play mode first";
            var house = UnityEngine.Object.FindAnyObjectByType<HouseInstance>();
            var robot = UnityEngine.Object.FindAnyObjectByType<RobotController>();
            if (house == null || robot == null) return "error: need HouseInstance and RobotController in the scene";
            var cm = EnsureCoverage(house, robot);
            EditorSceneManager.MarkSceneDirty(cm.gameObject.scene);
            EditorSceneManager.SaveScene(cm.gameObject.scene);
            return $"coverage={cm.name} shader={(cm.overlayShader != null ? cm.overlayShader.name : "MISSING")} cell={F(cm.cellSize)} brush={F(cm.brushRadius)}";
        }

        [CliCommand("cb_coverage", "Play mode: coverage statistics (cleanable area, covered area, %, passes).")]
        public static string Coverage()
        {
            var cm = UnityEngine.Object.FindAnyObjectByType<CoverageManager>();
            if (cm == null) return "error: no CoverageManager";
            if (!cm.Ready) return "error: coverage not initialised (enter play mode)";
            var s = cm.GetStats();
            var g = cm.Grid;
            var r = UnityEngine.Object.FindAnyObjectByType<RobotController>();
            return $"coverage={F(s.coveragePercent)}% covered_m2={F(s.coveredM2)} cleanable_m2={F(s.cleanableM2)} " +
                   $"avg_passes={F(s.averagePasses)} overlap={F(s.overlapPercent)}% grid={g.Width}x{g.Height}@{F(g.CellSize)}m " +
                   $"init_ms={F(cm.InitMs)} distance={(r != null ? F(r.DistanceTraveled) : "-")} sim_time={F(Time.time)}";
        }

        [CliCommand("cb_coverage_reset", "Play mode: clear cleaning progress (keeps the cleanable mask).")]
        public static string CoverageReset()
        {
            var cm = UnityEngine.Object.FindAnyObjectByType<CoverageManager>();
            if (cm == null || !cm.Ready) return "error: coverage not ready";
            cm.ResetCoverage();
            return "coverage reset";
        }

        [CliCommand("cb_coverage_export", "Play mode: save the coverage map texture as PNG (path relative to project root).")]
        public static string CoverageExport([CliArg("path", "Output PNG path")] string path = "Screenshots/coverage_map.png")
        {
            var cm = UnityEngine.Object.FindAnyObjectByType<CoverageManager>();
            if (cm == null || !cm.Ready) return "error: coverage not ready";
            string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, cm.Painter.Texture.EncodeToPNG());
            return $"saved {full} ({cm.Grid.Width}x{cm.Grid.Height})";
        }

        // ---- Play-mode helpers (drive / inspect the robot) ----

        [CliCommand("cb_robot_status", "Robot position, goal, odometry and NavMesh state.")]
        public static string RobotStatus()
        {
            var r = UnityEngine.Object.FindAnyObjectByType<RobotController>();
            if (r == null) return "error: no RobotController";
            var a = r.Agent != null ? r.Agent : r.GetComponent<NavMeshAgent>();
            var p = r.transform.position;
            string goal = r.CurrentGoal.HasValue ? $"({F(r.CurrentGoal.Value.x)},{F(r.CurrentGoal.Value.z)})" : "none";
            bool on = a != null && a.isActiveAndEnabled && a.isOnNavMesh;
            return $"playing={Application.isPlaying} mode={r.mode} pos=({F(p.x)},{F(p.z)}) goal={goal} " +
                   $"onNavMesh={on} remaining={(on && a.hasPath ? F(a.remainingDistance) : "-")} " +
                   $"distance={F(r.DistanceTraveled)} goalsReached={r.GoalsReached} timeScale={F(Time.timeScale)}";
        }

        [CliCommand("cb_robot_goto", "Play mode: send the robot to (x, z). Switches it to Idle mode.")]
        public static string RobotGoto(
            [CliArg("x", "Target X (m)")] float x,
            [CliArg("z", "Target Z (m)")] float z)
        {
            if (!Application.isPlaying) return "error: enter play mode first (editor_play)";
            var r = UnityEngine.Object.FindAnyObjectByType<RobotController>();
            if (r == null) return "error: no RobotController";
            r.mode = RobotController.Mode.Idle;
            bool ok = r.MoveTo(new Vector3(x, 0f, z));
            return ok ? $"ok: path to ({F(x)},{F(z)}) corners={r.Agent.path.corners.Length}" : $"unreachable: ({F(x)},{F(z)})";
        }

        [CliCommand("cb_robot_mode", "Play mode: set robot mode Idle | Wander | ClickToMove.")]
        public static string RobotMode([CliArg("mode", "Idle | Wander | ClickToMove")] string mode)
        {
            var r = UnityEngine.Object.FindAnyObjectByType<RobotController>();
            if (r == null) return "error: no RobotController";
            if (!System.Enum.TryParse(mode, true, out RobotController.Mode m)) return $"error: unknown mode '{mode}'";
            r.mode = m;
            if (m == RobotController.Mode.Idle && Application.isPlaying) r.Stop();
            return $"mode={r.mode}";
        }

        // ---- helpers ----

        static string BakeAndSave(HouseInstance house)
        {
            var surface = house.surface;
            surface.BuildNavMesh();
            var data = surface.navMeshData;
            if (data == null) return "navmesh=FAILED";

            AssetMaterialProvider.EnsureFolder(NavMeshFolder);
            string path = $"{NavMeshFolder}/NavMesh_{house.layoutName}.asset";
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != null) AssetDatabase.DeleteAsset(path);
            data.name = "NavMesh_" + house.layoutName;
            AssetDatabase.CreateAsset(data, path);
            EditorUtility.SetDirty(surface);

            float area = HouseInstance.NavMeshArea(out int tris);
            return $"navmesh={path} walkable_m2={F(area)} triangles={tris}";
        }

        static void FrameHouse(Camera cam, HouseInstance house, float pitch = 60f)
        {
            var b = house.floorBounds;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float span = Mathf.Max(b.size.z, b.size.x / Mathf.Max(cam.aspect, 1.3f));
            float dist = (span * 0.5f + 0.8f) / Mathf.Tan(halfFov);
            var rot = Quaternion.Euler(pitch, 0f, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = b.center - rot * Vector3.forward * dist;
        }

        static void AddSceneToBuild(string scenePath)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == scenePath)) return;
            list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}

namespace CleanerBot
{
    /// <summary>
    /// Floor coverage map. Source of truth for "where has the robot cleaned".
    /// - Cells are square (default 5 cm) over the floor's XZ extent.
    /// - Cleanable = cells the robot's brush can physically reach:
    ///   NavMesh cells connected to the robot's start, dilated by the brush radius.
    /// - Passes = how many separate times the brush went over a cell
    ///   (staying under the brush across consecutive stamps counts as one pass).
    /// </summary>
    public class CoverageGrid
    {
        public readonly int Width, Height;
        public readonly float CellSize;
        public readonly Vector2 Origin;     // world XZ of cell (0,0) min corner

        public readonly bool[] Cleanable;
        public readonly ushort[] Passes;
        readonly int[] _lastStep;
        int _step = 1;

        public int CleanableCount { get; private set; }
        public int CoveredCount { get; private set; }
        public int OverlapCount { get; private set; }    // cells with >= 2 passes
        public long TotalPasses { get; private set; }
        public bool Dirty { get; set; } = true;

        public int CellCount => Width * Height;
        public float CellArea => CellSize * CellSize;

        public CoverageGrid(Vector2 origin, Vector2 size, float cellSize)
        {
            CellSize = cellSize;
            Origin = origin;
            Width = Mathf.Max(1, Mathf.CeilToInt(size.x / cellSize));
            Height = Mathf.Max(1, Mathf.CeilToInt(size.y / cellSize));
            Cleanable = new bool[Width * Height];
            Passes = new ushort[Width * Height];
            _lastStep = new int[Width * Height];
        }

        public int Index(int x, int y) => y * Width + x;

        public Vector3 CellCenter(int x, int y) =>
            new Vector3(Origin.x + (x + 0.5f) * CellSize, 0f, Origin.y + (y + 0.5f) * CellSize);

        public bool WorldToCell(Vector3 p, out int x, out int y)
        {
            x = Mathf.FloorToInt((p.x - Origin.x) / CellSize);
            y = Mathf.FloorToInt((p.z - Origin.y) / CellSize);
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        /// <summary>Compute the cleanable mask. Call once after the NavMesh is available.</summary>
        public void BuildCleanableMask(Vector3 seedWorld, float brushRadius)
        {
            int n = CellCount;
            var onMesh = new bool[n];
            float half = CellSize * 0.5f;

            // 1) Cell centres that lie on the NavMesh at floor level
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    var c = CellCenter(x, y);
                    if (NavMesh.SamplePosition(c, out var hit, 0.12f, NavMesh.AllAreas))
                    {
                        float dx = hit.position.x - c.x, dz = hit.position.z - c.z;
                        if (dx * dx + dz * dz <= half * half && Mathf.Abs(hit.position.y) < 0.1f)
                            onMesh[Index(x, y)] = true;
                    }
                }

            // 2) Flood fill from the robot's start (drops islands the robot can't reach, e.g. under a bed)
            var reach = new bool[n];
            if (FindSeed(seedWorld, onMesh, out int seed))
            {
                var q = new Queue<int>();
                q.Enqueue(seed);
                reach[seed] = true;
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    int x = i % Width, y = i / Width;
                    TryPush(x + 1, y); TryPush(x - 1, y); TryPush(x, y + 1); TryPush(x, y - 1);
                }

                void TryPush(int x, int y)
                {
                    if (x < 0 || y < 0 || x >= Width || y >= Height) return;
                    int j = Index(x, y);
                    if (reach[j] || !onMesh[j]) return;
                    reach[j] = true;
                    q.Enqueue(j);
                }
            }

            // 3) Dilate by the brush radius: the brush reaches beyond the robot centre
            Array.Clear(Cleanable, 0, n);
            int r = Mathf.CeilToInt(brushRadius / CellSize);
            float r2 = (brushRadius / CellSize) * (brushRadius / CellSize);
            var offsets = new List<Vector2Int>();
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= r2) offsets.Add(new Vector2Int(dx, dy));

            for (int i = 0; i < n; i++)
            {
                if (!reach[i]) continue;
                int x = i % Width, y = i / Width;
                foreach (var o in offsets)
                {
                    int xx = x + o.x, yy = y + o.y;
                    if (xx < 0 || yy < 0 || xx >= Width || yy >= Height) continue;
                    Cleanable[Index(xx, yy)] = true;
                }
            }

            CleanableCount = 0;
            for (int i = 0; i < n; i++) if (Cleanable[i]) CleanableCount++;
            Clear();
        }

        bool FindSeed(Vector3 p, bool[] onMesh, out int seed)
        {
            seed = -1;
            if (!WorldToCell(p, out int cx, out int cy)) return false;
            for (int rad = 0; rad < 20; rad++)
                for (int dy = -rad; dy <= rad; dy++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                        if (onMesh[Index(x, y)]) { seed = Index(x, y); return true; }
                    }
            return false;
        }

        /// <summary>Reset cleaning progress (keeps the cleanable mask).</summary>
        public void Clear()
        {
            Array.Clear(Passes, 0, Passes.Length);
            Array.Clear(_lastStep, 0, _lastStep.Length);
            _step = 1;
            CoveredCount = OverlapCount = 0;
            TotalPasses = 0;
            Dirty = true;
        }

        /// <summary>Stamp the brush along a straight segment (spacing = half a cell, so no gaps at any speed).</summary>
        public void Sweep(Vector3 from, Vector3 to, float radius)
        {
            float d = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
            int steps = Mathf.Max(1, Mathf.CeilToInt(d / (CellSize * 0.5f)));
            for (int s = 1; s <= steps; s++)
                Stamp(Vector3.Lerp(from, to, (float)s / steps), radius);
        }

        /// <summary>Apply the circular brush once at p.</summary>
        public void Stamp(Vector3 p, float radius)
        {
            _step++;
            int x0 = Mathf.FloorToInt((p.x - radius - Origin.x) / CellSize);
            int x1 = Mathf.FloorToInt((p.x + radius - Origin.x) / CellSize);
            int y0 = Mathf.FloorToInt((p.z - radius - Origin.y) / CellSize);
            int y1 = Mathf.FloorToInt((p.z + radius - Origin.y) / CellSize);
            float r2 = radius * radius;

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(Height - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Width - 1, x1); x++)
                {
                    var c = CellCenter(x, y);
                    float dx = c.x - p.x, dz = c.z - p.z;
                    if (dx * dx + dz * dz > r2) continue;
                    int i = Index(x, y);
                    if (!Cleanable[i]) continue;

                    if (_lastStep[i] < _step - 1)      // wasn't under the brush on the previous stamp -> new pass
                    {
                        if (Passes[i] == 0) CoveredCount++;
                        else if (Passes[i] == 1) OverlapCount++;
                        if (Passes[i] < ushort.MaxValue) Passes[i]++;
                        TotalPasses++;
                        Dirty = true;
                    }
                    _lastStep[i] = _step;
                }
        }

        public CoverageStats GetStats()
        {
            return new CoverageStats
            {
                cleanableM2 = CleanableCount * CellArea,
                coveredM2 = CoveredCount * CellArea,
                coveragePercent = CleanableCount > 0 ? 100f * CoveredCount / CleanableCount : 0f,
                averagePasses = CoveredCount > 0 ? (float)TotalPasses / CoveredCount : 0f,
                overlapPercent = CoveredCount > 0 ? 100f * OverlapCount / CoveredCount : 0f,
            };
        }
    }

    [Serializable]
    public struct CoverageStats
    {
        public float cleanableM2;
        public float coveredM2;
        public float coveragePercent;
        public float averagePasses;     // over covered cells
        public float overlapPercent;    // % of covered cells cleaned 2+ times
    }
}

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
            if (dx * dx + dz * dz >= (cellSize * 0.5f) * (cellSize * 0.5f))
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

        public void SetShowRemaining(bool show)
        {
            showRemaining = show;
            if (Ready) Painter.Refresh(Grid, showRemaining);
        }

        void OnDestroy() => Painter?.Dispose();
    }
}

namespace CleanerBot
{
    /// <summary>
    /// Renders a CoverageGrid as a transparent overlay just above the floor.
    /// Colour = number of passes (1 = light teal ... 4+ = deep blue). Remaining (uncleaned) area is optionally tinted.
    /// </summary>
    public class CoveragePainter
    {
        public static readonly Color32 Remaining = new Color32(255, 196, 90, 60);   // "dust" still to clean
        public static readonly Color32[] PassColors =
        {
            new Color32(0, 0, 0, 0),            // 0 passes
            new Color32(80, 215, 200, 150),     // 1
            new Color32(40, 160, 220, 185),     // 2
            new Color32(30, 100, 210, 210),     // 3
            new Color32(60, 40, 170, 230),      // 4+
        };

        public Texture2D Texture => _tex;
        public GameObject Overlay => _quad;

        readonly Texture2D _tex;
        readonly Color32[] _px;
        readonly Material _mat;
        readonly GameObject _quad;

        public CoveragePainter(CoverageGrid grid, Shader shader, Transform parent, float height)
        {
            _tex = new Texture2D(grid.Width, grid.Height, TextureFormat.RGBA32, false)
            {
                name = "CoverageTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _px = new Color32[grid.CellCount];

            var size = new Vector2(grid.Width * grid.CellSize, grid.Height * grid.CellSize);
            _mat = new Material(shader) { name = "CoverageOverlay (runtime)" };
            _mat.SetTexture("_CoverageTex", _tex);
            _mat.SetVector("_Rect", new Vector4(grid.Origin.x, grid.Origin.y, size.x, size.y));

            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "CoverageOverlay";
            var col = _quad.GetComponent<Collider>();
            if (col != null) Kill(col);          // must not block raycasts / NavMesh
            _quad.transform.SetParent(parent, false);
            _quad.transform.position = new Vector3(grid.Origin.x + size.x * 0.5f, height, grid.Origin.y + size.y * 0.5f);
            _quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = _quad.GetComponent<Renderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public void Refresh(CoverageGrid grid, bool showRemaining)
        {
            var passes = grid.Passes;
            var clean = grid.Cleanable;
            var clear = PassColors[0];
            int maxIdx = PassColors.Length - 1;
            for (int i = 0; i < _px.Length; i++)
            {
                int p = passes[i];
                if (p > 0) _px[i] = PassColors[p < maxIdx ? p : maxIdx];
                else _px[i] = showRemaining && clean[i] ? Remaining : clear;
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        public void SetVisible(bool visible) => _quad.SetActive(visible);

        public void Dispose()
        {
            if (_quad != null) Kill(_quad);
            if (_mat != null) Kill(_mat);
            if (_tex != null) Kill(_tex);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}

namespace CleanerBot
{
    /// <summary>
    /// Procedural placeholder furniture built from primitives (swap for Asset Store models later).
    /// Local frame: footprint centred on the origin, +Z = front (the side you sit on / face).
    /// Legged pieces leave floor space underneath that a ~10 cm robot can clean.
    /// </summary>
    public static class FurnitureFactory
    {
        static readonly Color Fabric = new Color(0.36f, 0.42f, 0.52f);
        static readonly Color FabricDark = new Color(0.28f, 0.32f, 0.40f);
        static readonly Color WoodLight = new Color(0.72f, 0.56f, 0.38f);
        static readonly Color WoodDark = new Color(0.42f, 0.29f, 0.19f);
        static readonly Color White = new Color(0.92f, 0.92f, 0.92f);
        static readonly Color Metal = new Color(0.55f, 0.57f, 0.60f);
        static readonly Color Black = new Color(0.08f, 0.08f, 0.09f);
        static readonly Color Linen = new Color(0.90f, 0.87f, 0.80f);
        static readonly Color Leaf = new Color(0.25f, 0.55f, 0.28f);
        static readonly Color Pot = new Color(0.70f, 0.42f, 0.30f);

        public static GameObject Create(FurnitureSpec f, IMaterialProvider mats)
        {
            var root = new GameObject(f.kind);
            root.transform.localPosition = new Vector3(f.pos.x, 0f, f.pos.y);
            root.transform.localRotation = Quaternion.Euler(0f, f.rotY, 0f);
            var p = root.transform;

            switch (f.kind)
            {
                case "sofa": Sofa(p, mats, 2.0f); break;
                case "armchair": Sofa(p, mats, 0.9f); break;
                case "coffee_table": Table(p, mats, 1.1f, 0.6f, 0.42f, WoodLight, "WoodLight"); break;
                case "dining_table": Table(p, mats, 1.4f, 0.8f, 0.74f, WoodDark, "WoodDark"); break;
                case "desk": Table(p, mats, 1.2f, 0.6f, 0.74f, White, "White"); break;
                case "chair": Chair(p, mats); break;
                case "tv_stand": TvStand(p, mats); break;
                case "bookshelf": Solid(p, mats, 0.9f, 0.3f, 1.8f, WoodDark, "WoodDark"); break;
                case "kitchen_counter":
                    {
                        float sx = f.size.x > 0 ? f.size.x : 2.0f, sz = f.size.y > 0 ? f.size.y : 0.6f;
                        Solid(p, mats, sx, sz, 0.86f, White, "White");
                        Prims.Box(p, "Worktop", new Vector3(0, 0.88f, 0), new Vector3(sx, 0.04f, sz), mats.Get("Metal", Metal));
                        break;
                    }
                case "fridge": Solid(p, mats, 0.7f, 0.7f, 1.8f, Metal, "Metal"); break;
                case "bed": Bed(p, mats); break;
                case "nightstand": Solid(p, mats, 0.45f, 0.4f, 0.5f, WoodLight, "WoodLight"); break;
                case "wardrobe": Solid(p, mats, 1.2f, 0.6f, 2.0f, WoodLight, "WoodLight"); break;
                case "plant": Plant(p, mats); break;
                default:
                    Debug.LogWarning($"[CleanerBot] Unknown furniture kind '{f.kind}', using a placeholder box.");
                    Solid(p, mats, 0.5f, 0.5f, 0.5f, Color.magenta, "Unknown");
                    break;
            }
            return root;
        }

        /// <summary>Box that sits on the floor (robot cannot go under).</summary>
        static void Solid(Transform p, IMaterialProvider mats, float sx, float sz, float h, Color c, string key)
        {
            Prims.Box(p, "Body", new Vector3(0, h * 0.5f, 0), new Vector3(sx, h, sz), mats.Get(key, c));
        }

        static void Legs(Transform p, IMaterialProvider mats, float sx, float sz, float h, float leg, float inset, Color c, string key)
        {
            var m = mats.Get(key, c);
            float x = sx * 0.5f - inset - leg * 0.5f, z = sz * 0.5f - inset - leg * 0.5f;
            int i = 0;
            foreach (var sxn in new[] { -1f, 1f })
                foreach (var szn in new[] { -1f, 1f })
                    Prims.Box(p, $"Leg_{i++}", new Vector3(sxn * x, h * 0.5f, szn * z), new Vector3(leg, h, leg), m);
        }

        static void Table(Transform p, IMaterialProvider mats, float sx, float sz, float h, Color c, string key)
        {
            const float top = 0.04f;
            Prims.Box(p, "Top", new Vector3(0, h - top * 0.5f, 0), new Vector3(sx, top, sz), mats.Get(key, c));
            Legs(p, mats, sx, sz, h - top, 0.05f, 0.04f, c, key);
        }

        static void Chair(Transform p, IMaterialProvider mats)
        {
            const float s = 0.45f, seatH = 0.45f;
            Prims.Box(p, "Seat", new Vector3(0, seatH - 0.02f, 0), new Vector3(s, 0.04f, s), mats.Get("WoodDark", WoodDark));
            Prims.Box(p, "Back", new Vector3(0, seatH + 0.22f, -s * 0.5f + 0.02f), new Vector3(s, 0.4f, 0.04f), mats.Get("WoodDark", WoodDark));
            Legs(p, mats, s, s, seatH - 0.04f, 0.035f, 0.01f, WoodDark, "WoodDark");
        }

        /// <summary>Skirted sofa/armchair (solid to the floor), seat facing +Z.</summary>
        static void Sofa(Transform p, IMaterialProvider mats, float width)
        {
            const float depth = 0.9f;
            var fabric = mats.Get("Fabric", Fabric);
            var dark = mats.Get("FabricDark", FabricDark);
            Prims.Box(p, "Base", new Vector3(0, 0.21f, 0), new Vector3(width, 0.42f, depth), fabric);
            Prims.Box(p, "Back", new Vector3(0, 0.62f, -depth * 0.5f + 0.1f), new Vector3(width, 0.4f, 0.2f), dark);
            Prims.Box(p, "ArmL", new Vector3(-width * 0.5f + 0.075f, 0.53f, 0), new Vector3(0.15f, 0.22f, depth), dark);
            Prims.Box(p, "ArmR", new Vector3(width * 0.5f - 0.075f, 0.53f, 0), new Vector3(0.15f, 0.22f, depth), dark);
        }

        static void TvStand(Transform p, IMaterialProvider mats)
        {
            Solid(p, mats, 1.6f, 0.4f, 0.5f, WoodDark, "WoodDark");
            Prims.Box(p, "TV", new Vector3(0, 0.5f + 0.4f, 0), new Vector3(1.3f, 0.75f, 0.05f), mats.Get("Black", Black));
        }

        /// <summary>Bed on a solid base (too low for the robot), headboard at +Z.</summary>
        static void Bed(Transform p, IMaterialProvider mats)
        {
            const float w = 1.6f, l = 2.0f;
            Prims.Box(p, "Base", new Vector3(0, 0.2f, 0), new Vector3(w, 0.4f, l), mats.Get("WoodLight", WoodLight));
            Prims.Box(p, "Mattress", new Vector3(0, 0.5f, 0.02f), new Vector3(w - 0.06f, 0.2f, l - 0.1f), mats.Get("Linen", Linen));
            Prims.Box(p, "Headboard", new Vector3(0, 0.55f, l * 0.5f - 0.04f), new Vector3(w, 1.1f, 0.08f), mats.Get("WoodDark", WoodDark));
        }

        static void Plant(Transform p, IMaterialProvider mats)
        {
            Prims.Cyl(p, "Pot", Vector3.zero, 0.17f, 0.4f, mats.Get("Pot", Pot));
            Prims.Sphere(p, "Foliage", new Vector3(0, 0.7f, 0), 0.3f, mats.Get("Leaf", Leaf));
        }
    }
}

namespace CleanerBot
{
    public interface IMaterialProvider
    {
        Material Get(string key, Color color);
    }

    /// <summary>Creates in-memory URP Lit materials (used at runtime when switching houses).</summary>
    public class RuntimeMaterialProvider : IMaterialProvider
    {
        readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        public Material Get(string key, Color color)
        {
            if (!_cache.TryGetValue(key, out var m) || m == null)
            {
                m = new Material(Prims.FindLitShader()) { name = key };
                m.SetColor("_BaseColor", color);
                _cache[key] = m;
            }
            return m;
        }
    }

    /// <summary>Primitive helpers shared by the generators.</summary>
    public static class Prims
    {
        public static Shader FindLitShader()
        {
            var s = Shader.Find("Universal Render Pipeline/Lit");
            return s != null ? s : Shader.Find("Standard");
        }

        public static GameObject Box(Transform parent, string name, Vector3 localCenter, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>Upright cylinder whose base sits at localBase.</summary>
        public static GameObject Cyl(Transform parent, string name, Vector3 localBase, float radius, float height, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localBase + new Vector3(0f, height * 0.5f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        public static GameObject Sphere(Transform parent, string name, Vector3 localCenter, float radius, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = Vector3.one * radius * 2f;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }
    }

    /// <summary>Builds a house GameObject hierarchy (floor, walls, furniture, NavMeshSurface) from a HouseLayout.</summary>
    public static class HouseGenerator
    {
        public const int NotWalkableArea = 1;
        static readonly Color FloorColor = new Color(0.80f, 0.70f, 0.55f);
        static readonly Color WallColor = new Color(0.93f, 0.92f, 0.89f);

        public static HouseInstance Build(HouseLayout L, IMaterialProvider mats)
        {
            var root = new GameObject("House_" + L.name);
            var house = root.AddComponent<HouseInstance>();
            float t = L.wallThickness;

            // Floor: top surface at y = 0
            var floor = Prims.Box(root.transform, "Floor",
                new Vector3(L.width * 0.5f, -0.05f, L.depth * 0.5f),
                new Vector3(L.width + t, 0.1f, L.depth + t),
                mats.Get("Floor", FloorColor));

            // Walls (not walkable)
            var walls = new GameObject("Walls").transform;
            walls.SetParent(root.transform, false);
            MarkNotWalkable(walls.gameObject);
            var wallMat = mats.Get("Wall", WallColor);
            for (int i = 0; i < L.walls.Count; i++)
                BuildWall(walls, L.walls[i], i, t, L.wallHeight, wallMat);

            // Furniture (not walkable on top, but space underneath legged furniture stays walkable)
            var furn = new GameObject("Furniture").transform;
            furn.SetParent(root.transform, false);
            MarkNotWalkable(furn.gameObject);
            var counts = new Dictionary<string, int>();
            foreach (var f in L.furniture)
            {
                counts.TryGetValue(f.kind, out var n);
                counts[f.kind] = n + 1;
                var go = FurnitureFactory.Create(f, mats);
                go.name = $"{f.kind}_{n + 1}";
                go.transform.SetParent(furn, false);
            }

            // NavMesh surface over the whole house
            var surface = root.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true;   // fine voxels so gaps between chair/table legs are resolved
            surface.voxelSize = 0.03f;

            house.layoutName = L.name;
            house.displayName = L.displayName;
            house.surface = surface;
            house.floor = floor.transform;
            house.floorBounds = new Bounds(new Vector3(L.width * 0.5f, 0f, L.depth * 0.5f), new Vector3(L.width, 0f, L.depth));
            house.robotSpawn = new Vector3(L.robotSpawn.x, 0f, L.robotSpawn.y);
            return house;
        }

        static void MarkNotWalkable(GameObject go)
        {
            var mod = go.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = NotWalkableArea;
        }

        static void BuildWall(Transform parent, WallSpec w, int idx, float t, float h, Material m)
        {
            bool alongX = Mathf.Abs(w.a.y - w.b.y) < 1e-4f;
            float fixedC = alongX ? w.a.y : w.a.x;
            float s0 = (alongX ? Mathf.Min(w.a.x, w.b.x) : Mathf.Min(w.a.y, w.b.y)) - t * 0.5f;
            float s1 = (alongX ? Mathf.Max(w.a.x, w.b.x) : Mathf.Max(w.a.y, w.b.y)) + t * 0.5f;

            var doors = new List<Vector2>(w.doors);
            doors.Sort((p, q) => p.x.CompareTo(q.x));

            int k = 0;
            float cur = s0;
            foreach (var d in doors)
            {
                Segment(cur, d.x);
                cur = d.y;
            }
            Segment(cur, s1);

            void Segment(float from, float to)
            {
                if (to - from < 0.01f) return;
                float len = to - from, mid = (from + to) * 0.5f;
                var c = alongX ? new Vector3(mid, h * 0.5f, fixedC) : new Vector3(fixedC, h * 0.5f, mid);
                var size = alongX ? new Vector3(len, h, t) : new Vector3(t, h, len);
                Prims.Box(parent, $"Wall_{idx}_{k++}", c, size, m);
            }
        }
    }
}

namespace CleanerBot
{
    /// <summary>Root component of a generated house. Guarantees a NavMesh exists at runtime.</summary>
    [DefaultExecutionOrder(-100)]
    public class HouseInstance : MonoBehaviour
    {
        public string layoutName;
        public string displayName;
        public NavMeshSurface surface;
        public Transform floor;
        public Bounds floorBounds;
        public Vector3 robotSpawn;

        void Awake()
        {
            if (surface == null) surface = GetComponent<NavMeshSurface>();
            // Baked data is normally saved with the scene; build on the fly if it's missing
            // (e.g. a house generated at runtime from a preset).
            if (surface != null && surface.navMeshData == null)
                surface.BuildNavMesh();
        }

        public void RebuildNavMesh()
        {
            if (surface == null) surface = GetComponent<NavMeshSurface>();
            surface.BuildNavMesh();
        }

        /// <summary>Walkable NavMesh area in m짼 (triangulation of all loaded NavMesh data).</summary>
        public static float NavMeshArea(out int triangles)
        {
            var tri = NavMesh.CalculateTriangulation();
            float area = 0f;
            for (int i = 0; i < tri.indices.Length; i += 3)
            {
                var a = tri.vertices[tri.indices[i]];
                var b = tri.vertices[tri.indices[i + 1]];
                var c = tri.vertices[tri.indices[i + 2]];
                area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
            triangles = tri.indices.Length / 3;
            return area;
        }
    }
}

namespace CleanerBot
{
    /// <summary>
    /// Axis-aligned wall segment from a to b (XZ plane, metres).
    /// doors: openings as (start, end) along the wall's axis, in world coordinates.
    /// </summary>
    [Serializable]
    public class WallSpec
    {
        public Vector2 a;
        public Vector2 b;
        public List<Vector2> doors = new List<Vector2>();

        public WallSpec() { }
        public WallSpec(float ax, float az, float bx, float bz, params Vector2[] doorSpans)
        {
            a = new Vector2(ax, az);
            b = new Vector2(bx, bz);
            doors.AddRange(doorSpans);
        }
    }

    /// <summary>Furniture placement. pos = footprint centre (x, z). size is optional (kind-specific).</summary>
    [Serializable]
    public class FurnitureSpec
    {
        public string kind;
        public Vector2 pos;
        public float rotY;
        public Vector2 size;

        public FurnitureSpec() { }
        public FurnitureSpec(string kind, float x, float z, float rotY = 0f, float sx = 0f, float sz = 0f)
        {
            this.kind = kind;
            pos = new Vector2(x, z);
            this.rotY = rotY;
            size = new Vector2(sx, sz);
        }
    }

    /// <summary>Data description of a house. Serializable so presets can later come from JSON / floor plans.</summary>
    [Serializable]
    public class HouseLayout
    {
        public string name;
        public string displayName;
        public float width;          // X extent (m)
        public float depth;          // Z extent (m)
        public float wallHeight = 1.2f;    // cut-away height so the interior stays visible
        public float wallThickness = 0.1f;
        public Vector2 robotSpawn;
        public List<WallSpec> walls = new List<WallSpec>();
        public List<FurnitureSpec> furniture = new List<FurnitureSpec>();
    }
}

namespace CleanerBot
{
    /// <summary>Built-in ("prefilled") house layouts. Add new presets to the table below.</summary>
    public static class HousePresets
    {
        static readonly Dictionary<string, Func<HouseLayout>> Table = new Dictionary<string, Func<HouseLayout>>
        {
            { "Apartment_A", ApartmentA },
        };

        public static IReadOnlyList<string> Names => Table.Keys.ToList();

        public static HouseLayout Get(string name)
        {
            if (string.IsNullOrEmpty(name) || !Table.TryGetValue(name, out var f))
                throw new ArgumentException($"Unknown house preset '{name}'. Available: {string.Join(", ", Table.Keys)}");
            return f();
        }

        static Vector2 Door(float from, float to) => new Vector2(from, to);

        /// <summary>
        /// 10m x 8m apartment, 4 rooms:
        ///   Living (x0-6, z0-5) | Kitchen/Dining (x6-10, z0-4)
        ///   Study  (x0-6, z5-8) | Bedroom        (x6-10, z4-8)
        /// </summary>
        static HouseLayout ApartmentA()
        {
            var L = new HouseLayout
            {
                name = "Apartment_A",
                displayName = "Apartment A (4 rooms, 80 m짼)",
                width = 10f,
                depth = 8f,
                robotSpawn = new Vector2(4.6f, 0.5f),
            };

            // Outer walls
            L.walls.Add(new WallSpec(0, 0, 10, 0));
            L.walls.Add(new WallSpec(0, 8, 10, 8));
            L.walls.Add(new WallSpec(0, 0, 0, 8));
            L.walls.Add(new WallSpec(10, 0, 10, 8));
            // Interior walls with door openings (1.0 m)
            L.walls.Add(new WallSpec(6, 0, 6, 8, Door(1.6f, 2.6f), Door(6.2f, 7.2f))); // living|kitchen, study|bedroom
            L.walls.Add(new WallSpec(0, 5, 6, 5, Door(2.0f, 3.0f)));                    // living|study
            L.walls.Add(new WallSpec(6, 4, 10, 4, Door(7.6f, 8.6f)));                   // kitchen|bedroom

            var F = L.furniture;
            // Living room
            F.Add(new FurnitureSpec("sofa", 2.5f, 0.55f, 0f));
            F.Add(new FurnitureSpec("coffee_table", 2.5f, 1.75f));
            F.Add(new FurnitureSpec("tv_stand", 2.5f, 4.73f, 180f));
            F.Add(new FurnitureSpec("armchair", 0.55f, 2.2f, 90f));
            F.Add(new FurnitureSpec("bookshelf", 0.22f, 3.9f, 90f));
            F.Add(new FurnitureSpec("plant", 5.6f, 0.4f));
            // Kitchen / dining
            F.Add(new FurnitureSpec("kitchen_counter", 8.4f, 0.36f, 0f, 3.0f, 0.6f));
            F.Add(new FurnitureSpec("fridge", 9.58f, 1.25f, -90f));
            F.Add(new FurnitureSpec("dining_table", 8.0f, 2.6f));
            F.Add(new FurnitureSpec("chair", 7.6f, 1.95f, 0f));
            F.Add(new FurnitureSpec("chair", 8.4f, 1.95f, 0f));
            F.Add(new FurnitureSpec("chair", 7.6f, 3.25f, 180f));
            F.Add(new FurnitureSpec("chair", 8.4f, 3.25f, 180f));
            // Bedroom
            F.Add(new FurnitureSpec("bed", 8.6f, 6.93f, 0f));
            F.Add(new FurnitureSpec("nightstand", 7.5f, 7.7f));
            F.Add(new FurnitureSpec("nightstand", 9.68f, 7.7f));
            F.Add(new FurnitureSpec("wardrobe", 6.36f, 4.75f, 90f));
            F.Add(new FurnitureSpec("plant", 9.6f, 4.45f));
            // Study
            F.Add(new FurnitureSpec("desk", 1.2f, 7.6f));
            F.Add(new FurnitureSpec("chair", 1.2f, 6.95f, 0f));
            F.Add(new FurnitureSpec("bookshelf", 4.0f, 7.8f, 180f));
            F.Add(new FurnitureSpec("armchair", 5.35f, 5.8f, -90f));
            F.Add(new FurnitureSpec("plant", 0.4f, 5.45f));
            return L;
        }
    }
}
#if ENABLE_INPUT_SYSTEM
#endif

namespace CleanerBot
{
    /// <summary>
    /// Robot vacuum driven by a NavMeshAgent.
    /// Phase 1 modes: Idle (external MoveTo calls), Wander (random reachable goals), ClickToMove.
    /// A cleaning planner (phase 3) will call MoveTo() instead of Wander.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class RobotController : MonoBehaviour
    {
        public enum Mode { Idle, Wander, ClickToMove }

        [Header("Behaviour")]
        public Mode mode = Mode.Wander;

        [Header("Robot spec (typical robot vacuum)")]
        public float radius = 0.17f;
        public float height = 0.1f;
        public float speed = 0.3f;          // m/s
        public float angularSpeed = 180f;   // deg/s
        public float acceleration = 1.5f;

        [Header("Debug")]
        public bool drawPath = true;

        public float DistanceTraveled { get; private set; }
        public int GoalsReached { get; private set; }
        public Vector3? CurrentGoal { get; private set; }
        public NavMeshAgent Agent => _agent;

        NavMeshAgent _agent;
        LineRenderer _line;
        Vector3 _lastPos;
        Bounds _area;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.radius = radius;
            _agent.height = height;
            _agent.speed = speed;
            _agent.angularSpeed = angularSpeed;
            _agent.acceleration = acceleration;
            _agent.stoppingDistance = 0.03f;
            _agent.autoBraking = true;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            _line = GetComponentInChildren<LineRenderer>();
        }

        void Start()
        {
            var house = FindAnyObjectByType<HouseInstance>();
            _area = house != null ? house.floorBounds : new Bounds(transform.position, new Vector3(10, 0, 10));

            if (!_agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 1f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
            _lastPos = transform.position;
        }

        /// <summary>Plan a path to a world position. Returns false if unreachable.</summary>
        public bool MoveTo(Vector3 worldPos)
        {
            if (_agent == null || !_agent.isOnNavMesh) return false;
            if (!NavMesh.SamplePosition(worldPos, out var hit, 0.5f, NavMesh.AllAreas)) return false;

            var path = new NavMeshPath();
            if (!_agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                return false;

            _agent.SetPath(path);
            CurrentGoal = hit.position;
            return true;
        }

        public void Stop()
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();
            CurrentGoal = null;
        }

        public bool HasArrived =>
            _agent != null && _agent.isOnNavMesh && !_agent.pathPending &&
            (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.02f);

        void Update()
        {
            // Odometry
            var p = transform.position;
            DistanceTraveled += Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(_lastPos.x, 0, _lastPos.z));
            _lastPos = p;

            if (CurrentGoal.HasValue && HasArrived)
            {
                GoalsReached++;
                CurrentGoal = null;
            }

            switch (mode)
            {
                case Mode.Wander:
                    if (!CurrentGoal.HasValue) PickRandomGoal();
                    break;
                case Mode.ClickToMove:
                    HandleClick();
                    break;
            }

            if (drawPath) UpdatePathLine();
        }

        void PickRandomGoal()
        {
            for (int i = 0; i < 30; i++)
            {
                var q = new Vector3(
                    UnityEngine.Random.Range(_area.min.x, _area.max.x), 0f,
                    UnityEngine.Random.Range(_area.min.z, _area.max.z));
                if (MoveTo(q)) return;
            }
        }

        void HandleClick()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null || !mouse.leftButton.wasPressedThisFrame) return;
            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, 100f)) MoveTo(hit.point);
#endif
        }

        void UpdatePathLine()
        {
            if (_line == null) return;
            if (!_agent.hasPath) { _line.positionCount = 0; return; }
            var corners = _agent.path.corners;
            _line.positionCount = corners.Length;
            for (int i = 0; i < corners.Length; i++)
                _line.SetPosition(i, corners[i] + Vector3.up * 0.02f);
        }
    }
}

namespace CleanerBot
{
    /// <summary>Builds the robot vacuum GameObject (visuals without colliders + NavMeshAgent + controller).</summary>
    public static class RobotFactory
    {
        public static RobotController Create(Vector3 position, IMaterialProvider mats)
        {
            var root = new GameObject("CleanerBot");
            root.transform.position = position;

            // Agent first so RobotController's RequireComponent is satisfied with our settings.
            var agent = root.AddComponent<NavMeshAgent>();
            agent.agentTypeID = 0;
            agent.baseOffset = 0f;
            agent.radius = 0.17f;
            agent.height = 0.1f;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            NoCollider(Prims.Cyl(visual, "Body", Vector3.zero, 0.17f, 0.08f, mats.Get("RobotBody", new Color(0.15f, 0.15f, 0.17f))));
            NoCollider(Prims.Cyl(visual, "TopPlate", new Vector3(0, 0.08f, 0), 0.15f, 0.01f, mats.Get("RobotTop", new Color(0.75f, 0.77f, 0.80f))));
            NoCollider(Prims.Box(visual, "Bumper", new Vector3(0, 0.045f, 0.13f), new Vector3(0.22f, 0.05f, 0.08f), mats.Get("RobotAccent", new Color(0.10f, 0.65f, 0.95f))));
            NoCollider(Prims.Cyl(visual, "Lidar", new Vector3(0, 0.09f, -0.03f), 0.04f, 0.025f, mats.Get("RobotBody", new Color(0.15f, 0.15f, 0.17f))));

            // Path preview line
            var lineGo = new GameObject("PathLine");
            lineGo.transform.SetParent(root.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = 0.03f;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = mats.Get("PathLine", new Color(1f, 0.55f, 0.1f));

            return root.AddComponent<RobotController>();
        }

        static void NoCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
        }
    }
}
public static class __DryRunEntry { public static void Run(){} }
