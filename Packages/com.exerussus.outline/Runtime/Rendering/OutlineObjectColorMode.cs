namespace Exerussus.Outline.Rendering
{
    /// <summary>Чем рисуется сам объект в режиме прозрачности.</summary>
    public enum OutlineObjectColorMode
    {
        /// <summary>Упрощённое освещение по _BaseMap/_BaseColor материала: основной свет + окружение.</summary>
        Simple = 0,
        /// <summary>Исходный материал, проход UniversalForward.</summary>
        SourceMaterial = 1,
    }
}
