using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Open the settings section and close the summary (for screenshots).</summary>
public static class OpenSettings
{
    public static string Run()
    {
        var doc = Object.FindAnyObjectByType<UIDocument>();
        var root = doc.rootVisualElement;
        root.Q("settingsSection").RemoveFromClassList("hidden");
        root.Q("summaryPanel").AddToClassList("hidden");
        return "settings open";
    }
}
