using System;
using UnityEditor;
using UnityEngine;

namespace Exerussus.Outline.Lab.Editor
{
    /// <summary>
    /// Пресеты стилей площадки: создание ассетов в Generated и сброс уже созданных к эталонным значениям.
    /// </summary>
    public static class OutlineStylePresets
    {
        public const string Folder = "Assets/OutlineLab/Generated";

        private readonly struct Preset
        {
            public readonly string Name;
            public readonly Action<OutlineStyle> Setup;

            public Preset(string name, Action<OutlineStyle> setup)
            {
                Name = name;
                Setup = setup;
            }
        }

        /// <summary>Имена пресетов витрины (все, кроме служебных Hover/Selected/Enemy/Ghost/WorldThin).</summary>
        public static readonly string[] Gallery =
        {
            "Neon", "Toon", "Warning", "Hatched", "Hologram", "Halftone", "HexShield", "Marching",
        };

        /// <summary>Второй ряд витрины: эффекты свечения и заливки, текстуры.</summary>
        public static readonly string[] Effects =
        {
            "Fire", "Electric", "Sonar", "Rainbow", "Sparkle", "Scanner", "Dissolve", "Textured", "Cloak", "Glass",
        };

        private static readonly Preset[] All =
        {
            new("Hover", s =>
            {
                s.outerColor = new Color(1f, 1f, 1f, 0.85f);
                s.outerWidth = 3f;
                s.outerCurve = Curve((0f, 1f), (0.6f, 1f), (1f, 0f));
                s.additive = 0.2f;
                s.occludedMode = OutlineOccludedMode.Hidden;
            }),
            new("Selected", s =>
            {
                s.outerColor = new Color(2.2f, 1.5f, 0.35f, 1f);
                s.outerWidth = 14f;
                s.outerCurve = Curve((0f, 1f), (0.2f, 0.95f), (0.45f, 0.35f), (1f, 0f));
                s.innerColor = new Color(1f, 0.75f, 0.2f, 0.5f);
                s.innerWidth = 3f;
                s.pulseSpeed = 0.7f;
                s.pulseWidth = 0.12f;
                s.priority = 1;
                s.occludedMode = OutlineOccludedMode.Custom;
                s.occludedTint = new Color(1f, 1f, 1f, 0.55f);
            }),
            new("Enemy", s =>
            {
                s.outerColor = new Color(1.8f, 0.2f, 0.12f, 1f);
                s.outerWidth = 9f;
                s.fillColor = new Color(1f, 0.1f, 0.1f, 0.12f);
                s.noiseAmount = 0.2f;
                s.noiseScale = 30f;
                s.occludedMode = OutlineOccludedMode.Custom;
                s.occludedTint = new Color(1f, 0.5f, 0.5f, 0.9f);
                s.dashDuty = 0.45f;
                s.dashSpeed = 1.2f;
                s.occludedInnerMultiplier = 1f;
            }),
            new("Ghost", s =>
            {
                s.outerColor = new Color(0.3f, 1.2f, 1.6f, 0.8f);
                s.outerWidth = 40f;
                s.outerCurve = Curve((0f, 0.8f), (1f, 0f));
                s.innerColor = new Color(0.4f, 1f, 1f, 0.8f);
                s.innerWidth = 8f;
                s.rimColor = new Color(0.4f, 1f, 1f, 0.8f);
                s.rimPower = 2f;
                s.fillColor = new Color(0.2f, 0.8f, 1f, 0.12f);
                s.additive = 1f;
                s.occludedMode = OutlineOccludedMode.Same;
            }),
            new("WorldThin", s =>
            {
                s.outerColor = new Color(0.3f, 1.4f, 0.4f, 1f);
                s.widthMode = OutlineWidthMode.World;
                s.outerWidth = 0.06f;
                s.innerWidth = 0f;
                s.occludedMode = OutlineOccludedMode.Hidden;
            }),

            // --- витрина ---
            new("Neon", s =>
            {
                // тонкое раскалённое ядро + широкий аддитивный ореол (под Bloom)
                s.outerColor = new Color(3f, 0.4f, 2.2f, 1f);
                s.outerWidth = 26f;
                s.outerCurve = Curve((0f, 1f), (0.06f, 1f), (0.14f, 0.35f), (1f, 0f));
                s.rimColor = new Color(1f, 0.3f, 0.9f, 0.4f);
                s.rimPower = 3f;
                s.additive = 1f;
                s.occludedMode = OutlineOccludedMode.Same;
            }),
            new("Toon", s =>
            {
                // жёсткая «тушь» без затухания
                s.outerColor = new Color(0.04f, 0.04f, 0.06f, 1f);
                s.outerWidth = 3f;
                s.outerCurve = Curve((0f, 1f), (0.8f, 1f), (1f, 0f));
                s.occludedMode = OutlineOccludedMode.Hidden;
            }),
            new("Warning", s =>
            {
                s.outerColor = new Color(2f, 0.8f, 0.1f, 1f);
                s.outerWidth = 12f;
                s.pulseSpeed = 2f;
                s.pulseAlpha = 0.6f;
                s.pulseWidth = 0.3f;
                s.fillColor = new Color(1f, 0.5f, 0f, 0.15f);
                s.occludedMode = OutlineOccludedMode.Same;
            }),
            new("Hatched", s =>
            {
                // штриховка заливки + тонкий контур
                s.outerColor = new Color(1f, 0.45f, 0.2f, 1f);
                s.outerWidth = 4f;
                s.outerCurve = Curve((0f, 1f), (0.7f, 1f), (1f, 0f));
                s.fillColor = new Color(1f, 0.45f, 0.2f, 0.45f);
                s.pattern = OutlinePatternType.Stripes;
                s.patternLayers = OutlinePatternLayers.Fill;
                s.patternSpace = OutlinePatternSpace.Object;
                s.patternWorldScale = 0.06f;
                s.patternScale = 8f;
                s.patternAngle = 45f;
                s.patternSpeed = 0.5f;
                s.patternFill = 0.4f;
            }),
            new("Hologram", s =>
            {
                s.outerColor = new Color(0.2f, 1.2f, 1.4f, 0.8f);
                s.outerWidth = 12f;
                s.fillColor = new Color(0.2f, 0.9f, 1f, 0.3f);
                s.rimColor = new Color(0.4f, 1f, 1f, 0.8f);
                s.rimPower = 2.5f;
                s.additive = 1f;
                s.pattern = OutlinePatternType.Scanlines;
                s.patternLayers = OutlinePatternLayers.Fill | OutlinePatternLayers.Outer;
                s.patternScale = 4f;
                s.patternAngle = 0f;
                s.patternSpeed = -1f;
                s.patternFill = 0.8f;
                s.noiseAmount = 0.3f;
                s.noiseScale = 40f;
                s.noiseSpeed = 2f;
                s.occludedMode = OutlineOccludedMode.Same;
            }),
            new("Halftone", s =>
            {
                // свечение, нарезанное точками — «комиксный» полутон
                s.outerColor = new Color(0.6f, 0.8f, 1.6f, 1f);
                s.outerWidth = 22f;
                s.outerCurve = Curve((0f, 1f), (1f, 0f));
                s.pattern = OutlinePatternType.Dots;
                s.patternLayers = OutlinePatternLayers.Outer;
                s.patternScale = 6f;
                s.patternAngle = 30f;
                s.patternFill = 0.8f;
            }),
            new("HexShield", s =>
            {
                s.outerColor = new Color(0.3f, 1.6f, 0.8f, 0.9f);
                s.outerWidth = 20f;
                s.outerCurve = Curve((0f, 0.9f), (1f, 0f));
                s.innerColor = new Color(0.4f, 1.4f, 0.8f, 0.7f);
                s.innerWidth = 6f;
                s.fillColor = new Color(0.3f, 1f, 0.6f, 0.35f);
                s.additive = 0.6f;
                s.pattern = OutlinePatternType.Hex;
                s.patternLayers = OutlinePatternLayers.Fill;
                s.patternSpace = OutlinePatternSpace.SurfaceObject;
                s.patternWorldScale = 0.12f;
                s.patternScale = 14f;
                s.patternAngle = 0f;
                s.patternFill = 0.5f;
            }),
            new("Marching", s =>
            {
                // «бегущие муравьи» по контуру
                s.outerColor = new Color(1.5f, 1.3f, 0.2f, 1f);
                s.outerWidth = 5f;
                s.outerCurve = Curve((0f, 1f), (0.85f, 1f), (1f, 0f));
                s.pattern = OutlinePatternType.Stripes;
                s.patternLayers = OutlinePatternLayers.Outer;
                s.patternScale = 10f;
                s.patternAngle = 45f;
                s.patternSpeed = 1.5f;
                s.patternFill = 0.5f;
                s.occludedMode = OutlineOccludedMode.Hidden;
            }),
        };

        private static readonly Preset[] EffectPresets =
        {
            new("Fire", s =>
            {
                s.outerColor = new Color(2.5f, 1.2f, 0.3f, 1f);
                s.outerWidth = 12f;
                s.useOuterGradient = true;
                s.outerGradient = Grad((0f, new Color(2.5f, 2.2f, 1.4f)), (0.3f, new Color(2.4f, 0.9f, 0.15f)), (1f, new Color(1f, 0.1f, 0.02f)));
                s.fireAmount = 1.2f;
                s.fireScale = 14f;
                s.fireSpeed = 1.8f;
                s.fireFlicker = 0.6f;
                s.additive = 0.6f;
                s.occludedMode = OutlineOccludedMode.Hidden;
            }),
            new("Electric", s =>
            {
                s.outerColor = new Color(0.6f, 1.2f, 3f, 1f);
                s.outerWidth = 8f;
                s.outerCurve = Curve((0f, 1f), (0.25f, 0.6f), (1f, 0f));
                s.electricWobble = 3f;
                s.electricScale = 10f;
                s.electricSpeed = 4f;
                s.electricArcs = 2.5f;
                s.sparkleDensity = 0.15f;
                s.sparkleSize = 10f;
                s.sparkleSpeed = 6f;
                s.sparkleColor = new Color(1.5f, 2f, 3f, 1f);
                s.additive = 0.7f;
            }),
            new("Sonar", s =>
            {
                s.outerColor = new Color(0.3f, 2f, 1.2f, 1f);
                s.outerWidth = 26f;
                s.outerCurve = Curve((0f, 1f), (1f, 0f));
                s.waveStrength = 1f;
                s.wavePeriod = 9f;
                s.waveSpeed = 0.8f;
                s.waveDuty = 0.3f;
                s.additive = 0.5f;
            }),
            new("Rainbow", s =>
            {
                s.outerColor = new Color(2f, 2f, 2f, 1f);
                s.outerWidth = 10f;
                s.outerCurve = Curve((0f, 1f), (0.5f, 0.8f), (1f, 0f));
                s.contourMix = 1f;
                s.contourSpeed = 0.25f;
                s.marchStrength = 0.8f;
                s.marchCount = 18f;
                s.marchSpeed = 0.1f;
                s.marchDuty = 0.6f;
            }),
            new("Sparkle", s =>
            {
                s.outerColor = new Color(1.6f, 1.3f, 2f, 0.7f);
                s.outerWidth = 18f;
                s.outerCurve = Curve((0f, 0.8f), (1f, 0f));
                s.sparkleDensity = 0.6f;
                s.sparkleSize = 7f;
                s.sparkleSpeed = 1.5f;
                s.sparkleColor = new Color(3f, 2.6f, 3.5f, 1f);
                s.additive = 0.6f;
            }),
            new("Scanner", s =>
            {
                s.outerColor = new Color(0.3f, 1.4f, 2f, 1f);
                s.outerWidth = 4f;
                s.fillColor = new Color(0.2f, 0.6f, 1f, 0.12f);
                s.patternSpace = OutlinePatternSpace.SurfaceObject;
                s.scanColor = new Color(0.5f, 2f, 3f, 1f);
                s.scanDirection = Vector3.up;
                s.scanPeriod = 0.7f;
                s.scanWidth = 0.08f;
                s.scanSoftness = 0.06f;
                s.scanSpeed = 0.6f;
                s.additive = 0.5f;
            }),
            new("Dissolve", s =>
            {
                s.outerColor = new Color(1.8f, 0.6f, 0.15f, 1f);
                s.outerWidth = 5f;
                s.fillColor = new Color(0.9f, 0.35f, 0.1f, 0.55f);
                s.patternSpace = OutlinePatternSpace.SurfaceObject;
                s.dissolve = 0.45f;
                s.dissolveScale = 0.25f;
                s.dissolveEdgeWidth = 0.1f;
                s.dissolveEdgeColor = new Color(3f, 1.4f, 0.3f, 1f);
            }),
            new("Cloak", s =>
            {
                // маскировка: объекта нет, фон за ним дрожит и преломляется у края
                s.outerColor = new Color(0f, 0f, 0f, 0f);
                s.outerWidth = 0f;
                s.occludedMode = OutlineOccludedMode.Hidden;
                s.seeThrough = true;
                s.objectOpacity = 0f;
                s.distortion = 3f;
                s.distortionScale = 36f;
                s.distortionSpeed = 0.8f;
                s.refraction = 7f;
                s.refractionWidth = 18f;
                s.seeThroughTint = new Color(0.85f, 0.95f, 1f, 0.25f);
                s.edgeShimmer = new Color(0.5f, 0.8f, 1.1f, 0.35f);
            }),
            new("Glass", s =>
            {
                // прозрачность: полупрозрачный объект с френелем и лёгкой линзой
                s.outerColor = new Color(0.6f, 0.9f, 1.2f, 0.5f);
                s.outerWidth = 3f;
                s.seeThrough = true;
                s.objectOpacity = 0.3f;
                s.refraction = 4f;
                s.refractionWidth = 14f;
                s.seeThroughTint = new Color(0.7f, 0.9f, 1f, 0.35f);
                s.rimColor = new Color(0.8f, 0.95f, 1.2f, 0.5f);
                s.rimPower = 2.5f;
            }),
            new("Textured", s =>
            {
                var tex = GetOrCreateStarsTexture();
                s.outerColor = new Color(1.4f, 1.2f, 2f, 1f);
                s.outerWidth = 12f;
                s.fillColor = new Color(0.8f, 0.7f, 1.2f, 0.6f);
                // заливка держится за поверхность; паттерн свечения (снаружи силуэта) — в пространстве объекта
                s.patternSpace = OutlinePatternSpace.SurfaceObject;
                s.pattern = OutlinePatternType.Texture;
                s.patternTexture = tex;
                s.patternLayers = OutlinePatternLayers.Outer;
                s.patternWorldScale = 0.12f;
                s.patternSpeed = 0.3f;
                s.fillTexture = tex;
                s.fillTextureTiling = 0.25f;
            }),
        };

        public static OutlineStyle GetOrCreate(string name)
        {
            string path = PathOf(name);
            var style = AssetDatabase.LoadAssetAtPath<OutlineStyle>(path);
            if (style != null)
                return style;

            style = ScriptableObject.CreateInstance<OutlineStyle>();
            Find(name).Setup?.Invoke(style);
            AssetDatabase.CreateAsset(style, path);
            return style;
        }

        [MenuItem("Exerussus/Outline/Lab/Сбросить стили к пресетам")]
        public static void ResetAll()
        {
            foreach (var preset in AllPresets())
            {
                var style = AssetDatabase.LoadAssetAtPath<OutlineStyle>(PathOf(preset.Name));
                if (style == null)
                {
                    GetOrCreate(preset.Name);
                    continue;
                }

                var fresh = ScriptableObject.CreateInstance<OutlineStyle>();
                preset.Setup(fresh);
                string keepName = style.name;
                EditorUtility.CopySerialized(fresh, style);
                style.name = keepName;
                UnityEngine.Object.DestroyImmediate(fresh);
                style.MarkChanged();
                EditorUtility.SetDirty(style);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Outline] Стили сброшены к пресетам.");
        }

        private static string PathOf(string name) => $"{Folder}/Style_{name}.asset";

        private static Preset Find(string name)
        {
            foreach (var p in AllPresets())
                if (p.Name == name)
                    return p;
            return new Preset(name, null);
        }

        private static System.Collections.Generic.IEnumerable<Preset> AllPresets()
        {
            foreach (var p in All)
                yield return p;
            foreach (var p in EffectPresets)
                yield return p;
        }

        private static Gradient Grad(params (float t, Color c)[] keys)
        {
            var colors = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                colors[i] = new GradientColorKey(keys[i].c, keys[i].t);
            var g = new Gradient();
            g.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// <summary>Процедурная текстура для пресета Textured: звёзды на прозрачном фоне (PNG в Generated).</summary>
        private static Texture2D GetOrCreateStarsTexture()
        {
            string path = $"{Folder}/Tex_Stars.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            var rnd = new System.Random(7);
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(255, 255, 255, 0);
            for (int k = 0; k < 6; k++)
            {
                float cx = (float)rnd.NextDouble() * size, cy = (float)rnd.NextDouble() * size;
                float r = 8f + (float)rnd.NextDouble() * 12f;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // тайлится: расстояние с переносом через край
                    float dx = Mathf.Abs(x - cx); dx = Mathf.Min(dx, size - dx);
                    float dy = Mathf.Abs(y - cy); dy = Mathf.Min(dy, size - dy);
                    // четырёхлучевая звезда
                    float star = Mathf.Pow(Mathf.Abs(dx / r), 0.5f) + Mathf.Pow(Mathf.Abs(dy / r), 0.5f);
                    float a = Mathf.Clamp01((1.2f - star) * 3f);
                    int i = y * size + x;
                    px[i].a = (byte)Mathf.Max(px[i].a, a * 255f);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.Directory.CreateDirectory(Folder);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static AnimationCurve Curve(params (float t, float v)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (t, v) in keys)
                curve.AddKey(new Keyframe(t, v));
            for (int i = 0; i < curve.length; i++)
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            for (int i = 0; i < curve.length; i++)
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            return curve;
        }
    }
}
