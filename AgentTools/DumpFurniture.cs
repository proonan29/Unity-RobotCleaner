using System.Globalization;
using System.Text;
using CleanerBot;
using UnityEngine;

/// <summary>Dump world position + yaw of every model child under Furniture/* (for comparing scene versions).</summary>
public static class DumpFurniture
{
    static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    public static string Run()
    {
        var house = UnityEngine.Object.FindAnyObjectByType<HouseInstance>();
        var furn = house.transform.Find("Furniture");
        var sb = new StringBuilder();
        foreach (Transform t in furn)
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                sb.AppendLine($"{t.name}/{c.name} {F(c.position.x)} {F(c.position.z)} {F(c.eulerAngles.y)}");
            }
        return sb.ToString();
    }
}
