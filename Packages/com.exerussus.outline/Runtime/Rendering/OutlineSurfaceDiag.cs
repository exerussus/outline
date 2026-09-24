namespace Exerussus.Outline.Rendering
{
    /// <summary>
    /// Диагностика цены координат поверхности (Surface*) для бенчмарка: отключает части пути или включает прежний.
    /// Картинка в режимах NoRead и NoWrite неверная.
    /// </summary>
    public enum OutlineSurfaceDiag
    {
        /// <summary>Обычная работа: при сглаживании координаты пишет отдельный проход без MSAA.</summary>
        None = 0,
        /// <summary>Координаты пишутся, но композит их не читает (паттерн — как в Object).</summary>
        NoRead = 1,
        /// <summary>Координаты не пишутся; вместо них композит читает маску (текстура того же размера).</summary>
        NoWrite = 2,
        /// <summary>Прежний путь: вторая цель MSAA-маски, перенос в резолве.</summary>
        MsaaTarget = 3,
    }
}
