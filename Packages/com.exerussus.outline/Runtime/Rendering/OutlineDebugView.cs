namespace Exerussus.Outline.Rendering
{
    /// <summary>Отладочный вывод фичи поверх кадра.</summary>
    public enum OutlineDebugView
    {
        None = 0,
        /// <summary>Маска: цвет по id записи, темнее — перекрытая часть.</summary>
        Mask = 1,
        /// <summary>Сиды JFA: ячейки Вороного по ближайшей записи + полосы расстояния.</summary>
        Seeds = 2,
        /// <summary>Поле расстояния: полосы каждые 8 px, снаружи тёплые, внутри холодные.</summary>
        Distance = 3,
    }
}
