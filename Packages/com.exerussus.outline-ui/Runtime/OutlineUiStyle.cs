using UnityEngine;

namespace Exerussus.Outline.UI
{
    /// <summary>
    /// Стиль подсветки элемента UI (конфиг, в рантайме только читается). Все длины — в пунктах UI
    /// (масштаб панели применяется сам). Отдельно от <see cref="OutlineStyle"/> мира: в UI нет перекрытия,
    /// поверхности, прозрачности и мировых единиц, а привычные ширины — единицы пунктов, а не десятки пикселей.
    /// </summary>
    [CreateAssetMenu(menuName = "Exerussus/OutlineUI/Style", fileName = "OutlineUiStyle")]
    public sealed class OutlineUiStyle : ScriptableObject
    {
        [Header("Свечение снаружи")]
        [ColorUsage(true, true)] public Color outerColor = new(1f, 0.82f, 0.3f, 1f);
        [Min(0f), Tooltip("Ширина свечения, пт.")]
        public float outerWidth = 6f;
        [Tooltip("Прозрачность по доле ширины: t = 0 у края, t = 1 на внешней границе.")]
        public AnimationCurve outerCurve = OutlineStyle.DefaultOuterCurve();

        [Header("Контур внутри")]
        [ColorUsage(true, true)] public Color innerColor = new(1f, 0.82f, 0.3f, 0f);
        [Min(0f), Tooltip("Ширина внутреннего контура, пт.")]
        public float innerWidth = 2f;
        public AnimationCurve innerCurve = OutlineStyle.DefaultInnerCurve();

        [Header("Заливка")]
        [ColorUsage(true, true), Tooltip("Цвет поверх содержимого (альфа — сила).")]
        public Color fillColor = new(1f, 1f, 1f, 0f);
        public Texture2D fillTexture;
        [Min(0.01f), Tooltip("Размер тайла текстуры заливки, пт.")]
        public float fillTextureTiling = 32f;
        [Range(0f, 1f)] public float fillTextureStrength = 1f;

        [Header("Смешение")]
        [Range(0f, 1f), Tooltip("0 — обычное наложение, 1 — чистое сложение (свечение не затемняет фон).")]
        public float additive = 0f;

        [Header("Паттерн")]
        public OutlinePatternType pattern = OutlinePatternType.None;
        [Tooltip("Слои, на которые ложится паттерн.")]
        public OutlinePatternLayers patternLayers = OutlinePatternLayers.Fill;
        [Min(0.1f), Tooltip("Период паттерна, пт (в пространстве элемента — едет вместе с ним).")]
        public float patternScale = 6f;
        public float patternAngle = 45f;
        [Tooltip("Скорость движения, периодов в секунду.")]
        public float patternSpeed = 0f;
        [Range(0f, 1f)] public float patternFill = 0.5f;
        [Range(0f, 1f)] public float patternStrength = 1f;
        [Range(0.5f, 4f)] public float patternSoftness = 1f;
        [Tooltip("Своя текстура для pattern = Texture (яркость × альфа).")]
        public Texture2D patternTexture;

        [Header("Анимация")]
        [Min(0f), Tooltip("Пульс: циклов в секунду.")]
        public float pulseSpeed = 0f;
        [Range(0f, 1f)] public float pulseAlpha = 0f;
        [Range(0f, 1f)] public float pulseWidth = 0f;
        [Min(1f), Tooltip("Размер пятен шума, пт.")]
        public float noiseScale = 12f;
        [Range(0f, 1f)] public float noiseAmount = 0f;
        public float noiseSpeed = 1f;

        [Header("Цвет свечения")]
        [Tooltip("Цвет по ширине свечения из градиента (0 — у края).")]
        public bool useOuterGradient = false;
        [GradientUsage(true)] public Gradient outerGradient = OutlineStyle.DefaultOuterGradient();
        [Range(0f, 1f), Tooltip("Цвет по контуру (угол вокруг элемента): сила.")]
        public float contourMix = 0f;
        public Gradient contourGradient = OutlineStyle.DefaultContourGradient();
        [Tooltip("Вращение градиента по контуру, оборотов в секунду.")]
        public float contourSpeed = 0.1f;

        [Header("Волны")]
        [Range(0f, 1f)] public float waveStrength = 0f;
        [Min(1f), Tooltip("Период колец, пт.")]
        public float wavePeriod = 6f;
        public float waveSpeed = 1f;
        [Range(0.05f, 0.95f)] public float waveDuty = 0.35f;

        [Header("Бег по контуру")]
        [Range(0f, 1f)] public float marchStrength = 0f;
        [Min(1f)] public float marchCount = 16f;
        public float marchSpeed = 0.15f;
        [Range(0.05f, 0.95f)] public float marchDuty = 0.5f;

        [Header("Огонь")]
        [Min(0f)] public float fireAmount = 0f;
        [Min(1f), Tooltip("Размер языков, пт.")]
        public float fireScale = 10f;
        public float fireSpeed = 1.5f;
        [Range(0f, 1f)] public float fireFlicker = 0.5f;

        [Header("Электричество")]
        [Min(0f), Tooltip("Дрожание края, пт.")]
        public float electricWobble = 0f;
        [Min(1f), Tooltip("Масштаб шума, пт.")]
        public float electricScale = 8f;
        public float electricSpeed = 3f;
        [Range(0f, 1f)] public float electricArcs = 0f;

        [Header("Искры")]
        [Range(0f, 1f)] public float sparkleDensity = 0f;
        [Min(2f), Tooltip("Ячейка искр, пт.")]
        public float sparkleSize = 8f;
        public float sparkleSpeed = 2f;
        [ColorUsage(true, true)] public Color sparkleColor = new(1f, 1f, 1f, 1f);

        [Header("Сканер (внутри)")]
        [ColorUsage(true, true)] public Color scanColor = new(0.4f, 0.9f, 1f, 0f);
        [Tooltip("Направление полосы на экране: x вправо, y вверх.")]
        public Vector2 scanDirection = Vector2.up;
        [Min(1f), Tooltip("Период полос, пт.")]
        public float scanPeriod = 60f;
        [Min(0f), Tooltip("Ширина полосы, пт.")]
        public float scanWidth = 6f;
        [Min(0f), Tooltip("Мягкость краёв полосы, пт.")]
        public float scanSoftness = 4f;
        [Tooltip("Периодов в секунду.")]
        public float scanSpeed = 0.5f;

        [Header("Растворение")]
        [Range(0f, 1f)] public float dissolve = 0f;
        [Tooltip("Растворять по fade: при затухании элемент тает шумом, а не гаснет.")]
        public bool dissolveByFade = false;
        [Min(0.5f), Tooltip("Размер пятен шума, пт.")]
        public float dissolveScale = 14f;
        [Range(0f, 0.5f), Tooltip("Ширина светящейся кромки (доля шума).")]
        public float dissolveEdgeWidth = 0.08f;
        [ColorUsage(true, true)] public Color dissolveEdgeColor = new(1f, 0.55f, 0.15f, 1f);

        /// <summary>Растёт при любой правке — по нему перепекаются кривые и градиенты.</summary>
        public int Version { get; private set; }

        /// <summary>Сообщить об изменении кривых или градиентов из кода.</summary>
        public void MarkChanged() => Version++;

        private void OnValidate() => Version++;
    }
}
