using System;
using System.IO;
using Exerussus.Outline.UI;
using UnityEditor;
using UnityEngine;

namespace Exerussus.Outline.Lab.Editor
{
    /// <summary>
    /// Пресеты стилей подсветки UI (ассеты в Assets/OutlineUiLab/Styles). Ширины — единицы пунктов, как
    /// принято в интерфейсе; эффекты — те же, что у стилей мира, в масштабе элементов UI.
    /// </summary>
    public static class OutlineUiStylePresets
    {
        public const string Folder = "Assets/OutlineUiLab/Styles";

        /// <summary>Все пресеты в порядке показа (первый — ховер).</summary>
        public static readonly string[] Names =
        {
            "Hover", "Selected", "Enemy", "Soft", "Neon", "Warning", "Hatched", "Hologram",
            "Fire", "Electric", "Rainbow", "Sparkle", "Scanner", "Sonar", "Textured", "Dissolve",
        };

        public static OutlineUiStyle GetOrCreate(string name)
        {
            EnsureFolder(Folder);
            string path = PathOf(name);
            var style = AssetDatabase.LoadAssetAtPath<OutlineUiStyle>(path);
            if (style != null)
                return style;
            style = ScriptableObject.CreateInstance<OutlineUiStyle>();
            Setup(name, style);
            AssetDatabase.CreateAsset(style, path);
            return style;
        }

        [MenuItem("Exerussus/OutlineUI/Lab/Сбросить UI-стили к пресетам")]
        public static void ResetAll()
        {
            foreach (var name in Names)
            {
                var style = AssetDatabase.LoadAssetAtPath<OutlineUiStyle>(PathOf(name));
                if (style == null)
                {
                    GetOrCreate(name);
                    continue;
                }
                var fresh = ScriptableObject.CreateInstance<OutlineUiStyle>();
                Setup(name, fresh);
                EditorUtility.CopySerialized(fresh, style);
                style.name = $"UiStyle_{name}";
                style.MarkChanged();
                EditorUtility.SetDirty(style);
                UnityEngine.Object.DestroyImmediate(fresh);
            }
            AssetDatabase.SaveAssets();
        }

        private static void Setup(string name, OutlineUiStyle s)
        {
            var none = new Color(1f, 1f, 1f, 0f);
            switch (name)
            {
                case "Hover":
                    s.outerColor = new Color(0.9f, 0.95f, 1f, 0.55f);
                    s.outerWidth = 4f;
                    s.innerColor = none;
                    break;
                case "Selected":
                    s.outerColor = new Color(1f, 0.8f, 0.25f, 1f);
                    s.outerWidth = 6f;
                    s.innerColor = new Color(1f, 0.85f, 0.35f, 0.9f);
                    s.innerWidth = 1.5f;
                    break;
                case "Enemy":
                    s.outerColor = new Color(1f, 0.25f, 0.2f, 1f);
                    s.outerWidth = 6f;
                    s.innerColor = none;
                    s.pulseSpeed = 1.2f;
                    s.pulseAlpha = 0.35f;
                    break;
                case "Soft":
                    s.outerColor = new Color(0.4f, 0.85f, 1f, 0.8f);
                    s.outerWidth = 14f;
                    s.innerColor = none;
                    s.additive = 0.5f;
                    break;
                case "Neon":
                    s.outerColor = new Color(2f, 0.4f, 1.8f, 1f);
                    s.outerWidth = 5f;
                    s.innerColor = new Color(1.5f, 1.2f, 1.5f, 1f);
                    s.innerWidth = 1f;
                    s.additive = 0.6f;
                    break;
                case "Warning":
                    s.outerColor = new Color(1f, 0.55f, 0.1f, 1f);
                    s.outerWidth = 5f;
                    s.innerColor = none;
                    s.marchStrength = 1f;
                    s.marchCount = 24f;
                    s.marchSpeed = 0.2f;
                    break;
                case "Hatched":
                    s.outerColor = new Color(1f, 0.85f, 0.3f, 0.9f);
                    s.outerWidth = 3f;
                    s.innerColor = none;
                    s.fillColor = new Color(1f, 0.9f, 0.3f, 0.35f);
                    s.pattern = OutlinePatternType.Stripes;
                    s.patternLayers = OutlinePatternLayers.Fill;
                    s.patternScale = 6f;
                    s.patternSpeed = 0.5f;
                    break;
                case "Hologram":
                    s.outerColor = new Color(0.3f, 0.9f, 1f, 0.8f);
                    s.outerWidth = 4f;
                    s.innerColor = none;
                    s.fillColor = new Color(0.3f, 0.9f, 1f, 0.25f);
                    s.pattern = OutlinePatternType.Scanlines;
                    s.patternLayers = OutlinePatternLayers.Fill;
                    s.patternScale = 4f;
                    s.patternAngle = 90f;
                    s.patternSpeed = 1f;
                    s.scanColor = new Color(0.5f, 1f, 1f, 0.6f);
                    s.scanPeriod = 40f;
                    s.scanWidth = 4f;
                    s.scanSoftness = 4f;
                    s.scanSpeed = 0.5f;
                    break;
                case "Fire":
                    s.outerColor = new Color(1f, 0.55f, 0.15f, 1f);
                    s.outerWidth = 8f;
                    s.innerColor = none;
                    s.useOuterGradient = true;
                    s.fireAmount = 1.2f;
                    s.fireScale = 8f;
                    s.fireSpeed = 1.5f;
                    s.fireFlicker = 0.5f;
                    s.additive = 0.4f;
                    break;
                case "Electric":
                    s.outerColor = new Color(0.5f, 0.8f, 1.6f, 1f);
                    s.outerWidth = 5f;
                    s.innerColor = none;
                    s.electricWobble = 1.5f;
                    s.electricScale = 6f;
                    s.electricSpeed = 3f;
                    s.electricArcs = 0.8f;
                    s.additive = 0.5f;
                    break;
                case "Rainbow":
                    s.outerColor = new Color(1f, 1f, 1f, 1f);
                    s.outerWidth = 6f;
                    s.innerColor = none;
                    s.contourMix = 1f;
                    s.contourSpeed = 0.25f;
                    break;
                case "Sparkle":
                    s.outerColor = new Color(1f, 0.85f, 0.4f, 0.7f);
                    s.outerWidth = 8f;
                    s.innerColor = none;
                    s.sparkleDensity = 0.35f;
                    s.sparkleSize = 6f;
                    s.sparkleSpeed = 2f;
                    s.additive = 0.4f;
                    break;
                case "Scanner":
                    s.outerColor = new Color(0.3f, 0.9f, 1f, 0.6f);
                    s.outerWidth = 3f;
                    s.innerColor = none;
                    s.scanColor = new Color(0.4f, 0.9f, 1f, 0.9f);
                    s.scanPeriod = 50f;
                    s.scanWidth = 6f;
                    s.scanSoftness = 5f;
                    s.scanSpeed = 0.6f;
                    break;
                case "Sonar":
                    s.outerColor = new Color(0.3f, 1f, 0.7f, 1f);
                    s.outerWidth = 12f;
                    s.innerColor = none;
                    s.waveStrength = 1f;
                    s.wavePeriod = 5f;
                    s.waveSpeed = 1.2f;
                    s.waveDuty = 0.35f;
                    break;
                case "Textured":
                    s.outerColor = new Color(0.7f, 0.5f, 1f, 0.9f);
                    s.outerWidth = 5f;
                    s.innerColor = none;
                    s.fillColor = new Color(1f, 1f, 1f, 0.55f);
                    s.fillTexture = OutlineStylePresets.StarsTexture();
                    s.fillTextureTiling = 24f;
                    s.fillTextureStrength = 1f;
                    break;
                case "Dissolve":
                    s.outerColor = new Color(1f, 0.5f, 0.1f, 0f);
                    s.innerColor = none;
                    s.dissolve = 0.45f;
                    s.dissolveScale = 12f;
                    s.dissolveEdgeWidth = 0.1f;
                    s.dissolveEdgeColor = new Color(1f, 0.55f, 0.15f, 1f);
                    break;
                default:
                    throw new ArgumentException($"Нет UI-пресета «{name}»");
            }
        }

        private static string PathOf(string name) => $"{Folder}/UiStyle_{name}.asset";

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
