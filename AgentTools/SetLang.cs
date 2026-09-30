using CleanerBot.UI;

/// <summary>Switch UI language (play mode): run_script --file AgentTools/SetLang.cs --args '["ko"]'</summary>
public static class SetLang
{
    public static string Run(string lang)
    {
        Loc.Current = lang.ToLowerInvariant().StartsWith("k") ? Language.Korean : Language.English;
        return "language=" + Loc.Current;
    }
}
