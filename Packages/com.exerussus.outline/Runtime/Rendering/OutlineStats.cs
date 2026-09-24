namespace Exerussus.Outline.Rendering
{
    /// <summary>Статистика последнего кадра подсветки игровой камеры.</summary>
    public readonly struct OutlineStats
    {
        public readonly bool Rendered;
        public readonly int ActiveEntries;
        public readonly int DrawCalls;
        /// <summary>Проходы поля: init + шаги JFA.</summary>
        public readonly int JfaPasses;
        public readonly bool DualField;
        public readonly int FieldWidth;
        public readonly int FieldHeight;
        /// <summary>Фактический масштаб поля.</summary>
        public readonly float FieldScale;
        /// <summary>Доля поля, реально обсчитываемая после кропа (0..1).</summary>
        public readonly float Coverage;
        /// <summary>Стоимость поля: пиксели поля в области × проходы (двойной проход — ×2).</summary>
        public readonly long FieldCost;
        /// <summary>Бюджет стоимости поля из настроек.</summary>
        public readonly long FieldBudget;
        /// <summary>Сэмплов маски (1 — без сглаживания края).</summary>
        public readonly int EdgeSamples;
        /// <summary>Отрисованных слоёв (у каждого своё поле; размер и масштаб выше — первого).</summary>
        public readonly int Layers;
        /// <summary>Время записи Render Graph на CPU, мс.</summary>
        public readonly float CpuMs;

        public OutlineStats(bool rendered, int activeEntries, int drawCalls, int jfaPasses, bool dualField,
            int fieldWidth, int fieldHeight, float fieldScale, float coverage, long fieldCost, long fieldBudget, int edgeSamples, float cpuMs, int layers = 1)
        {
            Rendered = rendered;
            ActiveEntries = activeEntries;
            DrawCalls = drawCalls;
            JfaPasses = jfaPasses;
            DualField = dualField;
            FieldWidth = fieldWidth;
            FieldHeight = fieldHeight;
            FieldScale = fieldScale;
            Coverage = coverage;
            FieldCost = fieldCost;
            FieldBudget = fieldBudget;
            EdgeSamples = edgeSamples;
            CpuMs = cpuMs;
            Layers = layers;
        }
    }
}
