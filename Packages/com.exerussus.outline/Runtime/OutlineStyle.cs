using UnityEngine;

namespace Exerussus.Outline
{
    /// <summary>
    /// Стиль подсветки (конфиг, в рантайме только читается).
    /// Правки в инспекторе подхватываются на лету: цвета и числа — каждый кадр, кривые — по Version.
    /// </summary>
    [CreateAssetMenu(menuName = "Exerussus/Outline/Style", fileName = "OutlineStyle")]
    public sealed class OutlineStyle : ScriptableObject
    {
        [Header("Внешний контур")]
        [ColorUsage(true, true)] public Color outerColor = new(1f, 0.78f, 0.25f, 1f);
        [Tooltip("В чём задана ширина внешнего и внутреннего контура.")]
        public OutlineWidthMode widthMode = OutlineWidthMode.Pixels;
        [Min(0f), Tooltip("Ширина внешнего контура: px или мировые единицы.")]
        public float outerWidth = 10f;
        [Tooltip("Прозрачность по доле ширины: t = 0 у края объекта, t = 1 на внешней границе.")]
        public AnimationCurve outerCurve = DefaultOuterCurve();

        [Header("Внутренний контур")]
        [ColorUsage(true, true)] public Color innerColor = new(1f, 0.78f, 0.25f, 0f);
        [Min(0f)] public float innerWidth = 4f;
        [Tooltip("t = 0 у края, t = 1 на глубине innerWidth внутрь силуэта.")]
        public AnimationCurve innerCurve = DefaultInnerCurve();

        [Header("Заливка и rim")]
        [ColorUsage(true, true)] public Color fillColor = new(1f, 1f, 1f, 0f);
        [ColorUsage(true, true), Tooltip("Френель: альфа = интенсивность.")]
        public Color rimColor = new(1f, 1f, 1f, 0f);
        [Range(0.5f, 16f)] public float rimPower = 3f;

        [Header("Смешение и приоритет")]
        [Range(0f, 1f), Tooltip("0 — обычное альфа-смешение, 1 — чисто аддитивное (свечение).")]
        public float additive = 0f;
        [Tooltip("Базовый приоритет стиля; на стыке групп побеждает больший. Складывается с OutlineOptions.priority.")]
        public int priority = 0;

        [Header("Перекрытая часть")]
        public OutlineOccludedMode occludedMode = OutlineOccludedMode.Custom;
        [ColorUsage(true, true), Tooltip("Множитель цвета/альфы перекрытой части (режим Custom).")]
        public Color occludedTint = new(1f, 1f, 1f, 0.6f);
        [Min(1f), Tooltip("Период штриховки, px. Штрих по диагонали экрана.")]
        public float dashPeriod = 10f;
        [Range(0f, 1f), Tooltip("Доля заполнения штриха; 1 — без штриховки.")]
        public float dashDuty = 0.5f;
        [Tooltip("Скорость бега штриха, периодов в секунду.")]
        public float dashSpeed = 1f;
        [Range(0f, 1f), Tooltip("Множитель внутреннего контура/заливки/rim в перекрытой части.")]
        public float occludedInnerMultiplier = 0.5f;

        [Header("Паттерн")]
        public OutlinePatternType pattern = OutlinePatternType.None;
        [Tooltip("Пространство паттерна: экран, объект (привязка к центру), поверхность объекта или мира.")]
        public OutlinePatternSpace patternSpace = OutlinePatternSpace.Screen;
        [Tooltip("Слои, к которым применяется паттерн.")]
        public OutlinePatternLayers patternLayers = OutlinePatternLayers.Fill;
        [Min(1f), Tooltip("Период паттерна в пространстве Screen, px.")]
        public float patternScale = 8f;
        [Min(0.001f), Tooltip("Период паттерна в пространствах Object и Surface, мировые единицы (для SurfaceObject — единицы объекта).")]
        public float patternWorldScale = 0.1f;
        [Range(-180f, 180f), Tooltip("Поворот, градусы.")]
        public float patternAngle = 45f;
        [Tooltip("Скорость бега, периодов в секунду.")]
        public float patternSpeed = 0f;
        [Range(0f, 1f), Tooltip("Заполнение: толщина полос/линий, размер точек, порог шума.")]
        public float patternFill = 0.5f;
        [Range(0f, 1f), Tooltip("Сила: 0 — паттерн выключен, 1 — полностью вырезает промежутки.")]
        public float patternStrength = 1f;
        [Range(0.5f, 4f), Tooltip("Мягкость краёв паттерна (в пикселях антиалиасинга).")]
        public float patternSoftness = 1f;

        [Header("Анимация")]
        [Min(0f), Tooltip("Частота пульса, Гц. 0 — выключен.")]
        public float pulseSpeed = 0f;
        [Range(0f, 1f)] public float pulseAlpha = 0f;
        [Range(0f, 1f)] public float pulseWidth = 0f;
        [Min(1f), Tooltip("Масштаб шума, px.")]
        public float noiseScale = 24f;
        [Range(0f, 1f)] public float noiseAmount = 0f;
        public float noiseSpeed = 1f;

        [Header("Цвет свечения")]
        [Tooltip("Цвет внешнего контура берётся из градиента по доле ширины (t = 0 у края, 1 — на внешней границе). Альфа градиента умножается на кривую.")]
        public bool useOuterGradient = false;
        [GradientUsage(true)] public Gradient outerGradient = DefaultOuterGradient();

        [Header("Градиент по контуру")]
        [Range(0f, 1f), Tooltip("Сила окраски по углу вокруг центра объекта. 0 — выключено.")]
        public float contourMix = 0f;
        [GradientUsage(true), Tooltip("Цвет по углу: 0…1 — полный оборот.")]
        public Gradient contourGradient = DefaultContourGradient();
        [Tooltip("Вращение градиента, оборотов в секунду.")]
        public float contourSpeed = 0.1f;

        [Header("Волны")]
        [Range(0f, 1f), Tooltip("Кольца, бегущие от силуэта наружу. 0 — выключено.")]
        public float waveStrength = 0f;
        [Min(1f), Tooltip("Расстояние между кольцами, px.")]
        public float wavePeriod = 12f;
        [Tooltip("Скорость, периодов в секунду (отрицательная — внутрь).")]
        public float waveSpeed = 1f;
        [Range(0.05f, 0.95f), Tooltip("Толщина кольца, доля периода.")]
        public float waveDuty = 0.35f;

        [Header("Бег по контуру")]
        [Range(0f, 1f), Tooltip("Штрихи, бегущие вдоль контура по кругу. 0 — выключено.")]
        public float marchStrength = 0f;
        [Min(1f), Tooltip("Число штрихов на оборот.")]
        public float marchCount = 16f;
        [Tooltip("Скорость, оборотов в секунду.")]
        public float marchSpeed = 0.15f;
        [Range(0.05f, 0.95f)] public float marchDuty = 0.5f;

        [Header("Огонь")]
        [Range(0f, 2f), Tooltip("Языки пламени: удлинение свечения шумом, сильнее сверху. 0 — выключено.")]
        public float fireAmount = 0f;
        [Min(1f), Tooltip("Размер языков, px.")]
        public float fireScale = 18f;
        [Tooltip("Скорость подъёма, размеров в секунду.")]
        public float fireSpeed = 1.5f;
        [Range(0f, 1f), Tooltip("Мерцание альфы шумом.")]
        public float fireFlicker = 0.5f;

        [Header("Электричество")]
        [Min(0f), Tooltip("Дрожание края, px. 0 — выключено.")]
        public float electricWobble = 0f;
        [Min(1f), Tooltip("Масштаб шума, px.")]
        public float electricScale = 14f;
        public float electricSpeed = 3f;
        [Range(0f, 4f), Tooltip("Яркость разрядов внутри свечения.")]
        public float electricArcs = 0f;

        [Header("Искры")]
        [Range(0f, 1f), Tooltip("Доля ячеек с искрой. 0 — выключено.")]
        public float sparkleDensity = 0f;
        [Min(2f), Tooltip("Размер ячейки, px.")]
        public float sparkleSize = 12f;
        [Tooltip("Частота мерцания, Гц.")]
        public float sparkleSpeed = 2f;
        [ColorUsage(true, true), Tooltip("Цвет искр, альфа — яркость.")]
        public Color sparkleColor = new(1f, 1f, 1f, 1f);

        [Header("Сканер (заливка)")]
        [ColorUsage(true, true), Tooltip("Цвет полосы, альфа — сила. 0 — выключено. Строится в пространстве паттерна.")]
        public Color scanColor = new(0.4f, 0.9f, 1f, 0f);
        [Tooltip("Направление движения полосы, x и y: в Screen и Object — на экране, в Surface* — в развёртке поверхности (y — высота на боковых гранях).")]
        public Vector3 scanDirection = Vector3.up;
        [Min(0.001f), Tooltip("Расстояние между полосами: px для Screen, единицы мира/объекта для остальных.")]
        public float scanPeriod = 1f;
        [Min(0f), Tooltip("Ширина полосы, в тех же единицах.")]
        public float scanWidth = 0.08f;
        [Min(0f), Tooltip("Мягкость краёв полосы, в тех же единицах.")]
        public float scanSoftness = 0.05f;
        [Tooltip("Скорость, периодов в секунду.")]
        public float scanSpeed = 0.5f;

        [Header("Растворение (заливка)")]
        [Range(0f, 1f), Tooltip("Доля растворённой заливки, внутреннего контура и rim.")]
        public float dissolve = 0f;
        [Tooltip("Растворять по fade подсветки вместо общего затухания: появление и исчезновение «проедаются» шумом.")]
        public bool dissolveByFade = false;
        [Min(0.001f), Tooltip("Размер пятен: px для Screen, единицы мира/объекта для остальных.")]
        public float dissolveScale = 0.15f;
        [Range(0f, 0.5f), Tooltip("Ширина светящейся кромки растворения, доля шума.")]
        public float dissolveEdgeWidth = 0.08f;
        [ColorUsage(true, true)] public Color dissolveEdgeColor = new(1f, 0.5f, 0.1f, 1f);

        [Header("Прозрачность и маскировка")]
        [Tooltip("Объект перестаёт рисоваться камерой, на его месте — фон за ним (с искажением) и объект с непрозрачностью objectOpacity.")]
        public bool seeThrough = false;
        [Range(0f, 1f), Tooltip("Непрозрачность самого объекта: 0 — маскировка (виден только фон), 1 — объект как обычно.")]
        public float objectOpacity = 0.35f;
        [Tooltip("Сохранить тень скрытого объекта (в Play; в редакторе тень пропадает).")]
        public bool seeThroughShadows = true;
        [Min(0f), Tooltip("Искажение фона шумом, px.")]
        public float distortion = 0f;
        [Min(1f), Tooltip("Размер волн искажения, px.")]
        public float distortionScale = 40f;
        [Tooltip("Скорость искажения.")]
        public float distortionSpeed = 0.6f;
        [Min(0f), Tooltip("Преломление у края силуэта (эффект линзы), px.")]
        public float refraction = 0f;
        [Min(1f), Tooltip("Ширина зоны преломления и мерцания от края внутрь, px.")]
        public float refractionWidth = 14f;
        [ColorUsage(true, true), Tooltip("Тинт видимого сквозь объект фона, альфа — сила.")]
        public Color seeThroughTint = new(0.8f, 0.9f, 1f, 0f);
        [ColorUsage(true, true), Tooltip("Мерцание у края силуэта, альфа — сила.")]
        public Color edgeShimmer = new(0.6f, 0.9f, 1.2f, 0f);

        [Header("Текстуры")]
        [Tooltip("Маска паттерна при pattern = Texture: яркость × альфа. Период и поворот — от паттерна.")]
        public Texture2D patternTexture;
        [Tooltip("Текстура заливки: цвет умножается на неё. Строится в пространстве паттерна.")]
        public Texture2D fillTexture;
        [Min(0.001f), Tooltip("Размер тайла заливки: px для Screen, единицы мира/объекта для остальных.")]
        public float fillTextureTiling = 0.5f;
        [Range(0f, 1f)] public float fillTextureStrength = 1f;

        /// <summary>Растёт при любой правке — по нему перепекаются кривые.</summary>
        public int Version { get; private set; }

        /// <summary>Сообщить об изменении кривых из кода.</summary>
        public void MarkChanged() => Version++;

        private void OnValidate() => Version++;

        public static AnimationCurve DefaultOuterCurve()
        {
            // жёсткое ядро ~15% ширины + мягкий хвост
            return new AnimationCurve(
                new Keyframe(0f, 1f, 0f, 0f),
                new Keyframe(0.15f, 1f, 0f, -1.6f),
                new Keyframe(1f, 0f, 0f, 0f));
        }

        public static Gradient DefaultOuterGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 1f, 1f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0.35f), new GradientColorKey(new Color(0.8f, 0.1f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        public static Gradient DefaultContourGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.2f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0.25f),
                    new GradientColorKey(new Color(0.2f, 1f, 0.6f), 0.5f), new GradientColorKey(new Color(0.3f, 0.5f, 1f), 0.75f),
                    new GradientColorKey(new Color(1f, 0.2f, 0.4f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        public static AnimationCurve DefaultInnerCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 1f, 0f, -2f),
                new Keyframe(1f, 0f, 0f, 0f));
        }
    }
}
