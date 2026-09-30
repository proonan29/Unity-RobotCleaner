using System;
using System.Linq;

public static class Probe
{
    public static string Run()
    {
        var names = new[] { "CliCommandAttribute", "CliArgAttribute", "NavMeshSurface", "NavMeshAssetManager", "CollectObjects" };
        var t = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
            .Where(x => names.Contains(x.Name))
            .Select(x => x.FullName + " @ " + x.Assembly.GetName().Name);
        return string.Join("\n", t);
    }
}
