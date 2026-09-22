namespace Exerussus.Outline
{
    /// <summary>Как рисовать часть подсветки, перекрытую другими объектами сцены.</summary>
    public enum OutlineOccludedMode
    {
        /// <summary>Не рисовать.</summary>
        Hidden = 0,
        /// <summary>Рисовать так же, как видимую часть.</summary>
        Same = 1,
        /// <summary>Отдельный стиль: тинт, штрих, множитель заливки.</summary>
        Custom = 2,
    }
}
