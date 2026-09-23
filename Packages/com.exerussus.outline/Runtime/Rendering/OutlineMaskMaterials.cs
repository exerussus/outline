using System;
using System.Collections.Generic;
using UnityEngine;

namespace Exerussus.Outline.Rendering
{
    /// <summary>
    /// Кэш материалов-двойников маски: один на пару (исходный материал, id записи).
    /// Двойник несёт id записи и порог клипа, а текстуру/цвет альфы каждый кадр копирует с исходника
    /// (понимает URP Lit/Unlit — _BaseMap/_BaseColor/_Cutoff, legacy — _MainTex/_Color, glTFast —
    /// baseColorTexture/baseColorFactor/alphaCutoff). Материалы создаются при первом появлении пары.
    /// </summary>
    internal sealed class OutlineMaskMaterials : IDisposable
    {
        private const int TrimThreshold = 512;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int GltfBaseTexId = Shader.PropertyToID("baseColorTexture");
        private static readonly int GltfBaseFactorId = Shader.PropertyToID("baseColorFactor");
        private static readonly int GltfCutoffId = Shader.PropertyToID("alphaCutoff");
        private static readonly int OutlineIdId = Shader.PropertyToID("_OutlineId");
        private static readonly int OutlineClipId = Shader.PropertyToID("_OutlineClip");

        private readonly Shader _shader;
        private readonly Dictionary<(Material, int), Material> _cache = new(128);

        public OutlineMaskMaterials(Shader shader) => _shader = shader;

        /// <summary>Вызывать раз в кадр до Get: не даёт кэшу расти бесконечно (исходники могли умереть).</summary>
        public void TrimIfNeeded()
        {
            if (_cache.Count > TrimThreshold)
                Clear();
        }

        public Material Get(Material source, int id, OutlineAlphaMode mode, float threshold, float transparentCutoff)
        {
            var key = (source, id);
            if (!_cache.TryGetValue(key, out var mat) || mat == null)
            {
                mat = new Material(_shader) { name = "OutlineMask", hideFlags = HideFlags.HideAndDontSave };
                mat.SetFloat(OutlineIdId, id);
                _cache[key] = mat;
            }
            Sync(source, mat, mode, threshold, transparentCutoff);
            return mat;
        }

        public void Dispose() => Clear();

        private void Clear()
        {
            foreach (var pair in _cache)
                Destroy(pair.Value);
            _cache.Clear();
        }

        private static void Sync(Material src, Material dst, OutlineAlphaMode mode, float threshold, float transparentCutoff)
        {
            Texture tex = null;
            var st = new Vector4(1f, 1f, 0f, 0f);
            // полный цвет — для прохода цвета объекта (прозрачность); маске нужна только альфа
            var color = Color.white;
            float cutoff = 0.5f;
            bool alphaTest = false;
            bool transparent = false;

            if (src != null)
            {
                if (src.HasTexture(BaseMapId)) ReadTexture(src, BaseMapId, ref tex, ref st);
                else if (src.HasTexture(MainTexId)) ReadTexture(src, MainTexId, ref tex, ref st);
                else if (src.HasTexture(GltfBaseTexId)) ReadTexture(src, GltfBaseTexId, ref tex, ref st);

                if (src.HasColor(BaseColorId)) color = src.GetColor(BaseColorId);
                else if (src.HasColor(ColorId)) color = src.GetColor(ColorId);
                else if (src.HasColor(GltfBaseFactorId)) color = src.GetColor(GltfBaseFactorId);

                if (src.HasFloat(CutoffId)) cutoff = src.GetFloat(CutoffId);
                else if (src.HasFloat(GltfCutoffId)) cutoff = src.GetFloat(GltfCutoffId);

                int queue = src.renderQueue;
                alphaTest = src.IsKeywordEnabled("_ALPHATEST_ON")
                            || (src.HasFloat(AlphaClipId) && src.GetFloat(AlphaClipId) > 0.5f)
                            || (queue >= 2450 && queue <= 2500);
                transparent = queue > 2500 || (src.HasFloat(SurfaceId) && src.GetFloat(SurfaceId) > 0.5f);
            }

            float clip = mode switch
            {
                OutlineAlphaMode.ForceClip => threshold,
                OutlineAlphaMode.Ignore => -1f,
                _ => alphaTest ? cutoff : transparent ? transparentCutoff : -1f,
            };

            dst.SetTexture(BaseMapId, tex != null ? tex : Texture2D.whiteTexture);
            dst.SetVector(BaseMapStId, st);
            // SetColor в линейном пространстве сам переводит sRGB-цвет материала, как у исходного шейдера
            dst.SetColor(BaseColorId, color);
            dst.SetFloat(OutlineClipId, clip);
        }

        private static void ReadTexture(Material src, int id, ref Texture tex, ref Vector4 st)
        {
            tex = src.GetTexture(id);
            var scale = src.GetTextureScale(id);
            var offset = src.GetTextureOffset(id);
            st = new Vector4(scale.x, scale.y, offset.x, offset.y);
        }

        private static void Destroy(UnityEngine.Object o)
        {
            if (o == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(o);
            else
                UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
