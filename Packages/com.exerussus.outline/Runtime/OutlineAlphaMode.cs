namespace Exerussus.Outline
{
    /// <summary>Как маска учитывает альфу материала (_BaseMap.a * _BaseColor.a).</summary>
    public enum OutlineAlphaMode
    {
        /// <summary>По материалу: _AlphaClip → _Cutoff, прозрачный (_Surface = 1) → порог из настроек фичи, иначе без клипа.</summary>
        Auto = 0,
        /// <summary>Всегда клипать по порогу из OutlineOptions.alphaThreshold.</summary>
        ForceClip = 1,
        /// <summary>Игнорировать альфу: силуэт по мешу целиком.</summary>
        Ignore = 2,
    }
}
