using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.UI.Internal
{
    /// <summary>
    /// Определения фильтра подсветки (по одному на число шагов JFA) и заполнение свойств проходов из стиля слота.
    /// Проходы: сиды → шаги JFA (step = 2^(N-1) … 1, плюс ещё один шаг 1) → сборка.
    /// </summary>
    internal static class OutlineUiFilter
    {
        private const string ShaderResource = "OutlineUiFilter";
        private const int PassSeed = 0;
        private const int PassStep = 1;
        private const int PassComposite = 2;

        // корзины по дальности поля в пикселях: шагов JFA — 3 (≤8 px), 5 (≤32), 7 (≤128), 9 (≤512)
        private static readonly int[] s_BucketSteps = { 3, 5, 7, 9 };
        private static readonly FilterFunctionDefinition[] s_Definitions = new FilterFunctionDefinition[4];
        private static Material s_Material;

        private static readonly int IdPx = Shader.PropertyToID("_OlPx");
        private static readonly int IdStep = Shader.PropertyToID("_OlStep");
        private static readonly int IdOuter = Shader.PropertyToID("_OlOuter");
        private static readonly int IdInner = Shader.PropertyToID("_OlInner");
        private static readonly int IdFill = Shader.PropertyToID("_OlFill");
        private static readonly int IdWidths = Shader.PropertyToID("_OlWidths");
        private static readonly int IdPulse = Shader.PropertyToID("_OlPulse");
        private static readonly int IdNoise = Shader.PropertyToID("_OlNoise");
        private static readonly int IdPattern = Shader.PropertyToID("_OlPattern");
        private static readonly int IdPattern2 = Shader.PropertyToID("_OlPattern2");
        private static readonly int IdSpace = Shader.PropertyToID("_OlSpace");
        private static readonly int IdGradient = Shader.PropertyToID("_OlGradient");
        private static readonly int IdWave = Shader.PropertyToID("_OlWave");
        private static readonly int IdMarch = Shader.PropertyToID("_OlMarch");
        private static readonly int IdFire = Shader.PropertyToID("_OlFire");
        private static readonly int IdElectric = Shader.PropertyToID("_OlElectric");
        private static readonly int IdSparkle = Shader.PropertyToID("_OlSparkle");
        private static readonly int IdSparkle2 = Shader.PropertyToID("_OlSparkle2");
        private static readonly int IdScan = Shader.PropertyToID("_OlScan");
        private static readonly int IdScan2 = Shader.PropertyToID("_OlScan2");
        private static readonly int IdScan3 = Shader.PropertyToID("_OlScan3");
        private static readonly int IdDissolve = Shader.PropertyToID("_OlDissolve");
        private static readonly int IdDissolveEdge = Shader.PropertyToID("_OlDissolveEdge");
        private static readonly int IdTex = Shader.PropertyToID("_OlTex");
        private static readonly int IdLut = Shader.PropertyToID("_OlLut");
        private static readonly int IdLutPrev = Shader.PropertyToID("_OlLutPrev");
        private static readonly int IdPatternTex = Shader.PropertyToID("_OlPatternTex");
        private static readonly int IdFillTex = Shader.PropertyToID("_OlFillTex");

        // параметры стиля, по одному вектору на свойство шейдера (порядок — StyleParams.*)
        private static readonly Vector4[] s_A = new Vector4[StyleParams.Count];
        private static readonly Vector4[] s_B = new Vector4[StyleParams.Count];

        // ------------------------------------------------------------------ Определения

        public static FilterFunctionDefinition GetDefinition(int bucket)
        {
            bucket = Mathf.Clamp(bucket, 0, s_Definitions.Length - 1);
            var def = s_Definitions[bucket];
            if (def != null)
                return def;

            var material = GetMaterial();
            int steps = s_BucketSteps[bucket];
            def = ScriptableObject.CreateInstance<FilterFunctionDefinition>();
            def.hideFlags = HideFlags.HideAndDontSave;
            def.name = $"OutlineUi JFA{steps}";
            def.filterName = "outline-ui";
            def.parameters = new[]
            {
                new FilterParameterDeclaration { name = "slot", interpolationDefaultValue = new FilterParameter(0f) },
                new FilterParameterDeclaration { name = "reach", interpolationDefaultValue = new FilterParameter(0f) },
            };
            var passes = new PostProcessingPass[steps + 3];
            passes[0] = new PostProcessingPass
            {
                material = material,
                passIndex = PassSeed,
                applySettingsCallback = Apply,
                // поле растёт наружу на дальность свечения — цель прохода сидов шире элемента
                computeRequiredWriteMarginsCallback = ReachMargins,
            };
            for (int i = 1; i <= steps + 1; i++)
                passes[i] = new PostProcessingPass { material = material, passIndex = PassStep, applySettingsCallback = Apply };
            passes[steps + 2] = new PostProcessingPass { material = material, passIndex = PassComposite, applySettingsCallback = Apply };
            def.passes = passes;
            s_Definitions[bucket] = def;
            return def;
        }

        private static Material GetMaterial()
        {
            if (s_Material != null)
                return s_Material;
            var shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null)
            {
                Debug.LogError("[OutlineUi] Не найден шейдер Resources/OutlineUiFilter.");
                return null;
            }
            s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "OutlineUi Filter" };
            return s_Material;
        }

        private static PostProcessingMargins ReachMargins(FilterFunction func)
        {
            float r = func.parameterCount > 1 ? Mathf.Max(0f, func.GetParameter(1).floatValue) : 0f;
            return new PostProcessingMargins { left = r, top = r, right = r, bottom = r };
        }

        /// <summary>Дальность свечения, пункты: сколько места нужно вокруг элемента.</summary>
        public static float ReachPoints(OutlineStyle style, OutlineStyle prev)
        {
            float r = Reach(style);
            if (prev != null)
                r = Mathf.Max(r, Reach(prev));
            return Mathf.Ceil(r + 2f);
        }

        private static float Reach(OutlineStyle s)
        {
            if (s == null)
                return 0f;
            float outer = s.outerColor.a > 0f ? s.outerWidth * (1f + s.pulseWidth) * (1f + s.fireAmount) + s.electricWobble : 0f;
            return Mathf.Max(outer, 0f);
        }

        /// <summary>Набор шагов JFA под дальность (пункты → пиксели по масштабу панели элемента).</summary>
        public static int BucketFor(float reachPoints, VisualElement element)
        {
            float ppp = element != null && element.panel != null ? Mathf.Max(element.scaledPixelsPerPoint, 1f) : 2f;
            // + внутренний запас: элемент может оказаться на панели с большим масштабом позже
            float px = reachPoints * ppp * 1.25f;
            for (int b = 0; b < s_BucketSteps.Length; b++)
                if (px <= (1 << s_BucketSteps[b]))
                    return b;
            return s_BucketSteps.Length - 1;
        }

        public static bool IsAnimated(OutlineStyle s)
        {
            if (s == null)
                return false;
            return (s.pulseSpeed > 0f && (s.pulseAlpha > 0f || s.pulseWidth > 0f))
                || (s.pattern != OutlinePatternType.None && s.patternSpeed != 0f)
                || (s.noiseAmount > 0f && s.noiseSpeed != 0f)
                || (s.contourMix > 0f && s.contourSpeed != 0f)
                || (s.waveStrength > 0f && s.waveSpeed != 0f)
                || (s.marchStrength > 0f && s.marchSpeed != 0f)
                || s.fireAmount > 0f || s.electricWobble > 0f || s.electricArcs > 0f
                || s.sparkleDensity > 0f
                || (s.scanColor.a > 0f && s.scanSpeed != 0f);
        }

        // ------------------------------------------------------------------ Свойства прохода

        private static int StepsOf(FilterFunctionDefinition def)
        {
            for (int b = 0; b < s_Definitions.Length; b++)
                if (ReferenceEquals(s_Definitions[b], def))
                    return s_BucketSteps[b];
            return s_BucketSteps[s_BucketSteps.Length - 1];
        }

        private static void Apply(MaterialPropertyBlock mpb, FilterPassContext ctx)
        {
            var func = ctx.filterFunction;
            int slot = func.parameterCount > 0 ? Mathf.RoundToInt(func.GetParameter(0).floatValue) : 0;
            float ppp = Mathf.Max(ctx.scaledPixelsPerPoint, 1e-3f);
            float now = OutlineClock.Now;
            mpb.SetVector(IdPx, new Vector4(ppp, now % 3600f, ctx.readsGamma ? 1f : 0f, ctx.writesGamma ? 1f : 0f));

            int steps = StepsOf(func.customDefinition);
            int pass = ctx.filterPassIndex;
            if (pass == 0)
                return;
            if (pass <= steps + 1)
            {
                int k = pass; // 1..steps — шаги 2^(steps-k), последний — ещё один шаг 1
                float step = k <= steps ? (1 << (steps - k)) : 1f;
                mpb.SetVector(IdStep, new Vector4(step, 0f, 0f, 0f));
                return;
            }

            // сборка: параметры стиля слота (с плавной сменой стиля, fade и растворением эффекта)
            if (!OutlineUi.IsSlotAlive(slot))
            {
                FillEmpty(mpb);
                return;
            }
            var style = OutlineUi.GetStyle(slot);
            float fade = OutlineUi.EvaluateFade(slot, now);
            float blend = OutlineUi.EvaluateStyleBlend(slot, now, out var prev);
            float fxDissolve = OutlineUi.EvaluateDissolve(slot, now);

            StyleParams.Write(style, ppp, fade, fxDissolve, s_B);
            var lut = OutlineUiLuts.Get(style);
            var lutPrev = lut;
            if (prev != null && blend < 1f)
            {
                StyleParams.Write(prev, ppp, fade, fxDissolve, s_A);
                StyleParams.Blend(s_A, s_B, blend);
                lutPrev = OutlineUiLuts.Get(prev);
            }
            else
            {
                blend = 1f;
            }
            var gradient = s_B[StyleParams.Gradient];
            gradient.w = blend;
            s_B[StyleParams.Gradient] = gradient;

            mpb.SetVector(IdOuter, s_B[StyleParams.Outer]);
            mpb.SetVector(IdInner, s_B[StyleParams.Inner]);
            mpb.SetVector(IdFill, s_B[StyleParams.Fill]);
            mpb.SetVector(IdWidths, s_B[StyleParams.Widths]);
            mpb.SetVector(IdPulse, s_B[StyleParams.Pulse]);
            mpb.SetVector(IdNoise, s_B[StyleParams.Noise]);
            mpb.SetVector(IdPattern, s_B[StyleParams.Pattern]);
            mpb.SetVector(IdPattern2, s_B[StyleParams.Pattern2]);
            mpb.SetVector(IdSpace, s_B[StyleParams.Space]);
            mpb.SetVector(IdGradient, s_B[StyleParams.Gradient]);
            mpb.SetVector(IdWave, s_B[StyleParams.Wave]);
            mpb.SetVector(IdMarch, s_B[StyleParams.March]);
            mpb.SetVector(IdFire, s_B[StyleParams.Fire]);
            mpb.SetVector(IdElectric, s_B[StyleParams.Electric]);
            mpb.SetVector(IdSparkle, s_B[StyleParams.Sparkle]);
            mpb.SetVector(IdSparkle2, s_B[StyleParams.Sparkle2]);
            mpb.SetVector(IdScan, s_B[StyleParams.Scan]);
            mpb.SetVector(IdScan2, s_B[StyleParams.Scan2]);
            mpb.SetVector(IdScan3, s_B[StyleParams.Scan3]);
            mpb.SetVector(IdDissolve, s_B[StyleParams.Dissolve]);
            mpb.SetVector(IdDissolveEdge, s_B[StyleParams.DissolveEdge]);
            mpb.SetVector(IdTex, s_B[StyleParams.Tex]);
            mpb.SetTexture(IdLut, lut);
            mpb.SetTexture(IdLutPrev, lutPrev);
            var texStyle = blend >= 0.5f || prev == null ? style : prev;
            mpb.SetTexture(IdPatternTex, texStyle.patternTexture != null ? texStyle.patternTexture : Texture2D.whiteTexture);
            mpb.SetTexture(IdFillTex, texStyle.fillTexture != null ? texStyle.fillTexture : Texture2D.whiteTexture);
        }

        // слот уже снят (кадр, записанный до снятия фильтра): контент без подсветки
        private static void FillEmpty(MaterialPropertyBlock mpb)
        {
            mpb.SetVector(IdOuter, Vector4.zero);
            mpb.SetVector(IdInner, Vector4.zero);
            mpb.SetVector(IdFill, Vector4.zero);
            mpb.SetVector(IdWidths, Vector4.zero);
            mpb.SetVector(IdPulse, Vector4.zero);
            mpb.SetVector(IdNoise, Vector4.zero);
            mpb.SetVector(IdPattern, Vector4.zero);
            mpb.SetVector(IdPattern2, Vector4.zero);
            mpb.SetVector(IdSpace, new Vector4(1f, 0.01f, 0f, 0f));
            mpb.SetVector(IdGradient, new Vector4(0f, 0f, 0f, 1f));
            mpb.SetVector(IdWave, Vector4.zero);
            mpb.SetVector(IdMarch, Vector4.zero);
            mpb.SetVector(IdFire, Vector4.zero);
            mpb.SetVector(IdElectric, Vector4.zero);
            mpb.SetVector(IdSparkle, Vector4.zero);
            mpb.SetVector(IdSparkle2, Vector4.zero);
            mpb.SetVector(IdScan, Vector4.zero);
            mpb.SetVector(IdScan2, Vector4.zero);
            mpb.SetVector(IdScan3, Vector4.zero);
            mpb.SetVector(IdDissolve, Vector4.zero);
            mpb.SetVector(IdDissolveEdge, Vector4.zero);
            mpb.SetVector(IdTex, Vector4.zero);
            mpb.SetTexture(IdLut, Texture2D.whiteTexture);
            mpb.SetTexture(IdLutPrev, Texture2D.whiteTexture);
            mpb.SetTexture(IdPatternTex, Texture2D.whiteTexture);
            mpb.SetTexture(IdFillTex, Texture2D.whiteTexture);
        }
    }
}
