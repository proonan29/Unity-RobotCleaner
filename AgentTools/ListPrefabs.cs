using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>List imported Cute Furniture prefabs with bounds (size, min/max z) and collider/readable info.</summary>
public static class ListPrefabs
{
    public static string Run()
    {
        var sb = new StringBuilder();
        foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ithappy" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { sb.AppendLine($"{path} (no renderers)"); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
            var b = inst.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            int cols = inst.GetComponentsInChildren<Collider>().Length;
            var mf = inst.GetComponentsInChildren<MeshFilter>();
            bool readable = mf.All(m => m.sharedMesh == null || m.sharedMesh.isReadable);
            Object.DestroyImmediate(inst);
            sb.AppendLine($"{System.IO.Path.GetFileNameWithoutExtension(path)} | {path.Replace("Assets/ithappy/Cute_Furniture_Free/Prefabs/", "")} | size=({b.size.x:0.00},{b.size.y:0.00},{b.size.z:0.00}) center=({b.center.x:0.00},{b.min.y:0.00},{b.center.z:0.00}) meshes={mf.Length} colliders={cols} readable={readable}");
        }
        return sb.ToString();
    }
}
