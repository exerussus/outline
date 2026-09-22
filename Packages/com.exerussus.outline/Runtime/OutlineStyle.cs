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

        [Header("Паттерн (экранное пространство)")]
        public OutlinePatternType pattern = OutlinePatternType.None;
        [Tooltip("Слои, к которым применяется паттерн.")]
        public OutlinePatternLayers patternLayers = OutlinePatternLayers.Fill;
        [Min(1f), Tooltip("Период паттерна, px.")]
        public float patternScale = 8f;
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

        public static AnimationCurve DefaultInnerCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 1f, 0f, -2f),
                new Keyframe(1f, 0f, 0f, 0f));
        }
    }
}
