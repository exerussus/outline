namespace Exerussus.Outline.UI
{
    /// <summary>Отладочный вид фильтра подсветки (для пикселей внутри силуэта).</summary>
    public enum OutlineUiDebugView
    {
        None = 0,
        /// <summary>R — расстояние внутрь до края / 8 px, G — ширина внутреннего контура / 8 px, B — альфа его цвета.</summary>
        InsideDistance = 1,
        /// <summary>R/G/B — пуст ли сосед слева / справа / сверху (1 — край силуэта).</summary>
        InsideProbes = 2,
    }
}
