using System.Globalization;
using System.IO;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot.EditorTools
{
    /// <summary>CLI commands for the fixed-step cleaning simulation (phase 3).</summary>
    public static class SimulationCommands
    {
        const string PreviewName = "__SimPreview";
        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string Mmss(float s) => s < 0 ? "-" : $"{(int)(s / 60)}:{(int)(s % 60):00}";

        public static string Summary(SimStats s, float wallMs) =>
            $"planner={s.planner} end={s.endReason} coverage={F(s.coveragePercent)}% " +
            $"sim_time={Mmss(s.simTime)} t50={Mmss(s.timeTo50)} t90={Mmss(s.timeTo90)} t95={Mmss(s.timeTo95)} " +
            $"distance={F(s.distance)}m turns={F(s.rotationDeg / 360f)}rev goals={s.goals} failed={s.failedGoals} " +
            $"avg_passes={F(s.averagePasses)} overlap={F(s.overlapPercent)}% cleanable={F(s.cleanableM2)}m2 " +
            $"wall_ms={F(wallMs)} plan_ms={F(s.planningMs)}" +
            (s.batteryEnabled ? $" battery={F(s.batteryPercent)}% min_battery={F(s.minBattery)}% charges={s.charges} charging={Mmss(s.chargingTime)}" : " battery=off");

        /// <summary>Build a fresh grid + core for the house in the active scene (edit or play mode).</summary>
        static SimulationCore CreateHeadless(string planner, int seed, out string error)
        {
            var house = UnityEngine.Object.FindAnyObjectByType<HouseInstance>();
            var cm = UnityEngine.Object.FindAnyObjectByType<CoverageManager>();
            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            var spec = runner != null ? runner.spec : RobotSpec.Default;
            var battery = runner != null ? runner.battery : BatterySpec.Default;
            float cell = cm != null ? cm.cellSize : 0.05f;
            var core = HeadlessSim.Create(house, planner, seed, spec, battery, cell, out error);
            if (core == null) error = "error: " + error;
            return core;
        }
        [CliCommand("cb_sim_run", "Run a complete cleaning simulation headless (fixed step, no player loop). Optionally shows the result on the floor and writes Screenshots/sim_<planner>.png/.json.")]
        public static string SimRun(
            [CliArg("planner", "Frontier | Random")] string planner = "Frontier",
            [CliArg("max_minutes", "Simulated time limit (min)")] float maxMinutes = 180f,
            [CliArg("seed", "Random seed")] int seed = 1,
            [CliArg("preview", "Paint the result on the floor in the scene")] bool preview = true)
        {
            var core = CreateHeadless(planner, seed, out var err);
            if (core == null) return err;
            float wall = core.RunToEnd(0.05f, maxMinutes * 60f);

            string root = Path.GetDirectoryName(Application.dataPath);
            string dir = Path.Combine(root, "Screenshots");
            Directory.CreateDirectory(dir);
            string baseName = "sim_" + core.Planner.Name.ToLowerInvariant();
            File.WriteAllText(Path.Combine(dir, baseName + ".json"), JsonUtility.ToJson(core.Stats, true));

            var painterRoot = new GameObject("__tmp");
            painterRoot.hideFlags = HideFlags.HideAndDontSave;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/CleanerBot/Shaders/CoverageOverlay.shader");
            var painter = new CoveragePainter(core.Grid, shader, painterRoot.transform, 0.004f);
            painter.Overlay.hideFlags = HideFlags.DontSave;
            painter.Refresh(core.Grid, true);
            File.WriteAllBytes(Path.Combine(dir, baseName + ".png"), painter.Texture.EncodeToPNG());

            if (preview)
            {
                ClearPreview();
                painterRoot.name = PreviewName;
                painterRoot.hideFlags = HideFlags.DontSave;      // visible in the scene, never saved
                DrawTrailMarker(painterRoot.transform, core);
            }
            else painter.Dispose();
            if (!preview) UnityEngine.Object.DestroyImmediate(painterRoot);

            return Summary(core.Stats, wall);
        }

        [CliCommand("cb_sim_compare", "Run every planner headless with the same settings and compare.")]
        public static string SimCompare(
            [CliArg("max_minutes", "Simulated time limit (min)")] float maxMinutes = 60f,
            [CliArg("seed", "Random seed")] int seed = 1)
        {
            var sb = new StringBuilder();
            foreach (var name in PlannerFactory.Names)
            {
                var core = CreateHeadless(name, seed, out var err);
                if (core == null) return err;
                float wall = core.RunToEnd(0.05f, maxMinutes * 60f);
                sb.AppendLine(Summary(core.Stats, wall));
            }
            return sb.ToString().TrimEnd();
        }

        [CliCommand("cb_sim_clear_preview", "Remove the headless simulation preview overlay from the scene.")]
        public static string ClearPreview()
        {
            int n = 0;
            GameObject go;
            while ((go = GameObject.Find(PreviewName)) != null)
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r.sharedMaterial == null) continue;
                    if (r.sharedMaterial.HasProperty("_CoverageTex"))
                    {
                        var tex = r.sharedMaterial.GetTexture("_CoverageTex");
                        if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                    }
                    UnityEngine.Object.DestroyImmediate(r.sharedMaterial);
                }
                UnityEngine.Object.DestroyImmediate(go);
                n++;
            }
            return $"removed {n}";
        }

        static void DrawTrailMarker(Transform parent, SimulationCore core)
        {
            // small marker where the robot ended
            var m = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m.name = "EndPosition";
            m.hideFlags = HideFlags.DontSave;
            UnityEngine.Object.DestroyImmediate(m.GetComponent<Collider>());
            m.transform.SetParent(parent, false);
            m.transform.position = core.Robot.Position + Vector3.up * 0.05f;
            m.transform.localScale = new Vector3(0.12f, 0.05f, 0.12f);
            var mat = new Material(Prims.FindLitShader());
            mat.SetColor("_BaseColor", new Color(1f, 0.3f, 0.2f));
            m.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ---- scene wiring & play-mode control ----

        public static SimulationRunner EnsureRunner()
        {
            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null) runner = new GameObject("Simulation").AddComponent<SimulationRunner>();
            runner.coverage = UnityEngine.Object.FindAnyObjectByType<CoverageManager>();
            runner.robot = UnityEngine.Object.FindAnyObjectByType<RobotController>();
            EditorUtility.SetDirty(runner);
            return runner;
        }

        [CliCommand("cb_add_simulation", "Add/refresh the SimulationRunner in the active scene (edit mode) and save.")]
        public static string AddSimulation()
        {
            if (Application.isPlaying) return "error: exit play mode first";
            var r = EnsureRunner();
            EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
            EditorSceneManager.SaveScene(r.gameObject.scene);
            return $"runner={r.name} planner={r.planner} speed={F(r.speedMultiplier)}x autoStart={r.autoStart}";
        }

        static SimulationRunner PlayRunner(out string error)
        {
            error = null;
            if (!Application.isPlaying) { error = "error: enter play mode first (editor_play)"; return null; }
            var r = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (r == null) error = "error: no SimulationRunner (cb_add_simulation)";
            return r;
        }

        [CliCommand("cb_sim_start", "Play mode: (re)start cleaning from the dock with a planner.")]
        public static string SimStart([CliArg("planner", "Frontier | Random")] string planner = "Frontier")
        {
            var r = PlayRunner(out var e); if (r == null) return e;
            r.StartCleaning(planner);
            return $"started planner={r.planner} speed={F(r.speedMultiplier)}x";
        }

        [CliCommand("cb_sim_speed", "Play mode: set simulation speed multiplier (0.1 - 100).")]
        public static string SimSpeed([CliArg("x", "Speed multiplier")] float x)
        {
            var r = PlayRunner(out var e); if (r == null) return e;
            r.speedMultiplier = Mathf.Clamp(x, 0.1f, 100f);
            return $"speed={F(r.speedMultiplier)}x";
        }

        [CliCommand("cb_sim_pause", "Play mode: pause or resume the simulation.")]
        public static string SimPause([CliArg("paused", "true / false")] bool paused = true)
        {
            var r = PlayRunner(out var e); if (r == null) return e;
            r.SetPaused(paused);
            return $"state={r.Core?.State}";
        }

        [CliCommand("cb_sim_advance", "Play mode: advance the simulation by N simulated seconds right now (independent of the player loop / window focus).")]
        public static string SimAdvance([CliArg("seconds", "Simulated seconds")] float seconds = 60f)
        {
            var r = PlayRunner(out var e); if (r == null) return e;
            if (r.Core == null) return "not started";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            r.Advance(seconds);
            return $"advanced {F(seconds)}s in {F((float)sw.Elapsed.TotalMilliseconds)}ms: " + Summary(r.Core.Stats, 0f);
        }

        [CliCommand("cb_sim_status", "Play mode: live simulation statistics.")]
        public static string SimStatus()
        {
            var r = PlayRunner(out var e); if (r == null) return e;
            if (r.Core == null) return "not started";
            var p = r.Core.Robot.Position;
            return $"state={r.Core.State} task={r.Core.Task} battery={F(r.Core.Battery.Percent)}% speed={F(r.speedMultiplier)}x pos=({F(p.x)},{F(p.z)}) " + Summary(r.Core.Stats, 0f);
        }

        [CliCommand("cb_battery", "Show or change the battery settings used by the next run (play or edit mode; edit mode saves the scene).")]
        public static string BatteryConfig(
            [CliArg("enabled", "Simulate the battery (true/false); omit to keep")] string enabled = "",
            [CliArg("capacity", "Minutes of driving on a full battery; <=0 keeps")] float capacity = -1f,
            [CliArg("charge", "Minutes to charge 0->100 %; <=0 keeps")] float charge = -1f,
            [CliArg("resume", "Resume cleaning at this charge %; <=0 keeps")] float resume = -1f)
        {
            var r = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (r == null) return "error: no SimulationRunner";
            var b = r.battery;
            if (bool.TryParse(enabled, out var en)) b.enabled = en;
            if (capacity > 0f) b.capacityMinutes = capacity;
            if (charge > 0f) b.chargeMinutes = charge;
            if (resume > 0f) b.resumeAtPercent = Mathf.Clamp(resume, 10f, 100f);
            r.battery = b;
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(r);
                EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
                EditorSceneManager.SaveScene(r.gameObject.scene);
            }
            return $"battery enabled={b.enabled} capacity={F(b.capacityMinutes)}min charge={F(b.chargeMinutes)}min resume={F(b.resumeAtPercent)}% reserve={F(b.reservePercent)}% safety={F(b.safetyFactor)}";
        }
    }
}
