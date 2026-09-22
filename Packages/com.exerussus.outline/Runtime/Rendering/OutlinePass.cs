using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Exerussus.Outline.Rendering
{
    /// <summary>
    /// Пайплайн подсветки на Render Graph:
    /// 1) Mask — зарегистрированные рендереры поштучно (DrawRenderer, материал-двойник на пару материал+запись)
    ///    → RGBA8 (id, rim, видимость, покрытие). Не зависит от GPU Resident Drawer и куллинга камеры;
    /// 2) JFA Init + Steps — внешнее поле (взвешенное, с наложением на стыке групп) и, если нужно,
    ///    внутреннее (обычное расстояние) — одним MRT-проходом; работа ограничена экранным прямоугольником;
    /// 3) Composite — кривые, заливка, rim, паттерны, перекрытие, анимация, мягкий стык, смешение в цвет камеры.
    /// </summary>
    internal sealed class OutlinePass : ScriptableRenderPass, IDisposable
    {
        // 12 бит на координату в упаковке сида; сиды хранят координаты КАДРА — предел и для кадра
        private const int MaxFieldSize = 4095;

        private readonly OutlineSettings _settings;
        private readonly OutlineMaskMaterials _maskMaterials;
        private readonly Material _jfaMaterial;
        private readonly Material _compositeMaterial;
        private readonly OutlineGpuTables _tables = new();
        private readonly MaterialPropertyBlock _mpb = new();
        private readonly List<DrawItem> _draws = new(256);
        private readonly List<Material> _materialScratch = new(8);

        private readonly ProfilingSampler _maskSampler = new("Outline Mask");
        private readonly ProfilingSampler _initSampler = new("Outline JFA Init");
        private readonly ProfilingSampler _stepSampler = new("Outline JFA Step");
        private readonly ProfilingSampler _compositeSampler = new("Outline Composite");

        // экранный прямоугольник подсвеченного за кадр, px кадра (x0, y0, x1, y1); y — снизу, как SV_Position в RT
        private Vector4 _screenBounds;
        private bool _boundsUnbounded;

        public OutlineStats Stats { get; private set; }

        // авто-разрешение поля: ступени, чтобы пул RT не перевыделялся каждый кадр
        private static readonly float[] ScaleSteps = { 1f, 0.75f, 0.5f, 0.375f, 0.25f };
        private int _autoStep;
        private int _overBudgetFrames;
        private int _underBudgetFrames;
        private bool _warnedSize;

        private readonly struct DrawItem
        {
            public readonly Renderer Renderer;
            public readonly Material Material;
            public readonly int Submesh;

            public DrawItem(Renderer renderer, Material material, int submesh)
            {
                Renderer = renderer;
                Material = material;
                Submesh = submesh;
            }
        }

        private sealed class MaskPassData
        {
            public List<DrawItem> Draws;
            public Vector4 Globals;
        }

        private sealed class FullscreenPassData
        {
            public Material Material;
            public int Pass;
            public MaterialPropertyBlock Mpb;
            public TextureHandle Mask;
            public TextureHandle Seeds;
            public TextureHandle SeedsInner;
            public Texture Data;
            public Texture Lut;
            public Vector4 MaskSize;
            public Vector4 SeedSize;
            public Vector4 Params;
            public Vector4 Params2;
            public Vector4 Rect;
            public Vector4[] Entries;
            public Rect Scissor;
        }

        private struct FrameConsts
        {
            public TextureHandle Mask;
            public Vector4 MaskSize;
            public Vector4 SeedSize;
            public Vector4 Params2;
            public Vector4 Rect;
            public float Time;
        }

        public OutlinePass(OutlineSettings settings, Shader maskShader, Shader jfaShader, Shader compositeShader)
        {
            _settings = settings;
            _maskMaterials = new OutlineMaskMaterials(maskShader);
            _jfaMaterial = CoreUtils.CreateEngineMaterial(jfaShader);
            _compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
            profilingSampler = new ProfilingSampler("Outline");
            ConfigureInput(ScriptableRenderPassInput.Depth);
            // маска и композит работают в пикселях одной ориентации — пишем только в промежуточную цель
            requiresIntermediateTexture = true;

            _maskSampler.enableRecording = true;
            _initSampler.enableRecording = true;
            _stepSampler.enableRecording = true;
            _compositeSampler.enableRecording = true;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_jfaMaterial);
            CoreUtils.Destroy(_compositeMaterial);
            _tables.Dispose();
            _maskMaterials.Dispose();
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            long t0 = Stopwatch.GetTimestamp();

            var cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.cameraType == CameraType.Preview || cameraData.cameraType == CameraType.Reflection)
                return;
            if (cameraData.cameraType == CameraType.SceneView && !_settings.renderInSceneView)
                return;

            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;

            var targetDesc = cameraData.cameraTargetDescriptor;
            int width = targetDesc.width;
            int height = targetDesc.height;
            if (width <= 0 || height <= 0)
                return;
            if (width > MaxFieldSize || height > MaxFieldSize)
            {
                if (!_warnedSize)
                {
                    UnityEngine.Debug.LogWarning($"[Outline] Кадр {width}×{height} больше {MaxFieldSize} px — подсветка пропущена (упаковка сида 12 бит).");
                    _warnedSize = true;
                }
                return;
            }

            float fieldScale = ResolveFieldScale(cameraData);
            int fieldW = Mathf.Clamp(Mathf.CeilToInt(width * fieldScale), 1, MaxFieldSize);
            int fieldH = Mathf.Clamp(Mathf.CeilToInt(height * fieldScale), 1, MaxFieldSize);
            // реальный масштаб после клампа (4K+ при fieldScale = 1 упрётся в 4095)
            fieldScale = Mathf.Min(fieldW / (float)width, fieldH / (float)height);

            float now = OutlineClock.Now;
            var camera = cameraData.camera;
            if (!_tables.Build(camera, height, fieldScale, _settings, now) || !BuildDrawList(camera, width, height))
            {
                StoreStats(cameraData, default);
                return;
            }

            // --- прямоугольник работы в пикселях поля ---
            var rect = new Vector4(0, 0, fieldW, fieldH);
            if (_settings.cropToBounds && !_boundsUnbounded)
            {
                float pad = _tables.MaxRange + _settings.seamBlend + 2f;
                rect.x = Mathf.Clamp(Mathf.Floor((_screenBounds.x - pad) * fieldScale), 0, fieldW);
                rect.y = Mathf.Clamp(Mathf.Floor((_screenBounds.y - pad) * fieldScale), 0, fieldH);
                rect.z = Mathf.Clamp(Mathf.Ceil((_screenBounds.z + pad) * fieldScale), 0, fieldW);
                rect.w = Mathf.Clamp(Mathf.Ceil((_screenBounds.w + pad) * fieldScale), 0, fieldH);
                if (rect.z <= rect.x || rect.w <= rect.y)
                {
                    StoreStats(cameraData, default);
                    return; // всё подсвеченное вне кадра и вне досягаемости свечения
                }
            }

            bool dual = _tables.NeedsInnerField;

            // --- текстуры кадра ---
            var maskDesc = new TextureDesc(width, height)
            {
                name = "_OutlineMask",
                format = GraphicsFormat.R8G8B8A8_UNorm,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.clear,
            };
            var mask = renderGraph.CreateTexture(maskDesc);

            var depthDesc = new TextureDesc(width, height)
            {
                name = "_OutlineMaskDepth",
                format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                clearBuffer = true,
            };
            var maskDepth = renderGraph.CreateTexture(depthDesc);

            var seedDesc = new TextureDesc(fieldW, fieldH)
            {
                format = GraphicsFormat.R8G8B8A8_UNorm,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = false,
            };
            seedDesc.name = "_OutlineSeedsA";
            var outerA = renderGraph.CreateTexture(seedDesc);
            seedDesc.name = "_OutlineSeedsB";
            var outerB = renderGraph.CreateTexture(seedDesc);
            var innerA = TextureHandle.nullHandle;
            var innerB = TextureHandle.nullHandle;
            if (dual)
            {
                seedDesc.name = "_OutlineSeedsInnerA";
                innerA = renderGraph.CreateTexture(seedDesc);
                seedDesc.name = "_OutlineSeedsInnerB";
                innerB = renderGraph.CreateTexture(seedDesc);
            }

            // --- 1. маска ---
            RecordMask(renderGraph, resourceData, mask, maskDepth);

            var frame = new FrameConsts
            {
                Mask = mask,
                MaskSize = new Vector4(width, height, 1f / width, 1f / height),
                SeedSize = new Vector4(fieldW, fieldH, fieldScale, 1f / fieldScale),
                Params2 = new Vector4(_settings.seamOverlay ? 1f : 0f, dual ? 1f : 0f, 0f, 0f),
                Rect = rect,
                Time = now % 3600f,
            };

            // scissor: поле — в его пикселях, композит — в пикселях кадра (y снизу, как у Unity для RT)
            bool useScissor = _settings.cropToBounds && _settings.scissor && !_boundsUnbounded;
            var fieldScissor = useScissor ? new Rect(rect.x, rect.y, rect.z - rect.x, rect.w - rect.y) : Rect.zero;
            var frameScissor = Rect.zero;
            if (useScissor)
            {
                float inv = 1f / fieldScale;
                float fx = Mathf.Floor(rect.x * inv), fy = Mathf.Floor(rect.y * inv);
                frameScissor = new Rect(fx, fy,
                    Mathf.Min(width, Mathf.Ceil(rect.z * inv)) - fx, Mathf.Min(height, Mathf.Ceil(rect.w * inv)) - fy);
            }

            // --- 2. JFA ---
            // внутреннее поле нужно только на дальность внутреннего контура (обычно единицы px) — его шаги
            // включаются лишь на последних проходах; крупные шаги считают одно внешнее поле
            int innerStart = 0;
            if (dual)
                innerStart = Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.CeilToInt(_tables.MaxInnerRange * fieldScale)));

            RecordFullscreen(renderGraph, _initSampler, _jfaMaterial,
                dual ? OutlineShaderIds.PassInitDual : OutlineShaderIds.PassInit,
                outerA, innerA, AccessFlags.WriteAll, TextureHandle.nullHandle, TextureHandle.nullHandle,
                frame, 0f, 0f, fieldScissor);

            var outerCur = outerA;
            var outerNext = outerB;
            var innerCur = innerA;
            var innerNext = innerB;
            int passes = 0;

            float rangeField = _tables.MaxRange * fieldScale;
            int step = Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.CeilToInt(rangeField)));
            // начинаем с половины: ближайшая степень двойки ≥ дальности покрывает её одним шагом
            int extra = _settings.EffectiveExtraPass ? 1 : 0;
            for (step = Mathf.Max(1, step / 2); step >= 1 || extra-- > 0; step >>= 1)
            {
                int s = Mathf.Max(1, step);
                bool withInner = dual && s <= innerStart;
                RecordFullscreen(renderGraph, _stepSampler, _jfaMaterial,
                    withInner ? OutlineShaderIds.PassStepDual : OutlineShaderIds.PassStep,
                    outerNext, withInner ? innerNext : TextureHandle.nullHandle, AccessFlags.WriteAll,
                    outerCur, withInner ? innerCur : TextureHandle.nullHandle, frame, s, 0f, fieldScissor);
                (outerCur, outerNext) = (outerNext, outerCur);
                if (withInner)
                    (innerCur, innerNext) = (innerNext, innerCur);
                passes++;
            }

            // --- 3. композит в цвет камеры (с блендингом — нужен ReadWrite) ---
            RecordFullscreen(renderGraph, _compositeSampler, _compositeMaterial, 0,
                resourceData.activeColorTexture, TextureHandle.nullHandle, AccessFlags.ReadWrite,
                outerCur, dual ? innerCur : outerCur, frame, 0f, (float)_settings.debugView, frameScissor);

            float coverage = (rect.z - rect.x) * (rect.w - rect.y) / (fieldW * (float)fieldH);
            float cpuMs = (Stopwatch.GetTimestamp() - t0) * 1000f / Stopwatch.Frequency;
            StoreStats(cameraData, new OutlineStats(true, _tables.ActiveCount, _draws.Count, passes + 1, dual, fieldW, fieldH, fieldScale,
                coverage, _maskSampler.gpuElapsedTime, _initSampler.gpuElapsedTime + _stepSampler.gpuElapsedTime,
                _compositeSampler.gpuElapsedTime, cpuMs));
        }

        // статистику храним только для игровых камер — Scene View не должен перетирать цифры HUD
        private void StoreStats(UniversalCameraData cameraData, in OutlineStats stats)
        {
            if (cameraData.cameraType == CameraType.Game)
                Stats = stats;
        }

        /// <summary>
        /// Масштаб поля: из настроек (Desktop/WebGL) или авто-ступень по GPU-времени прошлого кадра.
        /// Ступень меняется только от игровой камеры и с гистерезисом (10 кадров вниз, 90 вверх).
        /// </summary>
        private float ResolveFieldScale(UniversalCameraData cameraData)
        {
            float max = _settings.EffectiveFieldScale;
            if (!_settings.autoFieldScale || !SystemInfo.supportsGpuRecorder)
                return max;

            int top = 0;
            while (top < ScaleSteps.Length - 1 && ScaleSteps[top] > max + 1e-4f)
                top++;
            int bottom = top;
            while (bottom < ScaleSteps.Length - 1 && ScaleSteps[bottom + 1] >= _settings.minFieldScale - 1e-4f)
                bottom++;

            if (cameraData.cameraType == CameraType.Game && Stats.Rendered)
            {
                float gpu = Stats.TotalGpuMs;
                if (gpu > 0f)
                {
                    if (gpu > _settings.gpuBudgetMs * 1.15f) { _overBudgetFrames++; _underBudgetFrames = 0; }
                    else if (gpu < _settings.gpuBudgetMs * 0.5f) { _underBudgetFrames++; _overBudgetFrames = 0; }
                    else { _overBudgetFrames = 0; _underBudgetFrames = 0; }

                    if (_overBudgetFrames >= 10) { _autoStep++; _overBudgetFrames = 0; }
                    if (_underBudgetFrames >= 90) { _autoStep--; _underBudgetFrames = 0; }
                }
            }

            _autoStep = Mathf.Clamp(_autoStep, top, bottom);
            return ScaleSteps[_autoStep];
        }

        /// <summary>
        /// Собрать список отрисовки маски (владелец рендерера, активная запись, слой камеры)
        /// и экранный прямоугольник подсвеченного.
        /// </summary>
        private bool BuildDrawList(Camera camera, int targetWidth, int targetHeight)
        {
            _draws.Clear();
            _maskMaterials.TrimIfNeeded();
            int cullingMask = camera.cullingMask;
            float transparentCutoff = _settings.transparentCutoff;

            _screenBounds = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            _boundsUnbounded = false;
            var pixelRect = camera.pixelRect;
            float sx = targetWidth / Mathf.Max(1f, pixelRect.width);
            float sy = targetHeight / Mathf.Max(1f, pixelRect.height);

            for (int id = 1; id < OutlineApi.SlotCount; id++)
            {
                if (!_tables.IsActive(id))
                    continue;

                var mode = OutlineApi.GetAlphaMode(id);
                float threshold = OutlineApi.GetAlphaThreshold(id);
                int count = OutlineApi.GetRendererCount(id);
                for (int i = 0; i < count; i++)
                {
                    var r = OutlineApi.GetRenderer(id, i);
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                        continue;
                    if ((cullingMask & (1 << r.gameObject.layer)) == 0)
                        continue;
                    if (!OutlineApi.IsOwner(r, id))
                        continue;

                    int subCount = SubmeshCount(r);
                    if (subCount <= 0)
                        continue;

                    AccumulateBounds(camera, r.bounds, pixelRect, sx, sy);

                    r.GetSharedMaterials(_materialScratch);
                    int matCount = Mathf.Max(1, _materialScratch.Count);
                    for (int m = 0; m < matCount; m++)
                    {
                        var src = m < _materialScratch.Count ? _materialScratch[m] : null;
                        var mat = _maskMaterials.Get(src, id, mode, threshold, transparentCutoff);
                        _draws.Add(new DrawItem(r, mat, Mathf.Min(m, subCount - 1)));
                    }
                }
            }
            _materialScratch.Clear();
            return _draws.Count > 0;
        }

        private void AccumulateBounds(Camera camera, Bounds b, Rect pixelRect, float sx, float sy)
        {
            if (_boundsUnbounded)
                return;
            var c = b.center;
            var e = b.extents;
            float near = camera.orthographic ? float.MinValue : camera.nearClipPlane;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3(
                    c.x + ((k & 1) != 0 ? e.x : -e.x),
                    c.y + ((k & 2) != 0 ? e.y : -e.y),
                    c.z + ((k & 4) != 0 ? e.z : -e.z));
                var sp = camera.WorldToScreenPoint(corner);
                if (sp.z <= near)
                {
                    // угол за камерой — проекция ненадёжна, считаем на весь кадр
                    _boundsUnbounded = true;
                    return;
                }
                float x = (sp.x - pixelRect.x) * sx;
                float y = (sp.y - pixelRect.y) * sy;
                _screenBounds.x = Mathf.Min(_screenBounds.x, x);
                _screenBounds.y = Mathf.Min(_screenBounds.y, y);
                _screenBounds.z = Mathf.Max(_screenBounds.z, x);
                _screenBounds.w = Mathf.Max(_screenBounds.w, y);
            }
        }

        private static int SubmeshCount(Renderer r)
        {
            switch (r)
            {
                case SkinnedMeshRenderer smr:
                    return smr.sharedMesh != null ? smr.sharedMesh.subMeshCount : 0;
                case MeshRenderer mr:
                    return mr.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 0;
                default:
                    return 0;
            }
        }

        private void RecordMask(RenderGraph renderGraph, UniversalResourceData resourceData,
            TextureHandle mask, TextureHandle maskDepth)
        {
            using var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Outline Mask", out var data, _maskSampler);

            data.Draws = _draws;

            var sceneDepth = resourceData.cameraDepthTexture;
            bool hasDepth = sceneDepth.IsValid();
            if (hasDepth)
                builder.UseTexture(sceneDepth);
            data.Globals = new Vector4(_settings.occlusionBias, 0f, hasDepth ? 1f : 0f, 0f);

            builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(maskDepth, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (MaskPassData d, RasterGraphContext ctx) =>
            {
                ctx.cmd.SetGlobalVector(OutlineShaderIds.MaskGlobals, d.Globals);
                var draws = d.Draws;
                for (int i = 0; i < draws.Count; i++)
                {
                    var item = draws[i];
                    if (item.Renderer != null)
                        ctx.cmd.DrawRenderer(item.Renderer, item.Material, item.Submesh, 0);
                }
            });
        }

        private void RecordFullscreen(RenderGraph renderGraph, ProfilingSampler sampler, Material material, int pass,
            TextureHandle target0, TextureHandle target1, AccessFlags targetAccess,
            TextureHandle seeds, TextureHandle seedsInner, in FrameConsts frame, float step, float debugView,
            Rect scissorRect)
        {
            using var builder = renderGraph.AddRasterRenderPass<FullscreenPassData>(sampler.name, out var data, sampler);

            data.Material = material;
            data.Pass = pass;
            data.Mpb = _mpb;
            data.Mask = frame.Mask;
            data.Seeds = seeds;
            data.SeedsInner = seedsInner;
            data.Data = _tables.Data;
            data.Lut = _tables.Lut;
            data.MaskSize = frame.MaskSize;
            data.SeedSize = frame.SeedSize;
            data.Params = new Vector4(frame.Time, step, _settings.seamBlend, debugView);
            data.Params2 = frame.Params2;
            data.Rect = frame.Rect;
            data.Entries = _tables.Entries;
            data.Scissor = scissorRect;

            builder.UseTexture(frame.Mask);
            if (seeds.IsValid())
                builder.UseTexture(seeds);
            if (seedsInner.IsValid() && !seedsInner.Equals(seeds))
                builder.UseTexture(seedsInner);
            builder.SetRenderAttachment(target0, 0, targetAccess);
            if (target1.IsValid())
                builder.SetRenderAttachment(target1, 1, targetAccess);

            builder.SetRenderFunc(static (FullscreenPassData d, RasterGraphContext ctx) =>
            {
                // MPB копируется в команду при вызове — один экземпляр на все проходы безопасен
                var mpb = d.Mpb;
                mpb.Clear();
                mpb.SetTexture(OutlineShaderIds.Mask, (Texture)d.Mask);
                if (d.Seeds.IsValid())
                    mpb.SetTexture(OutlineShaderIds.Seeds, (Texture)d.Seeds);
                if (d.SeedsInner.IsValid())
                    mpb.SetTexture(OutlineShaderIds.SeedsInner, (Texture)d.SeedsInner);
                mpb.SetTexture(OutlineShaderIds.Data, d.Data);
                mpb.SetTexture(OutlineShaderIds.Lut, d.Lut);
                mpb.SetVector(OutlineShaderIds.MaskSize, d.MaskSize);
                mpb.SetVector(OutlineShaderIds.SeedSize, d.SeedSize);
                mpb.SetVector(OutlineShaderIds.Params, d.Params);
                mpb.SetVector(OutlineShaderIds.Params2, d.Params2);
                mpb.SetVector(OutlineShaderIds.Rect, d.Rect);
                mpb.SetVectorArray(OutlineShaderIds.Entries, d.Entries);
                bool scissor = d.Scissor.width > 0f && d.Scissor.height > 0f;
                if (scissor)
                    ctx.cmd.EnableScissorRect(d.Scissor);
                ctx.cmd.DrawProcedural(Matrix4x4.identity, d.Material, d.Pass, MeshTopology.Triangles, 3, 1, mpb);
                if (scissor)
                    ctx.cmd.DisableScissorRect();
            });
        }
    }
}
