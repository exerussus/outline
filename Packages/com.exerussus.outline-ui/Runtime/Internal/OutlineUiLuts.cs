using System.Collections.Generic;
using UnityEngine;

namespace Exerussus.Outline.UI.Internal
{
    /// <summary>
    /// LUT стиля 256×4 RGBAHalf: 0 — внешняя кривая, 1 — внутренняя (в r), 2 — цвет по ширине свечения
    /// (у стиля без градиента — его цвет свечения, чтобы плавная смена стиля не прыгала), 3 — цвет по контуру.
    /// Кэш по стилю и его версии; пересчёт — только при изменении стиля.
    /// </summary>
    internal static class OutlineUiLuts
    {
        private const int Width = 256;

        private sealed class Entry
        {
            public Texture2D Texture;
            public int Version;
        }

        private static readonly Dictionary<OutlineStyle, Entry> s_Cache = new(16);
        private static readonly ushort[] s_Buffer = new ushort[Width * 4 * 4];

        public static Texture2D Get(OutlineStyle style)
        {
            if (!s_Cache.TryGetValue(style, out var e))
            {
                if (s_Cache.Count >= 64)
                    Clear();
                e = new Entry
                {
                    Texture = new Texture2D(Width, 4, TextureFormat.RGBAHalf, false, true)
                    {
                        name = "OutlineUi Lut",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave,
                    },
                    Version = int.MinValue,
                };
                s_Cache.Add(style, e);
            }
            if (e.Version != style.Version || e.Texture == null)
            {
                if (e.Texture == null)
                    e.Texture = new Texture2D(Width, 4, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
                Bake(style, e.Texture);
                e.Version = style.Version;
            }
            return e.Texture;
        }

        private static void Bake(OutlineStyle s, Texture2D tex)
        {
            BakeCurve(s.outerCurve, 0, x => 1f - x);
            BakeCurve(s.innerCurve, 1, x => 1f - x);
            if (s.useOuterGradient)
                BakeGradient(s.outerGradient, 2);
            else
                BakeSolid(s.outerColor, 2);
            BakeGradient(s.contourGradient, 3);
            tex.SetPixelData(s_Buffer, 0);
            tex.Apply(false, false);
        }

        private static void BakeCurve(AnimationCurve curve, int row, System.Func<float, float> fallback)
        {
            bool valid = curve != null && curve.length > 0;
            int o = row * Width * 4;
            for (int i = 0; i < Width; i++)
            {
                float t = i / (float)(Width - 1);
                float v = Mathf.Clamp01(valid ? curve.Evaluate(t) : fallback(t));
                ushort h = Mathf.FloatToHalf(v);
                s_Buffer[o + i * 4] = h;
                s_Buffer[o + i * 4 + 1] = h;
                s_Buffer[o + i * 4 + 2] = h;
                s_Buffer[o + i * 4 + 3] = h;
            }
        }

        private static void BakeGradient(Gradient g, int row)
        {
            int o = row * Width * 4;
            for (int i = 0; i < Width; i++)
            {
                float t = i / (float)(Width - 1);
                var c = g != null ? g.Evaluate(t) : Color.white;
                Put(o + i * 4, c.r, c.g, c.b, c.a);
            }
        }

        private static void BakeSolid(Color c, int row)
        {
            int o = row * Width * 4;
            for (int i = 0; i < Width; i++)
                Put(o + i * 4, c.r, c.g, c.b, 1f);
        }

        private static void Put(int i, float r, float g, float b, float a)
        {
            s_Buffer[i] = Mathf.FloatToHalf(r);
            s_Buffer[i + 1] = Mathf.FloatToHalf(g);
            s_Buffer[i + 2] = Mathf.FloatToHalf(b);
            s_Buffer[i + 3] = Mathf.FloatToHalf(a);
        }

        private static void Clear()
        {
            foreach (var e in s_Cache.Values)
            {
                if (e.Texture == null)
                    continue;
                if (Application.isPlaying)
                    Object.Destroy(e.Texture);
                else
                    Object.DestroyImmediate(e.Texture);
            }
            s_Cache.Clear();
        }
    }
}
