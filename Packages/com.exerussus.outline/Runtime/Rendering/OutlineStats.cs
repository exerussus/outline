namespace Exerussus.Outline.Rendering
{
    /// <summary>
    /// Статистика последнего кадра подсветки. GPU-время — из ProfilingSampler (сумма за прошлый кадр
    /// по всем камерам, включая Scene View); на платформах без GPU-рекордера (WebGL) будет 0.
    /// </summary>
    public readonly struct OutlineStats
    {
        public readonly bool Rendered;
        public readonly int ActiveEntries;
        public readonly int DrawCalls;
        public readonly int JfaPasses;
        public readonly bool DualField;
        public readonly int FieldWidth;
        public readonly int FieldHeight;
        /// <summary>Фактический масштаб поля (с учётом авто-разрешения).</summary>
        public readonly float FieldScale;
        /// <summary>Доля поля, реально обсчитываемая после кропа (0..1).</summary>
        public readonly float Coverage;
        public readonly float MaskGpuMs;
        public readonly float JfaGpuMs;
        public readonly float CompositeGpuMs;
        public readonly float CpuMs;

        public OutlineStats(bool rendered, int activeEntries, int drawCalls, int jfaPasses, bool dualField,
            int fieldWidth, int fieldHeight, float fieldScale, float coverage, float maskGpuMs, float jfaGpuMs, float compositeGpuMs,
            float cpuMs)
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
            MaskGpuMs = maskGpuMs;
            JfaGpuMs = jfaGpuMs;
            CompositeGpuMs = compositeGpuMs;
            CpuMs = cpuMs;
        }

        public float TotalGpuMs => MaskGpuMs + JfaGpuMs + CompositeGpuMs;
    }
}
