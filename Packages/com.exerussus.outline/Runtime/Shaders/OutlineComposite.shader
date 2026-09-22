// Композит подсветки в цвет камеры. Выход premultiplied: Blend One OneMinusSrcAlpha,
// аддитивность стиля = занижение альфы (additive = 1 → чистое сложение).
// Слои пикселя: внутри своего силуэта — заливка, внутренний контур, rim (+ наложение свечения чужой
// группы с приоритетом ≥ на стыке); снаружи — внешний контур ближайшей записи, на стыке двух записей —
// мягкое смешение нескольких кандидатов.
Shader "Hidden/Exerussus/Outline/Composite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineFullscreenVert
            #pragma fragment Frag
            #include "OutlineCommon.hlsl"

            #define LAYER_OUTER 1u
            #define LAYER_INNER 2u
            #define LAYER_FILL  4u

            // «Поверх» для непремножённого слоя (цвет + альфа) в premultiplied-накопитель
            void Over(inout float4 acc, float3 color, float a)
            {
                acc.rgb = color * a + acc.rgb * (1.0 - a);
                acc.a = a + acc.a * (1.0 - a);
            }

            // «Поверх» для двух premultiplied-слоёв
            float4 OverPremul(float4 top, float4 bottom)
            {
                return float4(top.rgb + bottom.rgb * (1.0 - top.a), top.a + bottom.a * (1.0 - top.a));
            }

            float2 SeedToFrame(uint2 seedPos)
            {
                return float2(seedPos) + 0.5; // сиды хранятся в пикселях кадра
            }

            float HalfField()
            {
                return 0.5; // сид — центр граничного пикселя кадра, край на полпикселя дальше
            }

            // Внешний слой записи id на расстоянии edgeDist от края (premultiplied, до эффектов записи)
            float4 OuterLayer(uint id, float edgeDist, float2 p, float time)
            {
                float4 widths = OutlineData(id, OL_COL_WIDTHS);
                float4 outer = OutlineData(id, OL_COL_OUTER);
                if (widths.x <= 0.0 || outer.a <= 0.0)
                    return 0;

                float4 pulse = OutlineData(id, OL_COL_PULSE);
                float width = widths.x * (1.0 + pulse.z * sin(time * pulse.x * TWO_PI));
                float t = edgeDist / max(width, 1e-3);
                if (t >= 1.0)
                    return 0;
                float a = outer.a * OutlineLut(id * 2u, t) * OutlinePatternFor(id, LAYER_OUTER, p, time);
                return float4(outer.rgb * a, a);
            }

            // Слои внутри силуэта записи id (premultiplied, до эффектов записи)
            float4 InsideLayers(uint id, float rimValue, float edgeDist, float2 p, float time)
            {
                float4 acc = 0;
                float4 widths = OutlineData(id, OL_COL_WIDTHS);
                float4 fill = OutlineData(id, OL_COL_FILL);
                float4 inner = OutlineData(id, OL_COL_INNER);
                float4 rim = OutlineData(id, OL_COL_RIM);

                if (fill.a > 0.0)
                    Over(acc, fill.rgb, fill.a * OutlinePatternFor(id, LAYER_FILL, p, time));
                if (widths.y > 0.0 && inner.a > 0.0)
                {
                    float t = edgeDist / widths.y;
                    if (t < 1.0)
                        Over(acc, inner.rgb, inner.a * OutlineLut(id * 2u + 1u, t) * OutlinePatternFor(id, LAYER_INNER, p, time));
                }
                if (rim.a > 0.0)
                    Over(acc, rim.rgb, rim.a * pow(saturate(rimValue), widths.z));
                return acc;
            }

            // Эффекты записи: перекрытие, пульс, шум, fade, аддитивность. Возвращает слой, готовый к смешению.
            float4 EntryEffects(float4 acc, uint id, bool visible, bool inside, float2 p, float time)
            {
                if (acc.a <= 0.0)
                    return 0;

                float4 misc = OutlineData(id, OL_COL_MISC);
                if (!visible)
                {
                    uint mode = (uint)misc.w;
                    if (mode == 0u)
                        return 0;
                    if (mode == 2u)
                    {
                        float4 tint = OutlineData(id, OL_COL_OCC);
                        float4 occ2 = OutlineData(id, OL_COL_OCC2);
                        acc.rgb *= tint.rgb;
                        acc *= tint.a;
                        if (inside)
                            acc *= occ2.w;
                        if (occ2.y < 1.0)
                        {
                            float period = max(occ2.x, 1.0);
                            float ph = frac((p.x + p.y) / period + time * occ2.z);
                            acc *= OutlineStepAA(ph, occ2.y, 1.0 / period);
                        }
                    }
                }

                float4 pulse = OutlineData(id, OL_COL_PULSE);
                if (pulse.x > 0.0 && pulse.y > 0.0)
                    acc *= 1.0 - pulse.y * (0.5 + 0.5 * sin(time * pulse.x * TWO_PI));

                float4 noise = OutlineData(id, OL_COL_NOISE);
                if (noise.y > 0.0)
                    acc *= lerp(1.0, OutlineValueNoise(p / max(noise.x, 1.0) + time * noise.z), noise.y);

                acc *= misc.x;
                acc.a *= 1.0 - misc.y;
                return acc;
            }

            float4 OuterEntry(uint id, uint2 seedPos, float2 p, float time)
            {
                float2 seedFull = SeedToFrame(seedPos);
                float edgeDist = max(distance(p, seedFull) - HalfField(), 0.0);
                float4 layer = OuterLayer(id, edgeDist, p, time);
                // видимость берём у пикселя-сида: какая часть силуэта «излучает» этот контур
                bool visible = OutlineLoadMask(int2(seedFull)).b > 0.5;
                return EntryEffects(layer, id, visible, false, p, time);
            }

            float OuterScore(uint id, uint2 seedPos, float2 p)
            {
                return length(float2(seedPos) + 0.5 - p) * OutlineEntryInvW(id) - OutlineEntryPrio(id);
            }

            // Снаружи: центральный кандидат + (опционально) мягкое смешение с соседями по полю
            float4 OuterComposite(int2 pf, float2 p, float time)
            {
                uint2 pos0;
                uint id0;
                OutlineDecodeSeed(LOAD_TEXTURE2D(_OutlineSeeds, uint2(pf)), pos0, id0);
                if (id0 == 0u)
                    return 0;

                float blendPx = _OutlineParams.z;
                if (blendPx <= 0.0)
                    return OuterEntry(id0, pos0, p, time);

                int r = max(1, (int)round(blendPx * _OutlineSeedSize.z));
                int2 offs[4] = { int2(r, 0), int2(-r, 0), int2(0, r), int2(0, -r) };
                uint ids[5];
                uint2 poss[5];
                ids[0] = id0;
                poss[0] = pos0;
                uint count = 1u;

                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    int2 q = clamp(pf + offs[i], int2(_OutlineRect.xy), int2(_OutlineRect.zw) - 1);
                    uint2 pos;
                    uint id;
                    OutlineDecodeSeed(LOAD_TEXTURE2D(_OutlineSeeds, uint2(q)), pos, id);
                    bool fresh = id != 0u;
                    for (uint k = 0u; k < count; k++)
                        fresh = fresh && ids[k] != id;
                    if (fresh)
                    {
                        ids[count] = id;
                        poss[count] = pos;
                        count++;
                    }
                }

                if (count == 1u)
                    return OuterEntry(id0, pos0, p, time); // не стык — обычный путь

                float scores[5];
                float best = 1e20;
                uint bestIdx = 0u;
                for (uint j = 0u; j < count; j++)
                {
                    scores[j] = OuterScore(ids[j], poss[j], p);
                    if (scores[j] < best)
                    {
                        best = scores[j];
                        bestIdx = j;
                    }
                }

                // разница в «ширинах» лучшей записи, при которой вес падает вдвое
                float softness = max(blendPx * OutlineEntryInvW(ids[bestIdx]), 1e-4);
                float4 sum = 0;
                float wsum = 0;
                for (uint n = 0u; n < count; n++)
                {
                    float w = exp2(-(scores[n] - best) / softness);
                    sum += OuterEntry(ids[n], poss[n], p, time) * w;
                    wsum += w;
                }
                return sum / max(wsum, 1e-4);
            }

            float4 DebugView(uint mode, float4 m, uint selfId, uint seedId, float dist, float edgeDist)
            {
                if (mode == 1u)
                {
                    if (selfId == 0u)
                        return float4(0, 0, 0, 1);
                    return float4(OutlineIdColor(selfId) * lerp(0.35, 1.0, m.b) + m.g * 0.25, 1);
                }
                if (mode == 2u)
                {
                    if (seedId == 0u)
                        return float4(0, 0, 0, 1);
                    float band = 0.6 + 0.4 * step(0.5, frac(dist / 16.0));
                    return float4(OutlineIdColor(seedId) * band, 1);
                }
                // mode 3: поле расстояния до края; ярко — в пределах ширины записи, тускло — дальше
                if (seedId == 0u)
                    return float4(0, 0, 0, 1);
                uint owner = selfId != 0u ? selfId : seedId;
                float4 w = OutlineData(owner, OL_COL_WIDTHS);
                float limit = selfId != 0u ? w.y : w.x;
                float inRange = edgeDist < limit ? 1.0 : 0.2;
                float stripes = step(0.85, frac(edgeDist / 8.0));
                float3 baseCol = selfId != 0u ? float3(0.2, 0.5, 1.0) : float3(1.0, 0.55, 0.15);
                return float4((baseCol + stripes * 0.5) * inRange, 1);
            }

            float4 Frag(OutlineVaryings input) : SV_Target
            {
                float2 p = input.positionCS.xy;              // центр пикселя кадра
                float time = _OutlineParams.x;
                uint debugMode = (uint)_OutlineParams.w;

                int2 pf = min(int2(p * _OutlineSeedSize.z), int2(_OutlineSeedSize.xy) - 1);
                if (!OutlineInRect(pf))
                {
                    if (debugMode != 0u)
                        return float4(0, 0, 0, 1);
                    discard;
                }

                float4 m = OutlineLoadMask(int2(p));
                uint selfId = OutlineMaskId(m);
                bool hasInnerField = _OutlineParams2.y > 0.5;

                // внутреннее расстояние до края (для внутреннего контура)
                uint2 innerPos;
                uint innerId;
                if (hasInnerField)
                    OutlineDecodeSeed(LOAD_TEXTURE2D(_OutlineSeedsInner, uint2(pf)), innerPos, innerId);
                else
                    OutlineDecodeSeed(LOAD_TEXTURE2D(_OutlineSeeds, uint2(pf)), innerPos, innerId);

                float half_ = HalfField();
                float innerDist = innerId != 0u ? distance(p, SeedToFrame(innerPos)) : 1e6;
                float edgeDist;
                if (selfId == 0u)
                {
                    edgeDist = max(innerDist - half_, 0.0);
                }
                else
                {
                    // внутри своей группы край на полпикселя дальше сида; на стыке с чужой — ближе
                    bool sameGroup = innerId != 0u
                        && OutlineEntryGroup(innerId) == OutlineEntryGroup(selfId);
                    edgeDist = sameGroup ? innerDist + half_ : max(innerDist - half_, 0.0);
                }

                if (debugMode != 0u)
                {
                    uint2 op;
                    uint oid;
                    OutlineDecodeSeed(LOAD_TEXTURE2D(_OutlineSeeds, uint2(pf)), op, oid);
                    uint shownId = debugMode == 2u ? oid : innerId;
                    float shownDist = debugMode == 2u ? (oid != 0u ? distance(p, SeedToFrame(op)) : 0.0) : innerDist;
                    return DebugView(debugMode, m, selfId, shownId, shownDist, edgeDist);
                }

                if (selfId == 0u)
                    return OuterComposite(pf, p, time);

                // --- внутри силуэта ---
                float4 result = EntryEffects(InsideLayers(selfId, m.g, edgeDist, p, time),
                                             selfId, m.b > 0.5, true, p, time);

                // наложение свечения чужой группы на стыке
                if (_OutlineParams2.x > 0.5)
                {
                    uint2 op;
                    uint oid;
                    OutlineDecodeSeed(LOAD_TEXTURE2D(_OutlineSeeds, uint2(pf)), op, oid);
                    if (oid != 0u
                        && OutlineEntryGroup(oid) != OutlineEntryGroup(selfId)
                        && OutlineEntryPrio(oid) >= OutlineEntryPrio(selfId))
                    {
                        result = OverPremul(OuterEntry(oid, op, p, time), result);
                    }
                }
                return result;
            }
            ENDHLSL
        }
    }
}
