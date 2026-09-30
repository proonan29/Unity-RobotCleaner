using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace CleanerBot.EditorTools
{
    /// <summary>Korean glyph support for the UI: Noto Sans KR (SIL OFL) as a dynamic fallback font.</summary>
    public static class FontCommands
    {
        const string FontsFolder = "Assets/CleanerBot/UI/Fonts";
        const string SourceFontPath = FontsFolder + "/NotoSansKR-Regular.otf";
        const string FontAssetPath = FontsFolder + "/NotoSansKR-Regular SDF.asset";
        const string TextSettingsPath = "Assets/CleanerBot/UI/CleanerBotTextSettings.asset";
        const string PanelSettingsPath = "Assets/CleanerBot/UI/CleanerBotPanelSettings.asset";

        [CliCommand("cb_setup_font", "Create the Noto Sans KR dynamic font asset and register it as a fallback in the UI's PanelTextSettings.")]
        public static string SetupFont([CliArg("force", "Recreate the font asset even if it exists")] bool force = false)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (font == null) return $"error: font file not found/imported: {SourceFontPath}";

            var fa = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (fa != null && force)
            {
                AssetDatabase.DeleteAsset(FontAssetPath);
                fa = null;
            }
            if (fa == null)
            {
                fa = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (fa == null) return "error: FontAsset.CreateFontAsset returned null";
                fa.name = "NotoSansKR-Regular SDF";
                fa.isMultiAtlasTexturesEnabled = true;
                AssetDatabase.CreateAsset(fa, FontAssetPath);

                // atlas texture(s) and material must live inside the asset
                if (fa.atlasTextures != null)
                    foreach (var tex in fa.atlasTextures)
                    {
                        if (tex == null) continue;
                        tex.name = fa.name + " Atlas";
                        AssetDatabase.AddObjectToAsset(tex, fa);
                    }
                if (fa.material != null)
                {
                    fa.material.name = fa.name + " Material";
                    AssetDatabase.AddObjectToAsset(fa.material, fa);
                }
                EditorUtility.SetDirty(fa);
            }

            var ts = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (ts == null)
            {
                ts = ScriptableObject.CreateInstance<PanelTextSettings>();
                AssetDatabase.CreateAsset(ts, TextSettingsPath);
            }
            if (ts.fallbackFontAssets == null) ts.fallbackFontAssets = new System.Collections.Generic.List<FontAsset>();
            if (!ts.fallbackFontAssets.Contains(fa)) ts.fallbackFontAssets.Add(fa);
            ts.clearDynamicDataOnBuild = true;
            EditorUtility.SetDirty(ts);

            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps != null)
            {
                ps.textSettings = ts;
                EditorUtility.SetDirty(ps);
            }
            AssetDatabase.SaveAssets();

            bool hasHangul = fa.HasCharacter('한', true, true);
            return $"font={FontAssetPath} textSettings={TextSettingsPath} panel={(ps != null ? "assigned" : "MISSING")} " +
                   $"fallbacks={ts.fallbackFontAssets.Count} hangul_glyph={hasHangul}";
        }
    }
}
