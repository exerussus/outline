using UnityEngine;

namespace Exerussus.Outline.UI.Internal
{
    /// <summary>
    /// Стиль → векторы свойств шейдера сборки (единицы — пиксели цели фильтра). Stateless: пишет в переданный
    /// буфер. Смешивание двух наборов — для плавной смены стиля.
    /// </summary>
    internal static class StyleParams
    {
        public const int Outer = 0;
        public const int Inner = 1;
        public const int Fill = 2;
        public const int Widths = 3;
        public const int Pulse = 4;
        public const int Noise = 5;
        public const int Pattern = 6;
        public const int Pattern2 = 7;
        public const int Space = 8;
        public const int Gradient = 9;
        public const int Wave = 10;
        public const int March = 11;
        public const int Fire = 12;
        public const int Electric = 13;
        public const int Sparkle = 14;
        public const int Sparkle2 = 15;
        public const int Scan = 16;
        public const int Scan2 = 17;
        public const int Scan3 = 18;
        public const int Dissolve = 19;
        public const int DissolveEdge = 20;
        public const int Tex = 21;
        public const int Count = 22;

        public static void Write(OutlineUiStyle s, float ppp, float fade, float fxDissolve, Vector4[] o)
        {
            // все длины стиля — в пунктах; в шейдере — пиксели цели фильтра
            o[Outer] = C(s.outerColor);
            o[Inner] = C(s.innerColor);
            o[Fill] = C(s.fillColor);
            o[Widths] = new Vector4(s.outerWidth * ppp, s.innerWidth * ppp, fade, s.additive);
            o[Pulse] = new Vector4(s.pulseSpeed, s.pulseAlpha, s.pulseWidth, 0f);
            o[Noise] = new Vector4(s.noiseScale * ppp, s.noiseAmount, s.noiseSpeed, 0f);
            o[Pattern] = new Vector4((float)s.pattern, s.patternScale, s.patternAngle * Mathf.Deg2Rad, s.patternSpeed);
            o[Pattern2] = new Vector4(s.patternFill, s.patternStrength, (float)(int)s.patternLayers, s.patternSoftness);
            // x — пунктов на пиксель для паттерна, y — для сканера, растворения и текстуры заливки
            o[Space] = new Vector4(1f / ppp, 1f / ppp, 0f, 0f);

            o[Gradient] = new Vector4(s.useOuterGradient ? 1f : 0f, s.contourMix, s.contourSpeed, 1f);
            o[Wave] = new Vector4(s.wavePeriod * ppp, s.waveSpeed, s.waveDuty, s.waveStrength);
            o[March] = new Vector4(s.marchCount, s.marchSpeed, s.marchDuty, s.marchStrength);
            o[Fire] = new Vector4(s.fireAmount, s.fireScale * ppp, s.fireSpeed, s.fireFlicker);
            o[Electric] = new Vector4(s.electricWobble * ppp, s.electricScale * ppp, s.electricSpeed, s.electricArcs);
            o[Sparkle] = C(s.sparkleColor);
            o[Sparkle2] = new Vector4(s.sparkleDensity, s.sparkleSize * ppp, s.sparkleSpeed, 0f);
            o[Scan] = C(s.scanColor);
            // у прямоугольника фильтра y идёт вниз, направление в стиле — вверх по экрану
            var dir = new Vector2(s.scanDirection.x, -s.scanDirection.y);
            if (dir.sqrMagnitude < 1e-8f)
                dir = new Vector2(0f, -1f);
            dir.Normalize();
            o[Scan2] = new Vector4(dir.x, dir.y, s.scanPeriod, 0f);
            o[Scan3] = new Vector4(s.scanWidth, s.scanSoftness, s.scanSpeed, 0f);

            float dissolve = s.dissolve;
            if (s.dissolveByFade)
                dissolve = Mathf.Max(dissolve, 1f - fade);
            if (fxDissolve >= 0f)
                dissolve = Mathf.Max(dissolve, fxDissolve);
            o[Dissolve] = new Vector4(dissolve, s.dissolveScale, s.dissolveEdgeWidth, 0f);
            o[DissolveEdge] = C(s.dissolveEdgeColor);

            bool patternTex = s.pattern == OutlinePatternType.Texture && s.patternTexture != null;
            bool fillTex = s.fillTexture != null && s.fillTextureStrength > 0f;
            o[Tex] = new Vector4(patternTex ? 1f : 0f, fillTex ? 1f : 0f, s.fillTextureTiling, s.fillTextureStrength);
        }

        /// <summary>b = lerp(a, b, t); дискретное (тип паттерна, слои, текстуры) — по середине перехода.</summary>
        public static void Blend(Vector4[] a, Vector4[] b, float t)
        {
            bool target = t >= 0.5f;
            var pa = a[Pattern];
            var pb = b[Pattern];
            var pa2 = a[Pattern2];
            var pb2 = b[Pattern2];
            bool samePattern = pa.x == pb.x && pa2.z == pb2.z && a[Space].x == b[Space].x;
            var texA = a[Tex];
            var texB = b[Tex];
            // строка «градиент по ширине» у стиля без градиента запечена его цветом свечения (OutlineUiLuts) —
            // на время перехода градиент включён, если он есть хоть у одного
            float gradientOn = Mathf.Max(a[Gradient].x, b[Gradient].x);
            for (int i = 0; i < Count; i++)
                b[i] = Vector4.LerpUnclamped(a[i], b[i], t);

            if (!samePattern)
            {
                // паттерн другого вида: прежний гаснет к середине, новый разгорается после
                b[Pattern] = target ? pb : pa;
                var p2 = target ? pb2 : pa2;
                p2.y *= Mathf.Abs(2f * t - 1f);
                b[Pattern2] = p2;
            }
            var g = b[Gradient];
            g.x = gradientOn;
            b[Gradient] = g;
            var tex = b[Tex];
            tex.x = target ? texB.x : texA.x;
            tex.y = target ? texB.y : texA.y;
            b[Tex] = tex;
        }

        // цвет стиля как задан (гамма); в линейное пространство переводит шейдер по флагам цели
        private static Vector4 C(Color c) => new(c.r, c.g, c.b, c.a);
    }
}
