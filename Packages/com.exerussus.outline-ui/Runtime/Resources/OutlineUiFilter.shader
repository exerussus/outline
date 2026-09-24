// Проходы фильтра подсветки UI Toolkit (FilterFunctionDefinition собирается в OutlineUiFilter.cs):
// 0 — расстояние по строке до силуэта (внутри силуэта — исходный цвет); 1 — сборка: сначала расстояние по
// столбцу (точное евклидово, не дальше R = _OlStep.x), затем исходный контент + заливка/внутренний контур/паттерны/сканер/растворение внутри,
// свечение с эффектами снаружи. Выход premultiplied, как и вход.
Shader "Hidden/Exerussus/OutlineUi/Filter"
{
    SubShader
    {
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            Name "Horizontal"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OlVert
            #pragma fragment Frag
            #include "OutlineUiFilter.hlsl"

            // расстояние по строке до ближайшего пикселя силуэта (с поправкой на его покрытие)
            float4 Frag(OlVaryings i) : SV_Target
            {
                OlRect r = OlMakeRect(i, false);
                float2 p = OlPixel(i, r);
                float4 v = OlLoad(r, p);
                if (OlOccupied(v))
                    return v;
                int maxD = (int)min(_OlStep.x, 255.0);
                float best = 1e5;
                [loop]
                for (int s = 1; s <= maxD; s++)
                {
                    if (s - 0.5 >= best)
                        break;
                    float4 a = OlLoad(r, p + float2(s, 0));
                    if (OlOccupied(a))
                        best = min(best, s - (saturate(a.a) - 0.5));
                    float4 b = OlLoad(r, p - float2(s, 0));
                    if (OlOccupied(b))
                        best = min(best, s - (saturate(b.a) - 0.5));
                }
                return OlEncode(best, best <= _OlStep.x);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OlVert
            #pragma fragment Frag
            #include "OutlineUiFilter.hlsl"

            // евклидово расстояние до силуэта: минимум по столбцу из sqrt(dx² + dy²), dx — из прохода по строкам.
            // false — дальше R (или поле не считалось: стиль без свечения)
            bool VerticalDistance(OlRect r, float2 p, float4 v, out float best)
            {
                best = 1e5;
                float dx;
                if (OlDecode(v, dx))
                    best = dx;
                int maxD = (int)min(_OlStep.x, 255.0);
                [loop]
                for (int s = 1; s <= maxD; s++)
                {
                    if (s - 0.5 >= best)
                        break;
                    [unroll]
                    for (int k = 0; k < 2; k++)
                    {
                        float4 n = OlLoad(r, p + float2(0, k == 0 ? s : -s));
                        if (OlOccupied(n))
                            best = min(best, s - (saturate(n.a) - 0.5));
                        else if (OlDecode(n, dx))
                            best = min(best, sqrt(dx * dx + s * s));
                    }
                }
                return best <= _OlStep.x;
            }

            float AngleAround(float2 p, float2 center)
            {
                float2 d = p - center;
                return atan2(d.y, d.x) * (0.5 / UNITY_PI) + 0.5;
            }

            // свечение снаружи на расстоянии edgeDist от края (premultiplied, до общих эффектов)
            float4 OuterLayer(float edgeDist, float2 p, float2 center, float time)
            {
                if (_OlWidths.x <= 0.0 || _OlOuter.a <= 0.0)
                    return 0;
                float width = _OlWidths.x * (1.0 + _OlPulse.z * sin(time * _OlPulse.x * OL_TWO_PI));
                float alphaMul = 1.0;

                if (_OlElectric.x > 0.0 || _OlElectric.w > 0.0)
                {
                    float n = OlValueNoise(p / max(_OlElectric.y, 1.0) + float2(time * _OlElectric.z, -time * _OlElectric.z * 0.7));
                    edgeDist = max(edgeDist + (n - 0.5) * 2.0 * _OlElectric.x, 0.0);
                }

                if (_OlFire.x > 0.0)
                {
                    float2 up = float2(0.0, -1.0); // y прямоугольника идёт вниз
                    float n = OlFbm(p / max(_OlFire.y, 1.0) - up * time * _OlFire.z);
                    float h = saturate(0.5 + 0.5 * dot(normalize(p - center + 1e-4), up));
                    width *= 1.0 + _OlFire.x * n * (0.25 + 1.5 * h * h);
                    alphaMul *= lerp(1.0, saturate(n * 1.6), _OlFire.w);
                }

                float t = edgeDist / max(width, 1e-3);
                if (t >= 1.0)
                    return 0;

                float3 outer = OlStyleColor(_OlOuter.rgb);
                float a = _OlOuter.a * OlLut(0u, t).r * OlPatternFor(1u, p, time) * alphaMul;
                float3 col = outer;
                if (_OlGradient.x > 0.5)
                {
                    float4 g = OlLut(2u, t);
                    col = OlStyleColor(g.rgb);
                    a *= g.a;
                }
                float ang = 0.0;
                if (_OlGradient.y > 0.0 || _OlMarch.w > 0.0)
                    ang = AngleAround(p, center);
                if (_OlGradient.y > 0.0)
                {
                    float4 c2 = OlLut(3u, frac(ang + time * _OlGradient.z));
                    col = lerp(col, OlStyleColor(c2.rgb), _OlGradient.y);
                    a *= lerp(1.0, c2.a, _OlGradient.y);
                }
                if (_OlWave.w > 0.0)
                {
                    float period = max(_OlWave.x, 1.0);
                    float ph = frac(edgeDist / period - time * _OlWave.y);
                    a *= lerp(1.0, OlStepAA(abs(ph - 0.5) * 2.0, _OlWave.z, 2.0 / period), _OlWave.w);
                }
                if (_OlMarch.w > 0.0)
                {
                    float count = max(round(_OlMarch.x), 1.0);
                    float ph = frac(ang * count - time * _OlMarch.y * count);
                    a *= lerp(1.0, OlStepAA(ph, _OlMarch.z, 0.08), _OlMarch.w);
                }

                float4 res = float4(col * a, a);
                if (_OlElectric.w > 0.0)
                {
                    float rn = OlValueNoise(p / max(_OlElectric.y * 2.0, 1.0) + float2(-time * _OlElectric.z * 1.3, time * _OlElectric.z));
                    float arc = pow(saturate(1.0 - abs(rn * 2.0 - 1.0) * 6.0), 3.0);
                    res.rgb += outer * arc * _OlElectric.w * (1.0 - t) * _OlOuter.a;
                }
                if (_OlSparkle2.x > 0.0)
                {
                    float cell = max(_OlSparkle2.y, 2.0);
                    float2 q = (p - center) / cell;
                    float2 ci = floor(q);
                    float h = OlHash(ci + 0.37);
                    if (h < _OlSparkle2.x)
                    {
                        float2 pt = float2(OlHash(ci + 11.1), OlHash(ci + 23.7)) * 0.7 + 0.15;
                        float d = length(frac(q) - pt) * cell;
                        float tw = saturate(sin(time * _OlSparkle2.z * OL_TWO_PI + h * 97.0));
                        float spark = pow(saturate(1.0 - d / (cell * 0.22)), 2.0) * tw * tw;
                        res.rgb += OlStyleColor(_OlSparkle.rgb) * _OlSparkle.a * spark * (1.0 - t);
                    }
                }
                return res;
            }

            // направление k из 8 (оси, затем диагонали). Без локального static const массива: компилятор D3D11
            // обнулял его, и край внутрь не находился никогда
            float2 InsideDir(int k)
            {
                float2 axis = float2((k & 1) ? -1.0 : 1.0, 0.0);
                if (k >= 2 && k < 4)
                    axis = float2(0.0, (k & 1) ? -1.0 : 1.0);
                if (k >= 4)
                    axis = float2((k & 1) ? -0.7071 : 0.7071, k >= 6 ? -0.7071 : 0.7071);
                return axis;
            }

            // расстояние внутрь до края (px): поиск прозрачного пикселя по 8 направлениям, не дальше maxDist
            float InsideDistance(OlRect r, float2 p, float maxDist)
            {
                int steps = (int)ceil(maxDist);
                // дальше maxDist края нет — внутренний контур здесь не виден
                bool any0 = false;
                [unroll]
                for (int k = 0; k < 8; k++)
                    any0 = any0 || !OlOccupied(OlLoad(r, p + InsideDir(k) * steps));
                if (!any0)
                    return 1e5;
                [loop]
                for (int s = 1; s <= steps; s++)
                {
                    [unroll]
                    for (int k = 0; k < 8; k++)
                    {
                        if (!OlOccupied(OlLoad(r, p + InsideDir(k) * s)))
                            return s - 0.5;
                    }
                }
                return 1e5;
            }

            // слои внутри силуэта (premultiplied)
            float4 InsideLayers(OlRect r, float2 p, float2 center, float time)
            {
                float4 acc = 0;
                float2 q = p * _OlSpace.x;
                if (_OlFill.a > 0.0)
                {
                    float3 fc = OlStyleColor(_OlFill.rgb);
                    float fa = _OlFill.a;
                    if (_OlTex.y > 0.5 && _OlTex.w > 0.0)
                    {
                        float tile = max(_OlTex.z, 1e-4);
                        float lod = log2(max(256.0 * _OlSpace.x / tile, 1.0));
                        float4 tx = _OlFillTex.SampleLevel(sampler_LinearRepeat, q / tile, lod);
                        fc = lerp(fc, fc * OlStyleColor(tx.rgb), _OlTex.w);
                        fa *= lerp(1.0, tx.a, _OlTex.w);
                    }
                    OlOver(acc, fc, fa * OlPatternFor(4u, p, time));
                }
                if (_OlWidths.y > 0.0 && _OlInner.a > 0.0)
                {
                    float d = InsideDistance(r, p, _OlWidths.y + 1.0);
                    float t = d / _OlWidths.y;
                    if (t < 1.0)
                        OlOver(acc, OlStyleColor(_OlInner.rgb), _OlInner.a * OlLut(1u, t).r * OlPatternFor(2u, p, time));
                }
                if (_OlScan.a > 0.0)
                {
                    float coord = dot(p * _OlSpace.y, normalize(_OlScan2.xy + float2(0.0, 1e-5)));
                    float period = max(_OlScan2.z, 1e-4);
                    float d = abs(frac(coord / period - time * _OlScan3.z) - 0.5) * period;
                    float band = 1.0 - smoothstep(_OlScan3.x * 0.5, _OlScan3.x * 0.5 + max(_OlScan3.y, _OlSpace.y), d);
                    acc.rgb += OlStyleColor(_OlScan.rgb) * _OlScan.a * band;
                    acc.a = saturate(acc.a + _OlScan.a * band * 0.5);
                }
                return acc;
            }

            // общие эффекты подсветки: пульс, шум, fade, аддитивность
            float4 Effects(float4 acc, float2 p, float time)
            {
                if (_OlPulse.x > 0.0 && _OlPulse.y > 0.0)
                    acc *= 1.0 - _OlPulse.y * (0.5 + 0.5 * sin(time * _OlPulse.x * OL_TWO_PI));
                if (_OlNoise.y > 0.0)
                    acc *= lerp(1.0, OlValueNoise(p / max(_OlNoise.x, 1.0) + time * _OlNoise.z), _OlNoise.y);
                acc *= _OlWidths.z;
                acc.a *= 1.0 - _OlWidths.w;
                return acc;
            }

            float4 Frag(OlVaryings i) : SV_Target
            {
                OlRect r = OlMakeRect(i, false);
                float2 p = OlPixel(i, r);
                float2 center = r.size * 0.5;
                float time = _OlPx.y;
                float4 v = OlLoad(r, p);
                float4 result;

                if (OlOccupied(v))
                {
                    // внутри: исходный контент, поверх — слои подсветки; у частично покрытого края — свечение под ним
                    float4 layers = Effects(InsideLayers(r, p, center, time), p, time);
                    result = float4(layers.rgb + v.rgb * (1.0 - layers.a), layers.a + v.a * (1.0 - layers.a));
                    if (v.a < 0.999)
                    {
                        float4 glow = Effects(OuterLayer(0.0, p, center, time), p, time);
                        result += glow * (1.0 - result.a);
                    }
                }
                else
                {
                    // вертикальный проход поля прямо здесь: минимум по столбцу из √(dx² + dy²), dx — из прохода
                    // по строке (с поправкой на покрытие граничного пикселя)
                    float d;
                    if (!VerticalDistance(r, p, v, d))
                        return 0;
                    result = Effects(OuterLayer(max(d, 0.0), p, center, time), p, time);
                    v = 0; // для растворения: снаружи контента нет
                }

                // растворение режет всё — и контент, и подсветку; кромка светится
                if (_OlDissolve.x > 0.0)
                {
                    float n = OlFbm(p * _OlSpace.y / max(_OlDissolve.y, 1e-4));
                    float thr = _OlDissolve.x * 1.05;
                    float keep = smoothstep(thr - 0.02, thr + 0.02, n);
                    float edge = (_OlDissolve.z > 0.0 && _OlDissolve.x < 0.999)
                        ? (1.0 - smoothstep(thr, thr + max(_OlDissolve.z, 1e-3), n)) * keep : 0.0;
                    float srcA = v.a;
                    result *= keep;
                    float4 ec = float4(OlStyleColor(_OlDissolveEdge.rgb), _OlDissolveEdge.a);
                    result.rgb += ec.rgb * ec.a * edge * srcA;
                    result.a = saturate(result.a + ec.a * edge * srcA);
                }

                // выход — в пространстве цели
                if (_OlPx.z > 0.5 && _OlPx.w < 0.5)
                    result.rgb = OlGammaToLinear(result.rgb);
                else if (_OlPx.z < 0.5 && _OlPx.w > 0.5)
                    result.rgb = OlLinearToGamma(result.rgb);
                return result;
            }
            ENDHLSL
        }
    }
}
