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
    ///    → RGBA8 (id, rim, видимость, покрытие). Не зависит от GPU Resident Drawer и куллинга камеры.
    ///    Со сглаживанием края маска рисуется в MSAA-цель и резолвится своим проходом: покрытие = доля сэмплов;
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
        private readonly Material _resolveMaterial; // null — сглаживание края недоступно
        private readonly LocalKeyword _resolveSurfaceKeyword;
        private readonly GlobalKeyword _surfaceKeyword = GlobalKeyword.Create("_OUTLINE_SURFACE");
        private readonly Texture2DArray _dummyArray;
        private readonly OutlineGpuTables _tables = new();
        private readonly MaterialPropertyBlock _mpb = new();
        private readonly List<DrawItem> _draws = new(256);
        private readonly List<Material> _materialScratch = new(8);

        private readonly ProfilingSampler _maskSampler = new("Outline Mask");
        private readonly ProfilingSampler _resolveSampler = new("Outline Mask Resolve");
        private readonly ProfilingSampler _initSampler = new("Outline JFA Init");
        private readonly ProfilingSampler _stepSampler = new("Outline JFA Step");
        private readonly ProfilingSampler _compositeSampler = new("Outline Composite");

        // экранный прямоугольник подсвеченного за кадр, px кадра (x0, y0, x1, y1); y — снизу, как SV_Position в RT
        private Vector4 _screenBounds;
        private bool _boundsUnbounded;

        public OutlineStats Stats { get; private set; }

        // авто-масштаб поля игровой камеры (гистерезис)
        private float _autoScale;
        private int _upFrames;
        private bool _warnedSize;

        private readonly struct DrawItem
        {
            public readonly Renderer Renderer;
            public readonly Material Material;
            public readonly Material Source; // исходный материал — для цвета объекта в режиме прозрачности
            public readonly int Submesh;
            public readonly int Entry;

            public DrawItem(Renderer renderer, Material material, Material source, int submesh, int entry)
            {
                Renderer = renderer;
                Material = material;
                Source = source;
                Submesh = submesh;
                Entry = entry;
            }
        }

        private sealed class MaskPassData
        {
            public List<DrawItem> Draws;
            public Vector4 Globals;
            public bool Surface;
            public GlobalKeyword SurfaceKeyword;
            public float[] ObjectSpace;
        }

        private sealed class ResolvePassData
        {
            public Material Material;
            public MaterialPropertyBlock Mpb;
            public TextureHandle MaskMS;
            public TextureHandle PosMS;
            public Vector4 Params;
            public Rect Scissor;
        }

        private sealed class FullscreenPassData
        {
            public Material Material;
            public int Pass;
            public MaterialPropertyBlock Mpb;
            public TextureHandle Mask;
            public TextureHandle Seeds;
            public TextureHandle SeedsInner;
            public TextureHandle Pos;
            public TextureHandle Background;
            public TextureHandle ObjectColor;
            public Texture Data;
            public Texture Lut;
            public Texture Ramp;
            public Texture TexArray;
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
            public TextureHandle Background; // копия цвета камеры (прозрачность/маскировка)
            public TextureHandle ObjectColor; // цвет скрытых объектов их материалами
            public TextureHandle Pos; // позиции поверхности; только для композита, если есть паттерн Surface*
            public Vector4 MaskSize;
            public Vector4 SeedSize;
            public Vector4 Params2;
            public Vector4 Rect;
            public float Time;
        }

        public OutlinePass(OutlineSettings settings, Shader maskShader, Shader jfaShader, Shader compositeShader,
            Shader resolveShader)
        {
            _settings = settings;
            _maskMaterials = new OutlineMaskMaterials(maskShader);
            _jfaMaterial = CoreUtils.CreateEngineMaterial(jfaShader);
            _compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
            if (resolveShader != null && resolveShader.isSupported && SystemInfo.supportsMultisampledTextures != 0)
            {
                _resolveMaterial = CoreUtils.CreateEngineMaterial(resolveShader);
                _resolveSurfaceKeyword = new LocalKeyword(resolveShader, "_OUTLINE_SURFACE");
            }
            _dummyArray = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false)
            {
                name = "OutlineDummyArray",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _dummyArray.SetPixels(new[] { Color.white }, 0);
            _dummyArray.Apply(false, true);
            profilingSampler = new ProfilingSampler("Outline");
            ConfigureInput(ScriptableRenderPassInput.Depth);
            // маска и композит работают в пикселях одной ориентации — пишем только в промежуточную цель
            requiresIntermediateTexture = true;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_jfaMaterial);
            CoreUtils.Destroy(_compositeMaterial);
            CoreUtils.Destroy(_resolveMaterial);
            CoreUtils.Destroy(_dummyArray);
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

            float now = OutlineClock.Now;
            var camera = cameraData.camera;
            if (!_tables.Build(camera, width, height, 1f, _settings, now) || !BuildDrawList(camera, width, height))
            {
                StoreStats(cameraData, default);
                return;
            }

            // --- область работы в пикселях кадра (x0, y0, x1, y1) ---
            var area = new Vector4(0, 0, width, height);
            if (_settings.cropToBounds && !_boundsUnbounded)
            {
                float pad = _tables.MaxRange + _settings.seamBlend + 2f;
                area.x = Mathf.Clamp(Mathf.Floor(_screenBounds.x - pad), 0, width);
                area.y = Mathf.Clamp(Mathf.Floor(_screenBounds.y - pad), 0, height);
                area.z = Mathf.Clamp(Mathf.Ceil(_screenBounds.z + pad), 0, width);
                area.w = Mathf.Clamp(Mathf.Ceil(_screenBounds.w + pad), 0, height);
                if (area.z <= area.x || area.w <= area.y)
                {
                    StoreStats(cameraData, default);
                    return; // всё подсвеченное вне кадра и вне досягаемости свечения
                }
            }

            bool dual = _tables.NeedsInnerField;
            bool extra = _settings.EffectiveExtraPass;
            float areaPx = (area.z - area.x) * (area.w - area.y);
            float fieldScale = ResolveFieldScale(cameraData, areaPx, dual, extra, out long cost);

            int fieldW = Mathf.Clamp(Mathf.CeilToInt(width * fieldScale), 1, MaxFieldSize);
            int fieldH = Mathf.Clamp(Mathf.CeilToInt(height * fieldScale), 1, MaxFieldSize);
            // реальный масштаб после округления размеров
            fieldScale = Mathf.Min(fieldW / (float)width, fieldH / (float)height);

            // --- прямоугольник работы в пикселях поля ---
            var rect = new Vector4(
                Mathf.Clamp(Mathf.Floor(area.x * fieldScale), 0, fieldW),
                Mathf.Clamp(Mathf.Floor(area.y * fieldScale), 0, fieldH),
                Mathf.Clamp(Mathf.Ceil(area.z * fieldScale), 0, fieldW),
                Mathf.Clamp(Mathf.Ceil(area.w * fieldScale), 0, fieldH));


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

            // --- текстуры кадра ---
            int samples = ResolveEdgeSamples(width, height);
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

            // позиции поверхности для паттернов Surface*: half-float, xyz + ось нормали
            bool surface = _tables.NeedsSurface;
            var posDesc = new TextureDesc(width, height)
            {
                name = "_OutlinePos",
                format = GraphicsFormat.R16G16B16A16_SFloat,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.clear,
            };
            var pos = surface ? renderGraph.CreateTexture(posDesc) : TextureHandle.nullHandle;

            var depthDesc = new TextureDesc(width, height)
            {
                name = "_OutlineMaskDepth",
                format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                clearBuffer = true,
            };

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
            if (samples > 1)
            {
                var msDesc = maskDesc;
                msDesc.name = "_OutlineMaskMS";
                msDesc.msaaSamples = (MSAASamples)samples;
                msDesc.bindTextureMS = true;
                var maskMS = renderGraph.CreateTexture(msDesc);
                var msDepthDesc = depthDesc;
                msDepthDesc.name = "_OutlineMaskDepthMS";
                msDepthDesc.msaaSamples = (MSAASamples)samples;
                var maskDepthMS = renderGraph.CreateTexture(msDepthDesc);
                var posMS = TextureHandle.nullHandle;
                if (surface)
                {
                    var posMsDesc = posDesc;
                    posMsDesc.name = "_OutlinePosMS";
                    posMsDesc.msaaSamples = (MSAASamples)samples;
                    posMsDesc.bindTextureMS = true;
                    posMS = renderGraph.CreateTexture(posMsDesc);
                }
                RecordMask(renderGraph, resourceData, maskMS, maskDepthMS, posMS);
                RecordResolve(renderGraph, maskMS, mask, posMS, pos, samples, frameScissor);
            }
            else
            {
                var maskDepth = renderGraph.CreateTexture(depthDesc);
                RecordMask(renderGraph, resourceData, mask, maskDepth, pos);
            }

            var frame = new FrameConsts
            {
                Mask = mask,
                Pos = pos,
                MaskSize = new Vector4(width, height, 1f / width, 1f / height),
                SeedSize = new Vector4(fieldW, fieldH, fieldScale, 1f / fieldScale),
                Params2 = new Vector4(_settings.seamOverlay ? 1f : 0f, dual ? 1f : 0f, _tables.MaxRange, 0f),
                Rect = rect,
                Time = now % 3600f,
            };

            // --- 2. JFA ---
            // внутреннее поле нужно только на дальность внутреннего контура (обычно единицы px) — его шаги
            // включаются лишь на последних проходах; крупные шаги считают одно внешнее поле
            int innerStart = dual ? OutlineQuality.InnerStartStep(_tables.MaxInnerRange, fieldScale) : 0;

            RecordFullscreen(renderGraph, _initSampler, _jfaMaterial,
                dual ? OutlineShaderIds.PassInitDual : OutlineShaderIds.PassInit,
                outerA, innerA, AccessFlags.WriteAll, TextureHandle.nullHandle, TextureHandle.nullHandle,
                frame, 0f, 0f, fieldScissor);

            var outerCur = outerA;
            var outerNext = outerB;
            var innerCur = innerA;
            var innerNext = innerB;
            int passes = 0;

            // шаги: от StartStep до 1, затем (опционально) ещё один шаг 1 — та же раскладка, что в OutlineQuality
            int start = OutlineQuality.StartStep(_tables.MaxRange, fieldScale);
            int total = 0;
            for (int st = start; st >= 1; st >>= 1)
                total++;
            if (extra)
                total++;

            for (int i = 0; i < total; i++)
            {
                int s = Mathf.Max(1, start >> i);
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

            // --- 3. новые текстуры стилей → массив (только когда появились) ---
            if (_tables.Textures.HasPending)
                RecordTextureUpload(renderGraph);

            // --- 4. прозрачность/маскировка: фон (копия цвета камеры) и цвет самих объектов ---
            if (_tables.NeedsBackground)
            {
                var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                var bgDesc = new TextureDesc(width, height)
                {
                    name = "_OutlineBackground",
                    format = colorDesc.format,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                };
                frame.Background = renderGraph.CreateTexture(bgDesc);
                RecordBackgroundCopy(renderGraph, resourceData.activeColorTexture, frame.Background, frameScissor);

                if (_tables.NeedsObjectColor)
                {
                    var objDesc = new TextureDesc(width, height)
                    {
                        name = "_OutlineObjectColor",
                        format = GraphicsFormat.R16G16B16A16_SFloat,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        clearBuffer = true,
                        clearColor = Color.clear,
                    };
                    frame.ObjectColor = renderGraph.CreateTexture(objDesc);
                    var objDepth = renderGraph.CreateTexture(new TextureDesc(width, height)
                    {
                        name = "_OutlineObjectDepth",
                        format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                        clearBuffer = true,
                    });
                    RecordObjectColor(renderGraph, frame.ObjectColor, objDepth);
                }
            }

            // --- 5. композит в цвет камеры (с блендингом — нужен ReadWrite) ---
            RecordFullscreen(renderGraph, _compositeSampler, _compositeMaterial, 0,
                resourceData.activeColorTexture, TextureHandle.nullHandle, AccessFlags.ReadWrite,
                outerCur, dual ? innerCur : outerCur, frame, 0f, (float)_settings.debugView, frameScissor);

            float coverage = (rect.z - rect.x) * (rect.w - rect.y) / (fieldW * (float)fieldH);
            float cpuMs = (Stopwatch.GetTimestamp() - t0) * 1000f / Stopwatch.Frequency;
            StoreStats(cameraData, new OutlineStats(true, _tables.ActiveCount, _draws.Count, passes + 1, dual,
                fieldW, fieldH, fieldScale, coverage, cost, _settings.EffectiveFieldBudget, samples, cpuMs));
        }

        // статистику храним только для игровых камер — Scene View не должен перетирать цифры HUD
        private void StoreStats(UniversalCameraData cameraData, in OutlineStats stats)
        {
            if (cameraData.cameraType == CameraType.Game)
                Stats = stats;
        }

        /// <summary>
        /// Масштаб поля: фиксированный из настроек или авто по бюджету (OutlineQuality).
        /// Для игровой камеры — гистерезис: вниз сразу, если текущий масштаб превышает бюджет на 10%,
        /// вверх — после 30 кадров подряд, когда больший масштаб укладывается.
        /// </summary>
        private float ResolveFieldScale(UniversalCameraData cameraData, float areaPx, bool dual, bool extra, out long cost)
        {
            float max = _settings.EffectiveFieldScale;
            if (!_settings.autoFieldScale)
            {
                cost = OutlineQuality.Cost(areaPx, _tables.MaxRange, _tables.MaxInnerRange, max, dual, extra);
                return max;
            }

            long budget = _settings.EffectiveFieldBudget;
            float min = Mathf.Min(_settings.minFieldScale, max);
            float target = OutlineQuality.Choose(areaPx, _tables.MaxRange, _tables.MaxInnerRange, dual, extra,
                max, min, budget, out cost);

            if (cameraData.cameraType != CameraType.Game)
                return target;

            if (_autoScale <= 0f || _autoScale > max + 1e-4f || _autoScale < min - 1e-4f)
            {
                _autoScale = target;
                _upFrames = 0;
            }
            else if (target < _autoScale)
            {
                long current = OutlineQuality.Cost(areaPx, _tables.MaxRange, _tables.MaxInnerRange, _autoScale, dual, extra);
                if (current > budget * 1.1f)
                    _autoScale = target;
                _upFrames = 0;
            }
            else if (target > _autoScale)
            {
                if (++_upFrames >= 30)
                {
                    _autoScale = target;
                    _upFrames = 0;
                }
            }
            else
            {
                _upFrames = 0;
            }

            cost = OutlineQuality.Cost(areaPx, _tables.MaxRange, _tables.MaxInnerRange, _autoScale, dual, extra);
            return _autoScale;
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
                        _draws.Add(new DrawItem(r, mat, src, Mathf.Min(m, subCount - 1), id));
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
            TextureHandle mask, TextureHandle maskDepth, TextureHandle pos)
        {
            using var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Outline Mask", out var data, _maskSampler);

            data.Draws = _draws;

            var sceneDepth = resourceData.cameraDepthTexture;
            bool hasDepth = sceneDepth.IsValid();
            if (hasDepth)
                builder.UseTexture(sceneDepth);
            data.Globals = new Vector4(_settings.occlusionBias, 0f, hasDepth ? 1f : 0f, 0f);

            builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
            data.Surface = pos.IsValid();
            data.SurfaceKeyword = _surfaceKeyword;
            data.ObjectSpace = _tables.SurfaceObjectSpace;
            if (data.Surface)
                builder.SetRenderAttachment(pos, 1, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(maskDepth, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (MaskPassData d, RasterGraphContext ctx) =>
            {
                ctx.cmd.SetGlobalVector(OutlineShaderIds.MaskGlobals, d.Globals);
                if (d.Surface)
                {
                    ctx.cmd.SetGlobalFloatArray(OutlineShaderIds.MaskObjectSpace, d.ObjectSpace);
                    ctx.cmd.EnableKeyword(d.SurfaceKeyword);
                }
                var draws = d.Draws;
                for (int i = 0; i < draws.Count; i++)
                {
                    var item = draws[i];
                    if (item.Renderer != null)
                        ctx.cmd.DrawRenderer(item.Renderer, item.Material, item.Submesh, 0);
                }
                if (d.Surface)
                    ctx.cmd.DisableKeyword(d.SurfaceKeyword);
            });
        }

        /// <summary>Число сэмплов маски: из настроек, если резолв доступен и формат поддерживает MSAA.</summary>
        private int ResolveEdgeSamples(int width, int height)
        {
            int requested = _settings.EffectiveEdgeSamples;
            if (requested <= 1 || _resolveMaterial == null)
                return 1;
            var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm,
                SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil)) { msaaSamples = requested };
            return Mathf.Max(1, SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc));
        }

        private void RecordResolve(RenderGraph renderGraph, TextureHandle maskMS, TextureHandle mask,
            TextureHandle posMS, TextureHandle pos, int samples, Rect scissorRect)
        {
            bool surface = posMS.IsValid() && pos.IsValid();
            _resolveMaterial.SetKeyword(_resolveSurfaceKeyword, surface);
            using var builder = renderGraph.AddRasterRenderPass<ResolvePassData>(_resolveSampler.name, out var data, _resolveSampler);
            data.Material = _resolveMaterial;
            data.Mpb = _mpb;
            data.MaskMS = maskMS;
            data.PosMS = surface ? posMS : TextureHandle.nullHandle;
            data.Params = new Vector4(samples, 0f, 0f, 0f);
            data.Scissor = scissorRect;

            builder.UseTexture(maskMS);
            builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
            if (surface)
            {
                builder.UseTexture(posMS);
                builder.SetRenderAttachment(pos, 1, AccessFlags.Write);
            }
            builder.SetRenderFunc(static (ResolvePassData d, RasterGraphContext ctx) =>
            {
                var mpb = d.Mpb;
                mpb.Clear();
                mpb.SetTexture(OutlineShaderIds.MaskMS, (Texture)d.MaskMS);
                if (d.PosMS.IsValid())
                    mpb.SetTexture(OutlineShaderIds.PosMS, (Texture)d.PosMS);
                mpb.SetVector(OutlineShaderIds.ResolveParams, d.Params);
                bool scissor = d.Scissor.width > 0f && d.Scissor.height > 0f;
                if (scissor)
                    ctx.cmd.EnableScissorRect(d.Scissor);
                ctx.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 0, MeshTopology.Triangles, 3, 1, mpb);
                if (scissor)
                    ctx.cmd.DisableScissorRect();
            });
        }

        private sealed class CopyPassData
        {
            public TextureHandle Source;
            public Rect Scissor;
        }

        private sealed class ObjectColorPassData
        {
            public List<DrawItem> Draws;
            public OutlineGpuTables Tables;
        }

        private void RecordBackgroundCopy(RenderGraph renderGraph, TextureHandle source, TextureHandle target, Rect scissor)
        {
            using var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Outline Background Copy", out var data, _compositeSampler);
            data.Source = source;
            data.Scissor = scissor;
            builder.UseTexture(source);
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            builder.SetRenderFunc(static (CopyPassData d, RasterGraphContext ctx) =>
            {
                bool sc = d.Scissor.width > 0f && d.Scissor.height > 0f;
                if (sc)
                    ctx.cmd.EnableScissorRect(d.Scissor);
                Blitter.BlitTexture(ctx.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), 0f, false);
                if (sc)
                    ctx.cmd.DisableScissorRect();
            });
        }

        /// <summary>
        /// Скрытые объекты (прозрачность) их собственными материалами — проход UniversalForward, освещение
        /// кадра берётся из глобальных данных URP.
        /// </summary>
        private void RecordObjectColor(RenderGraph renderGraph, TextureHandle target, TextureHandle depth)
        {
            using var builder = renderGraph.AddRasterRenderPass<ObjectColorPassData>("Outline Object Color", out var data, _maskSampler);
            data.Draws = _draws;
            data.Tables = _tables;
            builder.UseAllGlobalTextures(true);
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (ObjectColorPassData d, RasterGraphContext ctx) =>
            {
                var draws = d.Draws;
                for (int i = 0; i < draws.Count; i++)
                {
                    var item = draws[i];
                    if (item.Renderer == null || item.Source == null || !d.Tables.IsSeeThrough(item.Entry))
                        continue;
                    int pass = item.Source.FindPass("UniversalForward");
                    if (pass < 0)
                        pass = item.Source.FindPass("UniversalForwardOnly");
                    if (pass < 0)
                        pass = 0;
                    ctx.cmd.DrawRenderer(item.Renderer, item.Source, item.Submesh, pass);
                }
            });
        }

        private sealed class UploadPassData
        {
            public OutlineTextureArray Textures;
        }

        private void RecordTextureUpload(RenderGraph renderGraph)
        {
            using var builder = renderGraph.AddUnsafePass<UploadPassData>("Outline Style Textures", out var data);
            data.Textures = _tables.Textures;
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (UploadPassData d, UnsafeGraphContext ctx) =>
            {
                d.Textures.FlushPending(CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd));
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
            bool composite = material == _compositeMaterial;
            data.Pos = composite ? frame.Pos : TextureHandle.nullHandle;
            data.Background = composite ? frame.Background : TextureHandle.nullHandle;
            data.ObjectColor = composite ? frame.ObjectColor : TextureHandle.nullHandle;
            data.Data = _tables.Data;
            data.Lut = _tables.Lut;
            data.Ramp = _tables.Ramp;
            data.TexArray = _tables.Textures.Array != null ? _tables.Textures.Array : _dummyArray;
            data.MaskSize = frame.MaskSize;
            data.SeedSize = frame.SeedSize;
            data.Params = new Vector4(frame.Time, step, _settings.seamBlend, debugView);
            data.Params2 = frame.Params2;
            data.Rect = frame.Rect;
            data.Entries = _tables.Entries;
            data.Scissor = scissorRect;

            builder.UseTexture(frame.Mask);
            if (data.Pos.IsValid())
                builder.UseTexture(data.Pos);
            if (data.Background.IsValid())
                builder.UseTexture(data.Background);
            if (data.ObjectColor.IsValid())
                builder.UseTexture(data.ObjectColor);
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
                // без карты позиций шейдер её не читает, но слот должен быть занят
                mpb.SetTexture(OutlineShaderIds.Pos, d.Pos.IsValid() ? (Texture)d.Pos : (Texture)d.Mask);
                mpb.SetTexture(OutlineShaderIds.Background, d.Background.IsValid() ? (Texture)d.Background : Texture2D.blackTexture);
                mpb.SetTexture(OutlineShaderIds.ObjectColor, d.ObjectColor.IsValid() ? (Texture)d.ObjectColor : Texture2D.blackTexture);
                if (d.Seeds.IsValid())
                    mpb.SetTexture(OutlineShaderIds.Seeds, (Texture)d.Seeds);
                if (d.SeedsInner.IsValid())
                    mpb.SetTexture(OutlineShaderIds.SeedsInner, (Texture)d.SeedsInner);
                mpb.SetTexture(OutlineShaderIds.Data, d.Data);
                mpb.SetTexture(OutlineShaderIds.Lut, d.Lut);
                mpb.SetTexture(OutlineShaderIds.Ramp, d.Ramp);
                mpb.SetTexture(OutlineShaderIds.TexArray, d.TexArray);
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
