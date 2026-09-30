using System;
using System.IO;
using System.Linq;
using UnityEditor;

/// <summary>Schedule a .unitypackage import from the Asset Store cache on the next editor tick, so the CLI call
/// returns immediately (the import may show a modal "Missing signature" dialog that blocks the main thread).
/// Usage: run_script --file AgentTools/ImportDelayed.cs --args '["Cute_Furniture"]'  (underscore = space)</summary>
public static class ImportDelayed
{
    public static string Run(string match)
    {
        match = match.Replace('_', ' ');
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x");
        var pkg = Directory.GetFiles(root, "*.unitypackage", SearchOption.AllDirectories)
            .FirstOrDefault(p => Path.GetFileName(p).IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0);
        if (pkg == null) return "no package matches " + match;
        EditorApplication.delayCall += () => UnityEditor.AssetPackage.Package.Import(pkg, false);
        return "scheduled import: " + pkg;
    }
}
