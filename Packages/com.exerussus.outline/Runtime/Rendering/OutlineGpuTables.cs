using System;
using UnityEngine;

namespace Exerussus.Outline.Rendering
{
    /// <summary>
    /// GPU-таблицы записей: данные (32×64 RGBAFloat, строка = id записи), LUT кривых
    /// (256×128 R8, строки id*2 — внешняя, id*2+1 — внутренняя), LUT градиентов (256×128 RGBAHalf,
    /// id*2 — цвет свечения по ширине, id*2+1 — по углу вокруг объекта) и массив текстур стилей. Буферы выделяются в конструкторе,
    /// каждый кадр только перезаполняются. Раскладка столбцов совпадает с OutlineCommon.hlsl.
    /// </summary>
    internal sealed class OutlineGpuTables : IDisposable
    {
        public const int Columns = 32;
        public const int Rows = OutlineApi.SlotCount; // 64
        public const int LutWidth = 256;
        public const int LutRows = Rows * 2;

        private const int ColOuter = 0;
        private const int ColInner = 1;
        private const int ColFill = 2;
        private const int ColRim = 3;
        private const int ColWidths = 4;
        private const int ColMisc = 5;
        private const int ColOcc = 6;
        private const int ColOcc2 = 7;
        private const int ColPulse = 8;
        private const int ColNoise = 9;
        private const int ColPattern = 10;
        private const int ColPattern2 = 11;
        private const int ColPatternSpace = 12;
        private const int ColGradient = 13;
        private const int ColWave = 14;
        private const int ColMarch = 15;
        private const int ColFire = 16;
        private const int ColElectric = 17;
        private const int ColSparkle = 18;
        private const int ColSparkle2 = 19;
        private const int ColScan = 20;
        private const int ColScan2 = 21;
        private const int ColScan3 = 22;
        private const int ColDissolve = 23;
        private const int ColDissolveEdge = 24;
        private const int ColTextures = 25;
        private const int ColSeeThrough = 26;
        private const int ColSeeThrough2 = 27;
        private const int ColSeeTint = 28;
        private const int ColShimmer = 29;

        private readonly Texture2D _data;
        private readonly float[] _dataBuf = new float[Columns * Rows * 4];
        private readonly Texture2D _lut;
        private readonly byte[] _lutBuf = new byte[LutWidth * LutRows];
        private readonly Texture2D _ramp;
        private readonly ushort[] _rampBuf = new ushort[LutWidth * LutRows * 4];
        private readonly OutlineTextureArray _textures = new();
        private readonly OutlineStyle[] _bakedStyle = new OutlineStyle[Rows];
        private readonly int[] _bakedVersion = new int[Rows];
        private readonly bool[] _active = new bool[Rows];
        private readonly Vector4[] _entries = new Vector4[Rows];
        private readonly float[] _surfaceObjectSpace = new float[Rows];
        private readonly bool[] _seeThrough = new bool[Rows];

        public Texture Data => _data;

        /// <summary>Попала ли запись в текущий кадр (жива, есть стиль, fade > 0).</summary>
        public bool IsActive(int id) => _active[id];
        public Texture Lut => _lut;
        public Texture Ramp => _ramp;
        public OutlineTextureArray Textures => _textures;

        /// <summary>Горячие параметры для JFA/композита (uniform-массив): 1/ширина поля, приоритет, группа, есть.</summary>
        public Vector4[] Entries => _entries;

        /// <summary>Максимальная ширина внутреннего контура за кадр, px полного разрешения.</summary>
        public float MaxInnerRange { get; private set; }

        /// <summary>Максимальная дальность поля за кадр, px полного разрешения.</summary>
        public float MaxRange { get; private set; }

        /// <summary>Нужно ли внутреннее поле (есть запись с внутренним контуром) — иначе JFA в один таргет.</summary>
        public bool NeedsInnerField { get; private set; }

        /// <summary>Нужна ли карта позиций поверхности (есть запись с паттерном в пространстве Surface*).</summary>
        public bool NeedsSurface { get; private set; }

        /// <summary>Для маски: 1 — позиция поверхности в координатах объекта, 0 — мира (по id записи).</summary>
        public float[] SurfaceObjectSpace => _surfaceObjectSpace;

        /// <summary>Есть запись в режиме прозрачности/маскировки — нужна копия фона.</summary>
        public bool NeedsBackground { get; private set; }

        /// <summary>Нужен цвет самих объектов (прозрачность с objectOpacity > 0 или переход fade).</summary>
        public bool NeedsObjectColor { get; private set; }

        /// <summary>Запись в режиме прозрачности/маскировки.</summary>
        public bool IsSeeThrough(int id) => _seeThrough[id];

        /// <summary>Сколько записей попало в кадр.</summary>
        public int ActiveCount { get; private set; }

        public OutlineGpuTables()
        {
            _data = new Texture2D(Columns, Rows, TextureFormat.RGBAFloat, false, true)
            {
                name = "OutlineData",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _lut = new Texture2D(LutWidth, LutRows, TextureFormat.R8, false, true)
            {
                name = "OutlineLut",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _ramp = new Texture2D(LutWidth, LutRows, TextureFormat.RGBAHalf, false, true)
            {
                name = "OutlineRamp",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        /// <summary>
        /// Заполнить таблицы под камеру. false — рисовать нечего.
        /// targetWidth/targetHeight — размер цели камеры в px; fieldScale — масштаб поля расстояния.
        /// </summary>
        public bool Build(Camera camera, int targetWidth, int targetHeight, float fieldScale, OutlineSettings settings, float now)
        {
            OutlineApi.Tick(now);
            Array.Clear(_active, 0, _active.Length);
            ActiveCount = 0;
            if (!OutlineApi.HasAny)
                return false;

            Array.Clear(_dataBuf, 0, _dataBuf.Length);
            Array.Clear(_entries, 0, _entries.Length);
            Array.Clear(_surfaceObjectSpace, 0, _surfaceObjectSpace.Length);
            Array.Clear(_seeThrough, 0, _seeThrough.Length);
            bool needsBackground = false;
            bool needsObjectColor = false;
            float maxInner = 0f;
            bool needsSurface = false;
            _textures.BeginFrame();

            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            float resScale = settings.scaleWithResolution ? targetHeight / settings.referenceHeight : 1f;
            float maxWidth = settings.maxWidth * resScale;

            // пикселей на мировую единицу: ортографика — константа, перспектива — делим на глубину
            bool ortho = camera.orthographic;
            float pxPerUnitK = ortho
                ? targetHeight / (2f * Mathf.Max(1e-4f, camera.orthographicSize))
                : targetHeight / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            var camTr = camera.transform;
            Vector3 camPos = camTr.position;
            Vector3 camFwd = camTr.forward;

            bool lutDirty = false;
            float maxRange = 0f;
            int active = 0;
            bool needsInner = false;

            for (int id = 1; id < Rows; id++)
            {
                if (!OutlineApi.IsSlotAlive(id))
                    continue;
                var style = OutlineApi.GetStyle(id);
                if (style == null)
                    continue;

                float fade = OutlineApi.EvaluateFade(id, now);
                if (fade <= 1e-4f)
                    continue;

                float unitScale = resScale;
                if (style.widthMode == OutlineWidthMode.World)
                {
                    float depth = 1f;
                    if (!ortho && OutlineApi.TryGetCenter(id, out var c))
                        depth = Mathf.Max(camera.nearClipPlane, Vector3.Dot(c - camPos, camFwd));
                    unitScale = ortho ? pxPerUnitK : pxPerUnitK / depth;
                }

                float outerPx = Mathf.Min(style.outerWidth * unitScale, maxWidth);
                float innerPx = Mathf.Min(style.innerWidth * unitScale, maxWidth);
                // огонь удлиняет свечение, дрожание сдвигает край — дальность поля и вес записи с запасом на них
                float outerMaxPx = Mathf.Min(outerPx * (1f + style.pulseWidth) * (1f + style.fireAmount)
                    + style.electricWobble * resScale, maxWidth);
                bool hasOuter = style.outerColor.a > 0f && outerPx > 0f;
                bool hasInner = style.innerColor.a > 0f && innerPx > 0f;

                // без params-перегрузки Max — она аллоцирует массив
                maxRange = Mathf.Max(maxRange, Mathf.Max(hasOuter ? outerMaxPx : 0f, hasInner ? innerPx : 0f));
                active++;
                _active[id] = true;
                needsInner |= hasInner;
                if (style.seeThrough)
                {
                    _seeThrough[id] = true;
                    needsBackground = true;
                    needsObjectColor |= style.objectOpacity > 0f || fade < 0.999f;
                    // преломление и мерцание считаются по расстоянию до края внутрь — нужно внутреннее поле
                    if (style.refraction > 0f || style.edgeShimmer.a > 0f)
                    {
                        float w = Mathf.Min(style.refractionWidth * resScale, maxWidth);
                        needsInner = true;
                        maxInner = Mathf.Max(maxInner, w);
                        maxRange = Mathf.Max(maxRange, w);
                    }
                }
                if (hasInner)
                    maxInner = Mathf.Max(maxInner, innerPx);

                Put(id, ColOuter, ToShader(style.outerColor, linear));
                Put(id, ColInner, ToShader(style.innerColor, linear));
                Put(id, ColFill, ToShader(style.fillColor, linear));
                Put(id, ColRim, ToShader(style.rimColor, linear));
                Put(id, ColWidths, new Vector4(outerPx, innerPx, style.rimPower, OutlineApi.GetGroup(id)));
                float priority = (style.priority + OutlineApi.GetPriority(id)) * settings.priorityScale;
                Put(id, ColMisc, new Vector4(fade, style.additive, priority, (float)style.occludedMode));
                Put(id, ColOcc, ToShader(style.occludedTint, linear));
                Put(id, ColOcc2, new Vector4(style.dashPeriod * resScale, style.dashDuty, style.dashSpeed, style.occludedInnerMultiplier));
                Put(id, ColPulse, new Vector4(style.pulseSpeed, style.pulseAlpha, style.pulseWidth, 0f));
                // w: обратная ширина в пикселях кадра — вес записи в JFA (взвешенный Вороной);
                // сиды хранят координаты кадра, поэтому от масштаба поля вес не зависит
                float invOuterField = hasOuter ? 1f / Mathf.Max(0.5f, outerMaxPx) : 1e3f;
                Put(id, ColNoise, new Vector4(style.noiseScale * resScale, style.noiseAmount, style.noiseSpeed, invOuterField));
                _entries[id] = new Vector4(invOuterField, priority, OutlineApi.GetGroup(id), 1f);
                var space = style.patternSpace;
                float angle = style.patternAngle * Mathf.Deg2Rad;
                // якорь (всегда: нужен и для эффектов по углу вокруг объекта): центр первого рендерера на экране,
                // px кадра (y снизу, как SV_Position цели), и пикселей на мировую единицу на его глубине
                var spaceData = new Vector4((float)space, targetWidth * 0.5f, targetHeight * 0.5f, 1f);
                var anchorRenderer = OutlineApi.GetFirstRenderer(id);
                if (anchorRenderer != null)
                {
                    var center = anchorRenderer.bounds.center;
                    var vp = camera.WorldToViewportPoint(center);
                    float depth = Mathf.Max(camera.nearClipPlane, vp.z);
                    float pxPerUnit = ortho ? pxPerUnitK : pxPerUnitK / depth;
                    spaceData = new Vector4((float)space, vp.x * targetWidth, vp.y * targetHeight, Mathf.Max(1e-3f, pxPerUnit));

                    if (space == OutlinePatternSpace.Object)
                    {
                        // поворот объекта вокруг оси взгляда: куда на экране смотрит его ось X
                        var tr = anchorRenderer.transform;
                        float probe = Mathf.Max(0.01f, anchorRenderer.bounds.extents.magnitude);
                        var vp2 = camera.WorldToViewportPoint(center + tr.right * probe);
                        float dx = (vp2.x - vp.x) * targetWidth;
                        float dy = (vp2.y - vp.y) * targetHeight;
                        if (dx * dx + dy * dy > 1e-6f)
                            angle -= Mathf.Atan2(dy, dx);
                    }
                }

                bool surfaceSpace = space == OutlinePatternSpace.SurfaceObject || space == OutlinePatternSpace.SurfaceWorld;
                bool usesSpace = style.pattern != OutlinePatternType.None || style.scanColor.a > 0f
                    || style.dissolve > 0f || style.dissolveByFade || (style.fillTexture != null && style.fillTextureStrength > 0f);
                if (surfaceSpace && usesSpace)
                {
                    needsSurface = true;
                    _surfaceObjectSpace[id] = space == OutlinePatternSpace.SurfaceObject ? 1f : 0f;
                }

                // эффекты свечения и заливки
                Put(id, ColGradient, new Vector4(style.useOuterGradient ? 1f : 0f, style.contourMix, style.contourSpeed, 0f));
                Put(id, ColWave, new Vector4(style.wavePeriod * resScale, style.waveSpeed, style.waveDuty, style.waveStrength));
                Put(id, ColMarch, new Vector4(style.marchCount, style.marchSpeed, style.marchDuty, style.marchStrength));
                Put(id, ColFire, new Vector4(style.fireAmount, style.fireScale * resScale, style.fireSpeed, style.fireFlicker));
                Put(id, ColElectric, new Vector4(style.electricWobble * resScale, style.electricScale * resScale, style.electricSpeed, style.electricArcs));
                Put(id, ColSparkle, ToShader(style.sparkleColor, linear));
                Put(id, ColSparkle2, new Vector4(style.sparkleDensity, style.sparkleSize * resScale, style.sparkleSpeed, 0f));
                Put(id, ColScan, ToShader(style.scanColor, linear));
                var scanDir = style.scanDirection.sqrMagnitude > 1e-8f ? style.scanDirection.normalized : Vector3.up;
                Put(id, ColScan2, new Vector4(scanDir.x, scanDir.y, scanDir.z, style.scanPeriod));
                Put(id, ColScan3, new Vector4(style.scanWidth, style.scanSoftness, style.scanSpeed, 0f));
                Put(id, ColDissolve, new Vector4(style.dissolve, style.dissolveScale, style.dissolveEdgeWidth, style.dissolveByFade ? 1f : 0f));
                Put(id, ColDissolveEdge, ToShader(style.dissolveEdgeColor, linear));
                int patternSlice = style.pattern == OutlinePatternType.Texture ? _textures.Request(style.patternTexture) : -1;
                int fillSlice = style.fillTextureStrength > 0f ? _textures.Request(style.fillTexture) : -1;
                Put(id, ColTextures, new Vector4(patternSlice, fillSlice, style.fillTextureTiling, style.fillTextureStrength));
                Put(id, ColSeeThrough, new Vector4(style.seeThrough ? 1f : 0f, style.objectOpacity,
                    style.distortion * resScale, style.distortionScale * resScale));
                Put(id, ColSeeThrough2, new Vector4(style.distortionSpeed, style.refraction * resScale,
                    style.refractionWidth * resScale, 0f));
                Put(id, ColSeeTint, ToShader(style.seeThroughTint, linear));
                Put(id, ColShimmer, ToShader(style.edgeShimmer, linear));

                float period = space == OutlinePatternSpace.Screen ? style.patternScale * resScale : style.patternWorldScale;
                Put(id, ColPattern, new Vector4((float)style.pattern, period, angle, style.patternSpeed));
                Put(id, ColPatternSpace, spaceData);
                Put(id, ColPattern2, new Vector4(style.patternFill, style.patternStrength,
                    (float)(int)style.patternLayers, style.patternSoftness));

                if (!ReferenceEquals(_bakedStyle[id], style) || _bakedVersion[id] != style.Version)
                {
                    BakeCurve(style.outerCurve, id * 2);
                    BakeCurve(style.innerCurve, id * 2 + 1);
                    BakeGradient(style.outerGradient, id * 2, linear);
                    BakeGradient(style.contourGradient, id * 2 + 1, linear);
                    _bakedStyle[id] = style;
                    _bakedVersion[id] = style.Version;
                    lutDirty = true;
                }
            }

            if (active == 0)
            {
                ActiveCount = 0;
                return false;
            }

            MaxRange = maxRange;
            NeedsInnerField = needsInner;
            NeedsSurface = needsSurface;
            NeedsBackground = needsBackground;
            NeedsObjectColor = needsObjectColor;
            MaxInnerRange = maxInner;
            ActiveCount = active;
            _data.SetPixelData(_dataBuf, 0);
            _data.Apply(false, false);
            if (lutDirty)
            {
                _lut.SetPixelData(_lutBuf, 0);
                _lut.Apply(false, false);
                _ramp.SetPixelData(_rampBuf, 0);
                _ramp.Apply(false, false);
            }
            return true;
        }

        public void Dispose()
        {
            DestroyObject(_data);
            DestroyObject(_lut);
            DestroyObject(_ramp);
            _textures.Dispose();
        }

        private void Put(int row, int col, Vector4 v)
        {
            int i = (row * Columns + col) * 4;
            _dataBuf[i] = v.x;
            _dataBuf[i + 1] = v.y;
            _dataBuf[i + 2] = v.z;
            _dataBuf[i + 3] = v.w;
        }

        private static Vector4 ToShader(Color c, bool linear)
        {
            var l = linear ? c.linear : c;
            return new Vector4(l.r, l.g, l.b, c.a);
        }

        private void BakeCurve(AnimationCurve curve, int row)
        {
            int o = row * LutWidth;
            bool valid = curve != null && curve.length > 0;
            for (int i = 0; i < LutWidth; i++)
            {
                float t = i / (float)(LutWidth - 1);
                float v = valid ? curve.Evaluate(t) : 1f - t;
                _lutBuf[o + i] = (byte)(Mathf.Clamp01(v) * 255f + 0.5f);
            }
        }

        private void BakeGradient(Gradient gradient, int row, bool linear)
        {
            int o = row * LutWidth * 4;
            for (int i = 0; i < LutWidth; i++)
            {
                float t = i / (float)(LutWidth - 1);
                var c = gradient != null ? gradient.Evaluate(t) : Color.white;
                var l = linear ? c.linear : c;
                _rampBuf[o + i * 4] = Mathf.FloatToHalf(l.r);
                _rampBuf[o + i * 4 + 1] = Mathf.FloatToHalf(l.g);
                _rampBuf[o + i * 4 + 2] = Mathf.FloatToHalf(l.b);
                _rampBuf[o + i * 4 + 3] = Mathf.FloatToHalf(c.a);
            }
        }

        private static void DestroyObject(UnityEngine.Object o)
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
