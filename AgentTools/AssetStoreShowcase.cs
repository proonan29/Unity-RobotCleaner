using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Check imported Asset Store materials for URP compatibility (convert Built-in Standard -> URP Lit),
/// then build a separate showcase scene with the imported prefabs and frame the camera.
/// </summary>
public static class AssetStoreShowcase
{
    static readonly string[] Roots = { "Assets/ithappy", "Assets/EvSeStudio" };

    public static string Materials()
    {
        var sb = new StringBuilder();
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var guid in AssetDatabase.FindAssets("t:Material", Roots))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            string before = m.shader != null ? m.shader.name : "null";
            if (m.shader == null || m.shader.name == "Standard" || m.shader.name.StartsWith("Legacy") || m.shader.name == "Hidden/InternalErrorShader")
            {
                var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                var col = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                var nrm = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                m.shader = lit;
                if (tex != null) m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", col);
                if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
                EditorUtility.SetDirty(m);
                sb.AppendLine($"converted {path}: {before} -> URP/Lit");
            }
            else sb.AppendLine($"ok        {path}: {before}");
        }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    public static string Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.5f, 0.5f, 0.5f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.26f);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor"; floor.transform.localScale = new Vector3(3f, 1f, 2f);
        floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/CleanerBot/Materials/Floor.mat");

        string[] picks =
        {
            "Couch_08", "Armchair_02", "Coffee_Table_03", "Bed_07", "Closet_01", "Kitchen_Table_09",
            "Chair_17", "Fridge_01", "Work_Table_06", "Chair_PC_04", "Nightstand_02", "Plants_15",
        };
        var prefabs = AssetDatabase.FindAssets("t:Prefab", Roots).Select(AssetDatabase.GUIDToAssetPath).ToList();
        var sb = new StringBuilder();
        int i = 0;
        foreach (var name in picks)
        {
            var path = prefabs.FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);
            if (path == null) { sb.AppendLine("missing " + name); continue; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var b = Bounds(go);
            go.transform.position = new Vector3(-9f + (i % 6) * 3.4f, -b.min.y, (i / 6) * -3.6f + 2f);
            sb.AppendLine($"{name}: size={b.size}");
            i++;
        }

        // the robot vacuum model from the other pack
        var vac = prefabs.FirstOrDefault(p => p.EndsWith("/SM_Vacuum_Cleaner_01.prefab"));
        if (vac != null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(vac));
            var b = Bounds(go);
            go.transform.position = new Vector3(0f, -b.min.y, -5.5f);
            go.transform.localScale *= 3f;   // enlarged for the showcase only
            sb.AppendLine($"Vacuum_Cleaner_01: size={b.size} (shown x3)");
        }

        var cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
        cam.fieldOfView = 40f;
        cam.transform.position = new Vector3(0f, 11f, -16f);
        cam.transform.rotation = Quaternion.Euler(38f, 0f, 0f);

        EditorSceneManager.SaveScene(scene, "Assets/CleanerBot/Scenes/AssetStoreTest.unity");
        return sb.ToString();
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
