using System;

namespace Exerussus.Outline
{
    /// <summary>К каким слоям подсветки применяется паттерн.</summary>
    [Flags]
    public enum OutlinePatternLayers
    {
        None = 0,
        Outer = 1,
        Inner = 2,
        Fill = 4,
        All = Outer | Inner | Fill,
    }
}
