using System.Globalization;
using System.Text;
using CleanerBot;
using UnityEngine;

/// <summary>
/// For each furniture item: regenerate it with the catalog yaw set to the scene's child yaw,
/// then compare every model child's local position/rotation with the scene (detects manual child moves).
/// The catalog is restored in memory afterwards (not saved).
/// </summary>
public static class DiffFurniture2
{
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"({F(v.x)},{F(v.y)},{F(v.z)})";

    public static string Run()
    {
        var house = UnityEngine.Object.FindAnyObjectByType<HouseInstance>();
        var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
        var layout = HousePresets.Get(house.layoutName);
        var cat = app.catalog;
        var furn = house.transform.Find("Furniture");
        var sb = new StringBuilder();
        var counts = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var f in layout.furniture)
        {
            counts.TryGetValue(f.kind, out var n); counts[f.kind] = n + 1;
            var t = furn.Find($"{f.kind}_{n + 1}");
            var e = cat.Find(f.kind);
            if (t == null || e == null || t.childCount == 0) continue;
            float old = e.yawOffset;
            e.yawOffset = t.GetChild(0).localEulerAngles.y;
            var fresh = FurnitureFactory.Create(f, null, cat);
            e.yawOffset = old;
            sb.Append($"{t.name} yaw={F(t.GetChild(0).localEulerAngles.y)} children={t.childCount}/{fresh.transform.childCount}:");
            bool any = false;
            for (int i = 0; i < Mathf.Min(t.childCount, fresh.transform.childCount); i++)
            {
                var a = t.GetChild(i); var b = fresh.transform.GetChild(i);
                var d = a.localPosition - b.localPosition;
                float dy = Mathf.DeltaAngle(b.localEulerAngles.y, a.localEulerAngles.y);
                if (d.magnitude > 0.003f || Mathf.Abs(dy) > 0.5f)
                {
                    any = true;
                    sb.Append($" [{i} {a.name}] scene={V(a.localPosition)} regen={V(b.localPosition)} d={V(d)} dYaw={F(dy)}");
                }
            }
            sb.AppendLine(any ? "" : " same");
            UnityEngine.Object.DestroyImmediate(fresh);
        }
        return sb.ToString();
    }
}
