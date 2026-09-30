using System.Globalization;
using System.Linq;
using System.Text;
using CleanerBot;
using UnityEngine;

/// <summary>
/// Compare furniture in the open scene with what the preset + catalog would generate.
/// Prints root position/yaw deltas and the model child's local yaw/position deltas.
/// </summary>
public static class DiffFurniture
{
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Run()
    {
        var house = Object.FindAnyObjectByType<HouseInstance>();
        var app = Object.FindAnyObjectByType<AppController>();
        var layout = HousePresets.Get(house.layoutName);
        var cat = app != null ? app.catalog : null;
        var furn = house.transform.Find("Furniture");
        var sb = new StringBuilder();
        sb.AppendLine($"house={house.layoutName} children={furn.childCount} presetItems={layout.furniture.Count}");

        var counts = new System.Collections.Generic.Dictionary<string, int>();
        for (int i = 0; i < layout.furniture.Count; i++)
        {
            var f = layout.furniture[i];
            counts.TryGetValue(f.kind, out var n); counts[f.kind] = n + 1;
            string name = $"{f.kind}_{n + 1}";
            var t = furn.Find(name);
            if (t == null) { sb.AppendLine($"[{i}] {name}: MISSING in scene"); continue; }

            var p = t.localPosition;
            float yaw = t.localEulerAngles.y;
            float dYaw = Mathf.DeltaAngle(f.rotY, yaw);
            var dPos = new Vector2(p.x - f.pos.x, p.z - f.pos.y);
            var e = cat != null ? cat.Find(f.kind) : null;

            string child = "";
            if (t.childCount > 0)
            {
                var c = t.GetChild(0);
                float cy = c.localEulerAngles.y;
                child = $" child={c.name} childYaw={F(cy)} childPos=({F(c.localPosition.x)},{F(c.localPosition.y)},{F(c.localPosition.z)})" +
                        (e != null ? $" catYaw={F(e.yawOffset)} dChildYaw={F(Mathf.DeltaAngle(e.yawOffset, cy))}" : "");
                if (t.childCount > 1) child += $" (+{t.childCount - 1} more children)";
            }
            bool changed = dPos.magnitude > 0.005f || Mathf.Abs(dYaw) > 0.5f ||
                           (e != null && t.childCount > 0 && Mathf.Abs(Mathf.DeltaAngle(e.yawOffset, t.GetChild(0).localEulerAngles.y)) > 0.5f);
            sb.AppendLine($"{(changed ? "*" : " ")}[{i}] {name} preset=({F(f.pos.x)},{F(f.pos.y)}) rot={F(f.rotY)} | scene=({F(p.x)},{F(p.z)}) rot={F(yaw)} dPos=({F(dPos.x)},{F(dPos.y)}) dYaw={F(dYaw)}{child}");
        }
        // anything extra in the scene
        foreach (Transform t in furn)
        {
            var parts = t.name.Split('_');
            if (!int.TryParse(parts.Last(), out _)) sb.AppendLine($"extra/renamed object in scene: {t.name}");
        }
        return sb.ToString();
    }
}
