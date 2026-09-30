using System.Globalization;
using CleanerBot.UI;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace CleanerBot.EditorTools
{
    /// <summary>CLI commands for the UI Toolkit front-end and the AppController (phase 4).</summary>
    public static class UICommands
    {
        const string UiFolder = "Assets/CleanerBot/UI";
        const string PanelSettingsPath = UiFolder + "/CleanerBotPanelSettings.asset";
        const string ThemePath = UiFolder + "/CleanerBotTheme.tss";
        const string UxmlPath = UiFolder + "/CleanerBotUI.uxml";

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        static PanelSettings EnsurePanelSettings()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps != null) return ps;
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            return ps;
        }

        /// <summary>Create/refresh AppController + UIDocument in the active scene.</summary>
        public static string EnsureUI()
        {
            var runner = SimulationCommands.EnsureRunner();
            runner.autoStart = false;                         // the UI starts cleaning
            EditorUtility.SetDirty(runner);

            var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
            if (app == null) app = new GameObject("App").AddComponent<AppController>();
            app.house = UnityEngine.Object.FindAnyObjectByType<HouseInstance>();
            app.robot = UnityEngine.Object.FindAnyObjectByType<RobotController>();
            app.coverage = UnityEngine.Object.FindAnyObjectByType<CoverageManager>();
            app.runner = runner;
            app.cam = Camera.main;
            app.catalog = CatalogCommands.LoadCatalog();
            EditorUtility.SetDirty(app);

            var ui = UnityEngine.Object.FindAnyObjectByType<CleanerBotUI>();
            if (ui == null)
            {
                var go = new GameObject("UI");
                go.AddComponent<UIDocument>();
                ui = go.AddComponent<CleanerBotUI>();
            }
            var doc = ui.GetComponent<UIDocument>();
            doc.panelSettings = EnsurePanelSettings();
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            ui.app = app;
            EditorUtility.SetDirty(doc);
            EditorUtility.SetDirty(ui);

            return $"app={app.name} ui={ui.name} panel={(doc.panelSettings != null ? PanelSettingsPath : "MISSING")} " +
                   $"theme={(doc.panelSettings != null && doc.panelSettings.themeStyleSheet != null ? "ok" : "MISSING")} " +
                   $"uxml={(doc.visualTreeAsset != null ? "ok" : "MISSING")} runner.autoStart={runner.autoStart}";
        }

        [CliCommand("cb_add_ui", "Add/refresh the UI Toolkit screen and AppController in the active scene (edit mode) and save.")]
        public static string AddUI()
        {
            if (Application.isPlaying) return "error: exit play mode first";
            var r = EnsureUI();
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return r;
        }

        // ---- play-mode app control (same paths the UI uses) ----

        static AppController PlayApp(out string error)
        {
            error = null;
            if (!Application.isPlaying) { error = "error: enter play mode first (editor_play)"; return null; }
            var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
            if (app == null) error = "error: no AppController (cb_add_ui)";
            return app;
        }

        [CliCommand("cb_app_load_house", "Play mode: switch to a house preset at runtime (rebuilds NavMesh + coverage).")]
        public static string AppLoadHouse([CliArg("preset", "House preset name (cb_list_houses)")] string preset)
        {
            var app = PlayApp(out var e); if (app == null) return e;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            app.LoadHouse(preset);
            var g = app.coverage.Grid.GetStats();
            float nav = HouseInstance.NavMeshArea(out int tris);
            return $"loaded {app.CurrentHouse} in {F((float)sw.Elapsed.TotalMilliseconds)}ms walkable={F(nav)}m2 cleanable={F(g.cleanableM2)}m2 tris={tris}";
        }

        [CliCommand("cb_app_compare", "Play mode: run the UI's 'Compare planners' for the current house and show the summary panel.")]
        public static string AppCompare([CliArg("max_minutes", "Simulated time limit")] float maxMinutes = 60f)
        {
            var app = PlayApp(out var e); if (app == null) return e;
            var ui = UnityEngine.Object.FindAnyObjectByType<CleanerBotUI>();
            if (ui != null) ui.compareMinutes = maxMinutes;
            var res = app.ComparePlanners(maxMinutes);
            var sb = new System.Text.StringBuilder();
            foreach (var s in res) sb.AppendLine(SimulationCommands.Summary(s, 0f));
            if (ui != null) ui.SendMessage("RunComparison", SendMessageOptions.DontRequireReceiver);
            return sb.ToString().TrimEnd();
        }
    }
}
