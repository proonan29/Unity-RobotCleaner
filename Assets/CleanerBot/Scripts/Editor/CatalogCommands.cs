using System.IO;
using System.Linq;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CleanerBot.EditorTools
{
    /// <summary>Build the furniture catalog that maps layout kinds to Asset Store models (Cute Furniture FREE by ithappy).</summary>
    public static class CatalogCommands
    {
        public const string CatalogPath = "Assets/CleanerBot/Furniture/CuteFurnitureCatalog.asset";
        const string PackRoot = "Assets/ithappy/Cute_Furniture_Free";

        // kind -> (prefab name, alignment, tile, yaw so the model faces local +Z, depth shift away from the wall)
        // yaw/depth for sofa, armchair, bookshelf, wardrobe, kitchen_counter, fridge come from the user's manual scene fixes (2026-09-30)
        static readonly (string kind, string prefab, FurnitureCatalog.Align align, bool tile, float yaw, float depth)[] Map =
        {
            ("sofa", "Couch_08", FurnitureCatalog.Align.Back, false, 0f, 0.085f),
            ("armchair", "Armchair_02", FurnitureCatalog.Align.Back, false, 0f, 0f),
            ("coffee_table", "Coffee_Table_03", FurnitureCatalog.Align.Center, false, 180f, 0f),
            ("dining_table", "Kitchen_Table_09", FurnitureCatalog.Align.Center, false, 180f, 0f),
            ("desk", "Work_Table_06", FurnitureCatalog.Align.Back, false, 180f, 0f),
            ("chair", "Chair_17", FurnitureCatalog.Align.Center, false, 180f, 0f),
            ("bookshelf", "Closet_01", FurnitureCatalog.Align.Back, false, 0f, 0.045f),
            ("kitchen_counter", "Kitchen_D_01", FurnitureCatalog.Align.Back, true, 0f, 0.01f),
            ("fridge", "Fridge_01", FurnitureCatalog.Align.Back, false, 0f, 0.1f),
            ("bed", "Bed_07", FurnitureCatalog.Align.Front, false, 180f, 0f),
            ("nightstand", "Nightstand_02", FurnitureCatalog.Align.Back, false, 180f, 0f),
            ("wardrobe", "Closet_01", FurnitureCatalog.Align.Back, false, 0f, 0f),
            ("plant", "Plants_15", FurnitureCatalog.Align.Center, false, 180f, 0f),
            // tv_stand: no suitable model in the pack -> procedural
        };

        public static FurnitureCatalog LoadCatalog() => AssetDatabase.LoadAssetAtPath<FurnitureCatalog>(CatalogPath);

        [CliCommand("cb_setup_catalog", "Create/refresh the furniture catalog from the imported 'Cute Furniture FREE' pack (Read/Write meshes, kind->prefab map) and assign it to the AppController.")]
        public static string SetupCatalog()
        {
            if (!AssetDatabase.IsValidFolder(PackRoot)) return $"error: {PackRoot} not found — import the pack first (cb_store_download 260522, then import)";
            var sb = new StringBuilder();

            // MeshColliders are created at runtime (house switching) -> meshes must be readable in builds
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { PackRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
                {
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                    changed++;
                }
            }
            sb.AppendLine($"readable meshes enabled on {changed} models");

            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { PackRoot }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            var cat = LoadCatalog();
            if (cat == null)
            {
                AssetMaterialProvider.EnsureFolder(Path.GetDirectoryName(CatalogPath).Replace('\\', '/'));
                cat = ScriptableObject.CreateInstance<FurnitureCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
            }
            cat.entries.Clear();
            foreach (var m in Map)
            {
                var path = prefabs.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == m.prefab);
                var go = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                cat.entries.Add(new FurnitureCatalog.Entry { kind = m.kind, prefab = go, yawOffset = m.yaw, depthOffset = m.depth, align = m.align, tileAlongX = m.tile });
                sb.AppendLine($"{m.kind,-16} -> {(go != null ? m.prefab : "MISSING " + m.prefab)}");
            }
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();

            var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
            if (app != null && !Application.isPlaying)
            {
                app.catalog = cat;
                EditorUtility.SetDirty(app);
                EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
                EditorSceneManager.SaveScene(app.gameObject.scene);
                sb.AppendLine("assigned to AppController");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
