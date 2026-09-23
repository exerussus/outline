using UnityEngine;

namespace Exerussus.Outline.Rendering
{
    /// <summary>
    /// Выбор масштаба поля по бюджету (stateless). Стоимость = пиксели поля в области работы × число
    /// проходов (MRT-проход двойного поля считается за два). Одинаково работает на всех платформах,
    /// не зависит от GPU-таймеров. Раскладка проходов совпадает с OutlinePass.
    /// </summary>
    public static class OutlineQuality
    {
        /// <summary>Допустимые масштабы поля: ступени, чтобы пул RT не перевыделялся каждый кадр.</summary>
        public const int StepCount = 5;

        public static float Step(int index)
        {
            switch (index)
            {
                case 0: return 1f;
                case 1: return 0.75f;
                case 2: return 0.5f;
                case 3: return 0.375f;
                default: return 0.25f;
            }
        }

        /// <summary>Начальный шаг JFA для дальности rangePx при масштабе scale (как в OutlinePass).</summary>
        public static int StartStep(float rangePx, float scale)
        {
            int step = Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.CeilToInt(rangePx * scale)));
            return Mathf.Max(1, step / 2);
        }

        /// <summary>С какого шага включается двойное поле (внутренний контур).</summary>
        public static int InnerStartStep(float innerPx, float scale) =>
            Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.CeilToInt(innerPx * scale)));

        /// <summary>
        /// Число шагов JFA и сколько из них двойные. Init не входит.
        /// </summary>
        public static int StepPasses(float rangePx, float innerPx, float scale, bool dual, bool extra, out int dualPasses)
        {
            int start = StartStep(rangePx, scale);
            int innerStart = dual ? InnerStartStep(innerPx, scale) : 0;
            int passes = 0;
            dualPasses = 0;
            for (int s = start; s >= 1; s >>= 1)
            {
                passes++;
                if (dual && s <= innerStart)
                    dualPasses++;
            }
            if (extra)
            {
                passes++;
                if (dual)
                    dualPasses++;
            }
            return passes;
        }

        /// <summary>Стоимость поля: пиксели поля в области × (init + шаги), двойные проходы ×2.</summary>
        public static long Cost(float areaPx, float rangePx, float innerPx, float scale, bool dual, bool extra)
        {
            int steps = StepPasses(rangePx, innerPx, scale, dual, extra, out int dualPasses);
            int weighted = (dual ? 2 : 1) + steps + dualPasses;
            double fieldPixels = (double)areaPx * scale * scale;
            return (long)(fieldPixels * weighted);
        }

        /// <summary>
        /// Наибольший масштаб из ступеней в [minScale, maxScale], укладывающийся в бюджет;
        /// если ни один не укладывается — наименьший допустимый.
        /// </summary>
        public static float Choose(float areaPx, float rangePx, float innerPx, bool dual, bool extra,
            float maxScale, float minScale, long budget, out long cost)
        {
            float chosen = -1f;
            cost = 0;
            for (int i = 0; i < StepCount; i++)
            {
                float s = Step(i);
                if (s > maxScale + 1e-4f)
                    continue;
                if (s < minScale - 1e-4f)
                    break;
                chosen = s;
                cost = Cost(areaPx, rangePx, innerPx, s, dual, extra);
                if (cost <= budget)
                    return s;
            }

            if (chosen < 0f)
            {
                // границы не пересекаются со ступенями — берём maxScale как есть
                chosen = Mathf.Clamp(maxScale, 0.05f, 1f);
                cost = Cost(areaPx, rangePx, innerPx, chosen, dual, extra);
            }
            return chosen;
        }
    }
}
