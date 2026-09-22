using System;
using UnityEngine;

namespace Exerussus.Outline.Rendering
{
    /// <summary>
    /// GPU-таблицы записей: данные (16×64 RGBAFloat, строка = id записи) и LUT кривых
    /// (256×128 R8, строки id*2 — внешняя, id*2+1 — внутренняя). Буферы выделяются в конструкторе,
    /// каждый кадр только перезаполняются. Раскладка столбцов совпадает с OutlineCommon.hlsl.
    /// </summary>
    internal sealed class OutlineGpuTables : IDisposable
    {
        public const int Columns = 16;
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

        private readonly Texture2D _data;
        private readonly float[] _dataBuf = new float[Columns * Rows * 4];
        private readonly Texture2D _lut;
        private readonly byte[] _lutBuf = new byte[LutWidth * LutRows];
        private readonly OutlineStyle[] _bakedStyle = new OutlineStyle[Rows];
        private readonly int[] _bakedVersion = new int[Rows];
        private readonly bool[] _active = new bool[Rows];
        private readonly Vector4[] _entries = new Vector4[Rows];

        public Texture Data => _data;

        /// <summary>Попала ли запись в текущий кадр (жива, есть стиль, fade > 0).</summary>
        public bool IsActive(int id) => _active[id];
        public Texture Lut => _lut;

        /// <summary>Горячие параметры для JFA/композита (uniform-массив): 1/ширина поля, приоритет, группа, есть.</summary>
        public Vector4[] Entries => _entries;

        /// <summary>Максимальная ширина внутреннего контура за кадр, px полного разрешения.</summary>
        public float MaxInnerRange { get; private set; }

        /// <summary>Максимальная дальность поля за кадр, px полного разрешения.</summary>
        public float MaxRange { get; private set; }

        /// <summary>Нужно ли внутреннее поле (есть запись с внутренним контуром) — иначе JFA в один таргет.</summary>
        public bool NeedsInnerField { get; private set; }

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
        }

        /// <summary>
        /// Заполнить таблицы под камеру. false — рисовать нечего.
        /// targetHeight — высота цели камеры в px; fieldScale — масштаб поля расстояния.
        /// </summary>
        public bool Build(Camera camera, int targetHeight, float fieldScale, OutlineSettings settings, float now)
        {
            OutlineApi.Tick(now);
            Array.Clear(_active, 0, _active.Length);
            ActiveCount = 0;
            if (!OutlineApi.HasAny)
                return false;

            Array.Clear(_dataBuf, 0, _dataBuf.Length);
            Array.Clear(_entries, 0, _entries.Length);
            float maxInner = 0f;

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
                float outerMaxPx = Mathf.Min(outerPx * (1f + style.pulseWidth), maxWidth);
                bool hasOuter = style.outerColor.a > 0f && outerPx > 0f;
                bool hasInner = style.innerColor.a > 0f && innerPx > 0f;

                // без params-перегрузки Max — она аллоцирует массив
                maxRange = Mathf.Max(maxRange, Mathf.Max(hasOuter ? outerMaxPx : 0f, hasInner ? innerPx : 0f));
                active++;
                _active[id] = true;
                needsInner |= hasInner;
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
                Put(id, ColPattern, new Vector4((float)style.pattern, style.patternScale * resScale,
                    style.patternAngle * Mathf.Deg2Rad, style.patternSpeed));
                Put(id, ColPattern2, new Vector4(style.patternFill, style.patternStrength,
                    (float)(int)style.patternLayers, style.patternSoftness));

                if (!ReferenceEquals(_bakedStyle[id], style) || _bakedVersion[id] != style.Version)
                {
                    BakeCurve(style.outerCurve, id * 2);
                    BakeCurve(style.innerCurve, id * 2 + 1);
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
            MaxInnerRange = maxInner;
            ActiveCount = active;
            _data.SetPixelData(_dataBuf, 0);
            _data.Apply(false, false);
            if (lutDirty)
            {
                _lut.SetPixelData(_lutBuf, 0);
                _lut.Apply(false, false);
            }
            return true;
        }

        public void Dispose()
        {
            DestroyObject(_data);
            DestroyObject(_lut);
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
