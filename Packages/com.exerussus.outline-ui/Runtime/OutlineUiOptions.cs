namespace Exerussus.Outline.UI
{
    /// <summary>Параметры подсветки элемента UI.</summary>
    public struct OutlineUiOptions
    {
        /// <summary>Плавное появление, с.</summary>
        public float fadeIn;

        public static OutlineUiOptions Default => new() { fadeIn = 0f };

        public static OutlineUiOptions FadeIn(float seconds) => new() { fadeIn = seconds };
    }
}
