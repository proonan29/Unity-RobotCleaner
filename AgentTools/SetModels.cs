using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Play mode: flip the "Use 3D furniture models" setting through the UI toggle (same path as a click).</summary>
public static class SetModels
{
    public static string Run(bool on)
    {
        var doc = Object.FindAnyObjectByType<UIDocument>();
        var t = doc.rootVisualElement.Q<Toggle>("modelsToggle");
        t.value = on;
        var app = Object.FindAnyObjectByType<CleanerBot.AppController>();
        return $"useModels={app.useModels} house={app.CurrentHouse}";
    }
}
