using UnityEditor;
using UnityEngine;

public static class ShaderCheck
{
    public static string Run()
    {
        var s = AssetDatabase.LoadAssetAtPath<Shader>("Assets/CleanerBot/Shaders/CoverageOverlay.shader");
        if (s == null) return "shader asset missing";
        var msgs = ShaderUtil.GetShaderMessages(s);
        string m = "";
        foreach (var x in msgs) m += $"\n{x.severity}: {x.message} (line {x.line})";
        return $"name={s.name} supported={s.isSupported} hasError={ShaderUtil.ShaderHasError(s)} messages={msgs.Length}{m}";
    }
}
