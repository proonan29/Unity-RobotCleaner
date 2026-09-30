using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

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
            robot.transform.rotation = Quaternion.Euler(0f, house.robotSpawnYaw, 0f);
            EnsureCoverage(house, robot);
            SimulationCommands.EnsureRunner();
            UICommands.EnsureUI();

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
            CameraFramer.Frame(cam, house.floorBounds, pitch);
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
