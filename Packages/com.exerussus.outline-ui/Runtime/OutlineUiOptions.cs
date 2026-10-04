namespace Exerussus.Outline.UI
{
    /// <summary>Параметры подсветки элемента UI.</summary>
    public struct OutlineUiOptions
    {
        /// <summary>Плавное появление, с.</summary>
        public float fadeIn;

        /// <summary>
        /// Приоритет при переполнении слотов: новая подсветка вытесняет самую неважную с приоритетом ниже.
        /// По умолчанию 0; аварийным подсветкам — выше, декоративным — ниже.
        /// </summary>
        public int priority;

        public static OutlineUiOptions Default => new() { fadeIn = 0f };

        public static OutlineUiOptions FadeIn(float seconds) => new() { fadeIn = seconds };

        public static OutlineUiOptions Priority(int value) => new() { priority = value };

        public OutlineUiOptions WithPriority(int value)
        {
            var o = this;
            o.priority = value;
            return o;
        }
    }
}
