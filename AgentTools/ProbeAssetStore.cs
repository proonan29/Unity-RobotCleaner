using System;
using System.Linq;
using System.Reflection;
using System.Text;

/// <summary>Inspect the Package Manager service container and the purchase-list types (internal Editor API).</summary>
public static class ProbeAssetStore
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => a.GetName().Name.StartsWith("UnityEditor"))
        .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
        .FirstOrDefault(t => t.FullName == name);

    static void Dump(StringBuilder sb, Type t, int max = 40)
    {
        if (t == null) { sb.AppendLine("  (missing)"); return; }
        sb.AppendLine($"== {t.FullName} : {t.BaseType?.Name}");
        foreach (var c in t.GetConstructors(All)) sb.AppendLine("   ctor " + c);
        foreach (var f in t.GetFields(All).Take(max)) sb.AppendLine($"   field {f.FieldType.Name} {f.Name}{(f.IsStatic ? " (static)" : "")}");
        foreach (var p in t.GetProperties(All).Take(max)) sb.AppendLine($"   prop {p.PropertyType.Name} {p.Name}");
        foreach (var m in t.GetMethods(All).Where(m => !m.IsSpecialName).Take(max)) sb.AppendLine("   " + m);
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        const string ns = "UnityEditor.PackageManager.UI.Internal.";
        Dump(sb, T(ns + "ServicesContainer"), 30);
        Dump(sb, T(ns + "PurchasesQueryArgs"));
        Dump(sb, T(ns + "AssetStorePurchases"));
        Dump(sb, T(ns + "AssetStorePurchaseInfo"));
        Dump(sb, T(ns + "AssetStoreLocalInfo"));

        var proxyT = T(ns + "AssetStoreCachePathProxy");
        sb.AppendLine("cache proxy ctors: " + string.Join(" | ", proxyT.GetConstructors(All).Select(c => c.ToString())));
        return sb.ToString();
    }
}
