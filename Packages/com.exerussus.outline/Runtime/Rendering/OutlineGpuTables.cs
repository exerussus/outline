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
        private readonly bool[] _surfaceEntry = new bool[Rows];

        // параметры текущего кадра (камера, масштабы) — общие для всех записей
        private struct FrameContext
        {
            public Camera Camera;
            public OutlineSettings Settings;
            public bool Linear;
            public float ResScale;
            public float MaxWidth;
            public bool Ortho;
            public float PxPerUnitK;
            public Vector3 CamPos;
            public Vector3 CamFwd;
            public int Width;
            public int Height;
            public float Now;
        }
        private FrameContext _f;

        // строки для плавной смены стиля
        private readonly float[] _rowPrev = new float[Columns * 4];
        private readonly float[] _rowCur = new float[Columns * 4];

        public Texture Data => _data;

        /// <summary>Попала ли запись в текущий кадр (жива, есть стиль, fade > 0).</summary>
        public bool IsActive(int id) => _active[id];
        public Texture Lut => _lut;
        public Texture Ramp => _ramp;
        public OutlineTextureArray Textures => _textures;

        /// <summary>Горячие параметры для JFA/композита (uniform-массив): 1/ширина поля, приоритет, группа, есть.</summary>
        public Vector4[] Entries => _entries;

        /// <summary>Максимальная ширина внутреннего контура за кадр, px полного разрешения.</summary>
        public float MaxInnerRange => _lMaxInnerRange[_layer];

        /// <summary>Максимальная дальность поля за кадр, px полного разрешения.</summary>
        public float MaxRange => _lMaxRange[_layer];

        /// <summary>Нужно ли внутреннее поле (есть запись с внутренним контуром) — иначе JFA в один таргет.</summary>
        public bool NeedsInnerField => _lNeedsInnerField[_layer];

        /// <summary>Нужна ли карта позиций поверхности (есть запись с паттерном в пространстве Surface*).</summary>
        public bool NeedsSurface => _lNeedsSurface[_layer];

        /// <summary>Для маски: 1 — позиция поверхности в координатах объекта, 0 — мира (по id записи).</summary>
        public float[] SurfaceObjectSpace => _surfaceObjectSpace;

        /// <summary>Записи нужны координаты поверхности (паттерн, сканер, растворение или текстура в Surface*).</summary>
        public bool IsSurfaceEntry(int id) => _surfaceEntry[id];

        /// <summary>Есть запись в режиме прозрачности/маскировки — нужна копия фона.</summary>
        public bool NeedsBackground => _lNeedsBackground[_layer];

        /// <summary>Нужен цвет самих объектов (прозрачность с objectOpacity > 0 или переход fade).</summary>
        public bool NeedsObjectColor => _lNeedsObjectColor[_layer];

        /// <summary>Запись в режиме прозрачности/маскировки.</summary>
        public bool IsSeeThrough(int id) => _seeThrough[id];

        // сводки по слоям; свойства выше отдают значения текущего слоя (SetLayer)
        private readonly float[] _lMaxInnerRange = new float[OutlineApi.MaxLayers];
        private readonly float[] _lMaxRange = new float[OutlineApi.MaxLayers];
        private readonly bool[] _lNeedsInnerField = new bool[OutlineApi.MaxLayers];
        private readonly bool[] _lNeedsSurface = new bool[OutlineApi.MaxLayers];
        private readonly bool[] _lNeedsBackground = new bool[OutlineApi.MaxLayers];
        private readonly bool[] _lNeedsObjectColor = new bool[OutlineApi.MaxLayers];
        private readonly int[] _lActive = new int[OutlineApi.MaxLayers];
        private int _layer;

        /// <summary>Выбрать слой, к которому относятся MaxRange, NeedsInnerField и прочие сводки.</summary>
        public void SetLayer(int layer) => _layer = layer;

        /// <summary>Наибольшая дальность поля слоя, px кадра.</summary>
        public float LayerMaxRange(int layer) => _lMaxRange[layer];

        /// <summary>Записей слоя в кадре.</summary>
        public int LayerActiveCount(int layer) => _lActive[layer];

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
            Array.Clear(_surfaceEntry, 0, _surfaceEntry.Length);
            Array.Clear(_lMaxInnerRange, 0, _lMaxInnerRange.Length);
            Array.Clear(_lMaxRange, 0, _lMaxRange.Length);
            Array.Clear(_lNeedsInnerField, 0, _lNeedsInnerField.Length);
            Array.Clear(_lNeedsSurface, 0, _lNeedsSurface.Length);
            Array.Clear(_lNeedsBackground, 0, _lNeedsBackground.Length);
            Array.Clear(_lNeedsObjectColor, 0, _lNeedsObjectColor.Length);
            Array.Clear(_lActive, 0, _lActive.Length);
            _textures.BeginFrame();

            _f.Camera = camera;
            _f.Settings = settings;
            _f.Linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            _f.ResScale = settings.scaleWithResolution ? targetHeight / settings.referenceHeight : 1f;
            _f.MaxWidth = settings.maxWidth * _f.ResScale;
            _f.Width = targetWidth;
            _f.Height = targetHeight;
            _f.Now = now;

            // пикселей на мировую единицу: ортографика — константа, перспектива — делим на глубину
            _f.Ortho = camera.orthographic;
            _f.PxPerUnitK = _f.Ortho
                ? targetHeight / (2f * Mathf.Max(1e-4f, camera.orthographicSize))
                : targetHeight / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            var camTr = camera.transform;
            _f.CamPos = camTr.position;
            _f.CamFwd = camTr.forward;

            bool lutDirty = false;
            int active = 0;

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
                int L = OutlineApi.GetLayer(id);
                active++;
                _lActive[L]++;
                _active[id] = true;

                float blend = OutlineApi.EvaluateStyleBlend(id, now, out var prev);
                if (prev != null && blend < 1f)
                {
                    // плавная смена стиля: строка считается для обоих стилей и смешивается,
                    // сводки слоя — объединение (поле и проходы нужны обоим)
                    WriteEntry(id, prev, fade, L);
                    Array.Copy(_dataBuf, id * Columns * 4, _rowPrev, 0, Columns * 4);
                    var entryPrev = _entries[id];
                    float surfPrev = _surfaceObjectSpace[id];
                    WriteEntry(id, style, fade, L);
                    BlendRow(id, blend);
                    var entry = Vector4.LerpUnclamped(entryPrev, _entries[id], blend);
                    entry.x = InvLerp(entryPrev.x, _entries[id].x, blend); // вес поля — по ширине, не по 1/ширине
                    _entries[id] = entry;
                    _dataBuf[(id * Columns + ColNoise) * 4 + 3] = entry.x;
                    if (_seeThrough[id])
                        _lNeedsObjectColor[L] = true; // непрозрачность объекта меняется во время перехода
                    if (blend < 0.5f)
                        _surfaceObjectSpace[id] = surfPrev;
                    BlendLuts(id, prev, style, blend);
                    _bakedStyle[id] = null; // по окончании перехода таблицы целевого стиля перепекутся
                    lutDirty = true;
                    continue;
                }

                WriteEntry(id, style, fade, L);
                if (!ReferenceEquals(_bakedStyle[id], style) || _bakedVersion[id] != style.Version)
                {
                    BakeStyle(style, id);
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

        /// <summary>Строка данных записи и её вклад в сводки слоя.</summary>
        private void WriteEntry(int id, OutlineStyle style, float fade, int L)
        {
            var camera = _f.Camera;
            var settings = _f.Settings;
            bool linear = _f.Linear;
            float resScale = _f.ResScale;
            float maxWidth = _f.MaxWidth;
            bool ortho = _f.Ortho;
            float pxPerUnitK = _f.PxPerUnitK;
            Vector3 camPos = _f.CamPos;
            Vector3 camFwd = _f.CamFwd;
            int targetWidth = _f.Width;
            int targetHeight = _f.Height;
            float now = _f.Now;

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
            _lMaxRange[L] = Mathf.Max(_lMaxRange[L], Mathf.Max(hasOuter ? outerMaxPx : 0f, hasInner ? innerPx : 0f));
            _lNeedsInnerField[L] |= hasInner;
            // растворение от временного эффекта (OutlineFx): запись становится прозрачной, объект — непрозрачным
            float fxDissolve = OutlineApi.EvaluateDissolve(id, now);
            bool fx = fxDissolve >= 0f;
            bool seeThrough = style.seeThrough || fx;
            float objectOpacity = fx ? 1f : style.objectOpacity;
            float dissolveAmount = fx ? Mathf.Max(style.dissolve, fxDissolve) : style.dissolve;
            if (seeThrough)
            {
                _seeThrough[id] = true;
                _lNeedsBackground[L] = true;
                _lNeedsObjectColor[L] |= objectOpacity > 0f || fade < 0.999f;
                // преломление и мерцание считаются по расстоянию до края внутрь — нужно внутреннее поле
                if (style.refraction > 0f || style.edgeShimmer.a > 0f)
                {
                    float w = Mathf.Min(style.refractionWidth * resScale, maxWidth);
                    _lNeedsInnerField[L] = true;
                    _lMaxInnerRange[L] = Mathf.Max(_lMaxInnerRange[L], w);
                    _lMaxRange[L] = Mathf.Max(_lMaxRange[L], w);
                }
            }
            if (hasInner)
                _lMaxInnerRange[L] = Mathf.Max(_lMaxInnerRange[L], innerPx);

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
            // диагностика NoRead: композит не читает координаты поверхности, паттерн строится как в Object
            var shaderSpace = settings.surfaceDiag == OutlineSurfaceDiag.NoRead
                && (space == OutlinePatternSpace.SurfaceObject || space == OutlinePatternSpace.SurfaceWorld)
                ? OutlinePatternSpace.Object : space;
            var spaceData = new Vector4((float)shaderSpace, targetWidth * 0.5f, targetHeight * 0.5f, 1f);
            var anchorRenderer = OutlineApi.GetFirstRenderer(id);
            if (anchorRenderer != null)
            {
                var center = anchorRenderer.bounds.center;
                var vp = camera.WorldToViewportPoint(center);
                float depth = Mathf.Max(camera.nearClipPlane, vp.z);
                float pxPerUnit = ortho ? pxPerUnitK : pxPerUnitK / depth;
                spaceData = new Vector4((float)shaderSpace, vp.x * targetWidth, vp.y * targetHeight, Mathf.Max(1e-3f, pxPerUnit));

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
            // координаты поверхности нужны только тому, что рисуется внутри силуэта: паттерн заливки или
            // внутреннего контура, текстура заливки, сканер, растворение (паттерн свечения строится как в Object)
            var patLayers = style.patternLayers;
            bool fillVisible = style.fillColor.a > 0f;
            bool patternInside = style.pattern != OutlinePatternType.None
                && ((fillVisible && (patLayers & OutlinePatternLayers.Fill) != 0)
                    || (hasInner && (patLayers & OutlinePatternLayers.Inner) != 0));
            bool usesSpace = patternInside || style.scanColor.a > 0f || dissolveAmount > 0f || style.dissolveByFade
                || (fillVisible && style.fillTexture != null && style.fillTextureStrength > 0f);
            if (surfaceSpace && usesSpace)
            {
                _lNeedsSurface[L] = true;
                _surfaceEntry[id] = true;
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
            Put(id, ColDissolve, new Vector4(dissolveAmount, style.dissolveScale, style.dissolveEdgeWidth, style.dissolveByFade ? 1f : 0f));
            Put(id, ColDissolveEdge, ToShader(style.dissolveEdgeColor, linear));
            int patternSlice = style.pattern == OutlinePatternType.Texture ? _textures.Request(style.patternTexture) : -1;
            int fillSlice = style.fillTextureStrength > 0f ? _textures.Request(style.fillTexture) : -1;
            Put(id, ColTextures, new Vector4(patternSlice, fillSlice, style.fillTextureTiling, style.fillTextureStrength));
            Put(id, ColSeeThrough, new Vector4(seeThrough ? 1f : 0f, objectOpacity,
                style.distortion * resScale, style.distortionScale * resScale));
            Put(id, ColSeeThrough2, new Vector4(style.distortionSpeed, style.refraction * resScale,
                style.refractionWidth * resScale, 0f));
            Put(id, ColSeeTint, ToShader(style.seeThroughTint, linear));
            Put(id, ColShimmer, ToShader(style.edgeShimmer, linear));
            if (!seeThrough)
            {
                // нейтральные значения: плавная смена стиля между обычным и прозрачным идёт через них
                Put(id, ColSeeThrough, new Vector4(0f, 1f, 0f, 1f));
                Put(id, ColSeeThrough2, Vector4.zero);
                Put(id, ColSeeTint, new Vector4(1f, 1f, 1f, 0f));
                Put(id, ColShimmer, Vector4.zero);
            }

            float period = space == OutlinePatternSpace.Screen ? style.patternScale * resScale : style.patternWorldScale;
            Put(id, ColPattern, new Vector4((float)style.pattern, period, angle, style.patternSpeed));
            Put(id, ColPatternSpace, spaceData);
            Put(id, ColPattern2, new Vector4(style.patternFill, style.patternStrength,
                (float)(int)style.patternLayers, style.patternSoftness));
        }

        // смешать строку записи (сейчас в ней целевой стиль) с сохранённой строкой прежнего стиля
        private void BlendRow(int id, float t)
        {
            int o = id * Columns * 4;
            Array.Copy(_dataBuf, o, _rowCur, 0, Columns * 4);
            for (int i = 0; i < Columns * 4; i++)
                _dataBuf[o + i] = Mathf.LerpUnclamped(_rowPrev[i], _rowCur[i], t);

            // дискретные значения не смешиваются — переключаются на середине перехода
            bool target = t >= 0.5f;
            Pick(o, ColMisc * 4 + 3, target);         // режим перекрытого
            Pick(o, ColDissolve * 4 + 3, target);     // растворение по fade
            Pick(o, ColTextures * 4 + 1, target);     // срез текстуры заливки (сила заливки смешивается)

            // паттерн другого вида (тип, пространство, слои, текстура): прежний гаснет к середине,
            // новый разгорается после — берём столбцы целиком и масштабируем силу
            bool samePattern = _rowPrev[ColPatternSpace * 4] == _rowCur[ColPatternSpace * 4]
                && _rowPrev[ColPattern * 4] == _rowCur[ColPattern * 4]
                && _rowPrev[ColPattern2 * 4 + 2] == _rowCur[ColPattern2 * 4 + 2]
                && _rowPrev[ColTextures * 4] == _rowCur[ColTextures * 4];
            if (!samePattern)
            {
                for (int k = 0; k < 4; k++)
                {
                    Pick(o, ColPattern * 4 + k, target);
                    Pick(o, ColPatternSpace * 4 + k, target);
                    Pick(o, ColPattern2 * 4 + k, target);
                }
                Pick(o, ColTextures * 4, target);
                _dataBuf[o + ColPattern2 * 4 + 1] *= Mathf.Abs(2f * t - 1f);
            }

            // градиент по ширине: у стиля без градиента строка градиента запечена его цветом (BlendLuts)
            _dataBuf[o + ColGradient * 4] = Mathf.Max(_rowPrev[ColGradient * 4], _rowCur[ColGradient * 4]);
            // прозрачность включена на всё время перехода; непрозрачность объекта плавно уходит к 1 у обычного стиля
            _dataBuf[o + ColSeeThrough * 4] = Mathf.Max(_rowPrev[ColSeeThrough * 4], _rowCur[ColSeeThrough * 4]);
        }

        private static float InvLerp(float invA, float invB, float t) =>
            1f / Mathf.Max(1e-4f, Mathf.LerpUnclamped(1f / invA, 1f / invB, t));

        private void Pick(int o, int i, bool target) => _dataBuf[o + i] = target ? _rowCur[i] : _rowPrev[i];

        private void BakeStyle(OutlineStyle style, int id)
        {
            BakeCurve(style.outerCurve, id * 2);
            BakeCurve(style.innerCurve, id * 2 + 1);
            BakeGradient(style.outerGradient, id * 2, _f.Linear);
            BakeGradient(style.contourGradient, id * 2 + 1, _f.Linear);
        }

        // запечённые кривые и градиенты стиля во float — для смешивания без повторного Evaluate каждый кадр
        private sealed class StyleBake
        {
            public int Version = int.MinValue;
            public bool Linear;
            public readonly float[] Lut = new float[LutWidth * 2];      // внешняя, внутренняя кривая
            public readonly float[] Ramp = new float[LutWidth * 2 * 4]; // градиент по ширине, по контуру
            public Vector4 Solid;                                       // цвет свечения (строка «без градиента»)
        }

        private readonly System.Collections.Generic.Dictionary<OutlineStyle, StyleBake> _bakes = new(8);
        // результат последнего смешивания: одинаковые переходы (одна пара стилей, одно время) считаются один раз
        private readonly byte[] _blendLut = new byte[LutWidth * 2];
        private readonly ushort[] _blendRamp = new ushort[LutWidth * 2 * 4];
        private OutlineStyle _blendPrev, _blendStyle;
        private int _blendPrevVersion, _blendStyleVersion;
        private float _blendT = -1f;
        private bool _blendLinear;

        private StyleBake GetBake(OutlineStyle style)
        {
            if (!_bakes.TryGetValue(style, out var b))
            {
                if (_bakes.Count >= 32)
                    _bakes.Clear();
                b = new StyleBake();
                _bakes.Add(style, b);
            }
            if (b.Version == style.Version && b.Linear == _f.Linear)
                return b;
            b.Version = style.Version;
            b.Linear = _f.Linear;
            EvaluateCurve(style.outerCurve, b.Lut, 0);
            EvaluateCurve(style.innerCurve, b.Lut, LutWidth);
            EvaluateGradient(style.outerGradient, b.Ramp, 0, _f.Linear);
            EvaluateGradient(style.contourGradient, b.Ramp, LutWidth * 4, _f.Linear);
            var c = _f.Linear ? style.outerColor.linear : style.outerColor;
            b.Solid = new Vector4(c.r, c.g, c.b, 1f);
            return b;
        }

        private static void EvaluateCurve(AnimationCurve curve, float[] dst, int o)
        {
            bool valid = curve != null && curve.length > 0;
            for (int i = 0; i < LutWidth; i++)
            {
                float t = i / (float)(LutWidth - 1);
                dst[o + i] = Mathf.Clamp01(valid ? curve.Evaluate(t) : 1f - t);
            }
        }

        private static void EvaluateGradient(Gradient gradient, float[] dst, int o, bool linear)
        {
            for (int i = 0; i < LutWidth; i++)
            {
                float t = i / (float)(LutWidth - 1);
                var c = gradient != null ? gradient.Evaluate(t) : Color.white;
                var l = linear ? c.linear : c;
                dst[o + i * 4] = l.r;
                dst[o + i * 4 + 1] = l.g;
                dst[o + i * 4 + 2] = l.b;
                dst[o + i * 4 + 3] = c.a;
            }
        }

        // кривые и градиенты обоих стилей смешиваются построчно; запечённое кэшируется по стилю,
        // смешанное — по паре стилей и доле перехода
        private void BlendLuts(int id, OutlineStyle prev, OutlineStyle style, float t)
        {
            bool same = ReferenceEquals(prev, _blendPrev) && ReferenceEquals(style, _blendStyle) && t == _blendT
                && prev.Version == _blendPrevVersion && style.Version == _blendStyleVersion && _f.Linear == _blendLinear;
            if (!same)
            {
                var a = GetBake(prev);
                var b = GetBake(style);
                for (int i = 0; i < _blendLut.Length; i++)
                    _blendLut[i] = (byte)(Mathf.LerpUnclamped(a.Lut[i], b.Lut[i], t) * 255f + 0.5f);

                // строка по ширине: у стиля без градиента — его цвет свечения, если градиент есть хоть у одного
                bool gradient = prev.useOuterGradient || style.useOuterGradient;
                bool solidA = gradient && !prev.useOuterGradient;
                bool solidB = gradient && !style.useOuterGradient;
                for (int i = 0; i < LutWidth; i++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        int j = i * 4 + k;
                        float va = solidA ? a.Solid[k] : a.Ramp[j];
                        float vb = solidB ? b.Solid[k] : b.Ramp[j];
                        _blendRamp[j] = Mathf.FloatToHalf(Mathf.LerpUnclamped(va, vb, t));
                    }
                }
                for (int j = LutWidth * 4; j < _blendRamp.Length; j++)
                    _blendRamp[j] = Mathf.FloatToHalf(Mathf.LerpUnclamped(a.Ramp[j], b.Ramp[j], t));

                _blendPrev = prev;
                _blendStyle = style;
                _blendT = t;
                _blendPrevVersion = prev.Version;
                _blendStyleVersion = style.Version;
                _blendLinear = _f.Linear;
            }
            Array.Copy(_blendLut, 0, _lutBuf, id * 2 * LutWidth, _blendLut.Length);
            Array.Copy(_blendRamp, 0, _rampBuf, id * 2 * LutWidth * 4, _blendRamp.Length);
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
