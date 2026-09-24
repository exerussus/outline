// Композит подсветки в цвет камеры. Выход premultiplied: Blend One OneMinusSrcAlpha,
// аддитивность стиля = занижение альфы (additive = 1 → чистое сложение).
// Слои пикселя: внутри своего силуэта — заливка, внутренний контур, rim (+ наложение свечения чужой
// группы на стыке: с приоритетом ≥ своего — целиком, ниже — гаснет вглубь силуэта на радиусе стыка); снаружи — внешний контур ближайшей записи, на стыке двух записей —
// мягкое смешение нескольких кандидатов.
// Край субпиксельный: покрытие граничного пикселя (a маски, доля сэмплов MSAA) сдвигает край относительно
// центра сида на (a - 0.5) px; частично покрытый пиксель силуэта смешивает внутренние слои со свечением.
// При поле ниже ×1 сиды редкие (один на блок k×k кадра), и у самого края расстояние до ближайшего сида
// завышено до ~k px — тонкие контуры рассыпаются в точки. Поэтому вблизи края расстояние уточняется:
// берутся сиды той же записи из соседних пикселей поля 3×3, и край считается ломаной через два ближайших.
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

            TEXTURE2D(_OutlineBg);        // копия цвета камеры (объекты в режиме прозрачности в ней не нарисованы)
            TEXTURE2D(_OutlineObjColor);  // цвет скрытых объектов их материалами (a — есть объект)

            // Доля растворения записи: из стиля/эффекта или (по fade) 1 - fade
            float DissolveAmount(uint id)
            {
                float4 dis = OutlineData(id, OL_COL_DISSOLVE);
                float amount = dis.x;
                if (dis.w > 0.5)
                    amount = max(amount, 1.0 - OutlineData(id, OL_COL_MISC).x);
                return amount;
            }

            // Растворение в точке p: keep — сколько осталось (0..1), edge — светящаяся кромка
            void DissolveTerms(uint id, float2 q, float amount, out float keep, out float edge)
            {
                float4 dis = OutlineData(id, OL_COL_DISSOLVE);
                float n = OutlineFbm(q / max(dis.y, 1e-4));
                // при amount = 1 порог выше максимума шума — растворено целиком
                float thr = amount * 1.05;
                keep = smoothstep(thr - 0.02, thr + 0.02, n);
                edge = (dis.z > 0.0 && amount < 0.999)
                    ? (1.0 - smoothstep(thr, thr + max(dis.z, 1e-3), n)) * keep
                    : 0.0;
            }

            // Основа пикселя записи в режиме прозрачности/маскировки: фон за объектом с искажением и
            // преломлением у края, поверх — сам объект с непрозрачностью, тинт, мерцание края.
            // edgeDist — расстояние до края внутрь, edgeSeed — ближайший граничный пиксель (px кадра).
            float4 SeeThroughBase(uint id, float2 p, float edgeDist, float2 edgeSeed, bool hasEdge, float time)
            {
                float4 st = OutlineData(id, OL_COL_SEETHRU);
                float4 st2 = OutlineData(id, OL_COL_SEETHRU2);
                float fade = OutlineData(id, OL_COL_MISC).x;

                float2 offset = 0;
                if (st.z > 0.0)
                {
                    float2 q = p / max(st.w, 1.0);
                    float t = time * st2.x;
                    float2 n = float2(OutlineValueNoise(q + float2(t, 0.37 * t)),
                                      OutlineValueNoise(q + float2(17.3 - 0.6 * t, 5.1 + t)));
                    offset += (n - 0.5) * 2.0 * st.z;
                }
                float edgeK = hasEdge ? 1.0 - saturate(edgeDist / max(st2.z, 1.0)) : 0.0;
                if (st2.y > 0.0 && hasEdge)
                {
                    // линза: у края фон берётся из-за силуэта — выборка смещается наружу
                    float2 inward = normalize(p - edgeSeed + 1e-4);
                    offset -= inward * st2.y * edgeK * edgeK;
                }
                offset *= fade;

                float2 uv = clamp((p + offset) * _OutlineMaskSize.zw, 0.0, 1.0);
                float3 col = SAMPLE_TEXTURE2D_LOD(_OutlineBg, sampler_LinearClamp, uv, 0).rgb;

                float4 tint = OutlineData(id, OL_COL_SEETINT);
                col = lerp(col, col * tint.rgb, tint.a * fade);

                // сам объект: при fade → 0 возвращается к полной непрозрачности (плавный вход и выход)
                float opacity = lerp(1.0, st.y, fade);

                // растворение режет сам объект: на растворённых участках — фон, по кромке — свечение
                float keep = 1.0;
                float edge = 0.0;
                float dissolve = DissolveAmount(id);
                if (dissolve > 0.0)
                {
                    float2 q;
                    float pxSize;
                    float3 pos;
                    bool hasPos;
                    OutlineSpaceCoords(id, p, true, q, pxSize, pos, hasPos);
                    DissolveTerms(id, q, dissolve, keep, edge);
                }

                float4 obj = LOAD_TEXTURE2D(_OutlineObjColor, uint2(p));
                if (obj.a > 0.0)
                    col = lerp(col, obj.rgb, saturate(opacity) * keep);
                float4 ec = OutlineData(id, OL_COL_DISSOLVE_EDGE);
                col += ec.rgb * ec.a * edge;

                float4 sh = OutlineData(id, OL_COL_SHIMMER);
                if (sh.a > 0.0)
                {
                    float flick = 0.55 + 0.45 * OutlineValueNoise(p * 0.08 + time * 2.3);
                    col += sh.rgb * sh.a * edgeK * edgeK * flick * fade;
                }
                return float4(col, 1.0);
            }

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
                return 0.5; // наибольший сдвиг края от центра сида (пиксель покрыт целиком)
            }

            // Сдвиг края от центра сида наружу, px: покрытие 1 → +0.5, покрытие 0.25 → -0.25.
            // Без сглаживания покрытие всегда 1 — край на полпикселя дальше центра, как раньше.
            float EdgeOffset(float4 seedMask)
            {
                return seedMask.a - 0.5;
            }

            // Доля оборота вокруг якоря записи (центр объекта на экране), 0…1
            float AngleAround(uint id, float2 p)
            {
                float2 d = p - OutlineData(id, OL_COL_PATSPACE).yz;
                return atan2(d.y, d.x) * (0.5 / PI) + 0.5;
            }

            // Внешний слой записи id на расстоянии edgeDist от края (premultiplied, до эффектов записи):
            // кривая, градиенты, паттерн, волны, бег по контуру, огонь, электричество, искры
            float4 OuterLayer(uint id, float edgeDist, float2 p, float time)
            {
                float4 widths = OutlineData(id, OL_COL_WIDTHS);
                float4 outer = OutlineData(id, OL_COL_OUTER);
                if (widths.x <= 0.0 || outer.a <= 0.0)
                    return 0;

                float4 pulse = OutlineData(id, OL_COL_PULSE);
                float width = widths.x * (1.0 + pulse.z * sin(time * pulse.x * TWO_PI));
                float alphaMul = 1.0;

                // электричество: край дрожит шумом
                float4 elec = OutlineData(id, OL_COL_ELECTRIC);
                float elecN = 0.5;
                if (elec.x > 0.0 || elec.w > 0.0)
                {
                    elecN = OutlineValueNoise(p / max(elec.y, 1.0) + float2(time * elec.z, -time * elec.z * 0.7));
                    edgeDist = max(edgeDist + (elecN - 0.5) * 2.0 * elec.x, 0.0);
                }

                // огонь: шум уносится вверх по экрану, свечение удлиняется, сильнее над объектом
                float4 fire = OutlineData(id, OL_COL_FIRE);
                if (fire.x > 0.0)
                {
                    float2 up = float2(0.0, 1.0);
                    float n = OutlineFbm(p / max(fire.y, 1.0) - up * time * fire.z);
                    float2 dir = p - OutlineData(id, OL_COL_PATSPACE).yz;
                    float h = saturate(0.5 + 0.5 * dot(normalize(dir + 1e-4), up));
                    width *= 1.0 + fire.x * n * (0.25 + 1.5 * h * h);
                    alphaMul *= lerp(1.0, saturate(n * 1.6), fire.w);
                }

                float t = edgeDist / max(width, 1e-3);
                if (t >= 1.0)
                    return 0;

                float a = outer.a * OutlineLut(id * 2u, t) * OutlinePatternFor(id, LAYER_OUTER, p, time, false) * alphaMul;
                float3 col = outer.rgb;

                // цвет по ширине свечения
                float4 grad = OutlineData(id, OL_COL_GRADIENT);
                if (grad.x > 0.5)
                {
                    float4 g = OutlineRamp(id * 2u, t);
                    col = g.rgb;
                    a *= g.a;
                }
                // цвет по углу вокруг объекта
                float ang = 0.0;
                float4 march = OutlineData(id, OL_COL_MARCH);
                if (grad.y > 0.0 || march.w > 0.0)
                    ang = AngleAround(id, p);
                if (grad.y > 0.0)
                {
                    float4 c2 = OutlineRamp(id * 2u + 1u, frac(ang + time * grad.z));
                    col = lerp(col, c2.rgb, grad.y);
                    a *= lerp(1.0, c2.a, grad.y);
                }

                // волны: кольца, бегущие от силуэта
                float4 wave = OutlineData(id, OL_COL_WAVE);
                if (wave.w > 0.0)
                {
                    float period = max(wave.x, 1.0);
                    float ph = frac(edgeDist / period - time * wave.y);
                    float ring = OutlineStepAA(abs(ph - 0.5) * 2.0, wave.z, 2.0 / period);
                    a *= lerp(1.0, ring, wave.w);
                }

                // бег по контуру: штрихи по углу вокруг объекта
                if (march.w > 0.0)
                {
                    float count = max(round(march.x), 1.0);
                    float ph = frac(ang * count - time * march.y * count);
                    float dash = OutlineStepAA(ph, march.z, 0.08);
                    a *= lerp(1.0, dash, march.w);
                }

                float4 res = float4(col * a, a);

                // разряды: тонкие гребни шума внутри свечения
                if (elec.w > 0.0)
                {
                    float r = OutlineValueNoise(p / max(elec.y * 2.0, 1.0) + float2(-time * elec.z * 1.3, time * elec.z));
                    float arc = pow(saturate(1.0 - abs(r * 2.0 - 1.0) * 6.0), 3.0);
                    res.rgb += outer.rgb * arc * elec.w * (1.0 - t) * outer.a;
                }

                // искры: случайные мерцающие точки в свечении (свет, альфу не трогают)
                float4 spark2 = OutlineData(id, OL_COL_SPARKLE2);
                if (spark2.x > 0.0)
                {
                    float cell = max(spark2.y, 2.0);
                    float2 q = (p - OutlineData(id, OL_COL_PATSPACE).yz) / cell;
                    float2 ci = floor(q);
                    float h = OutlineHash(ci + 0.37);
                    if (h < spark2.x)
                    {
                        float2 pt = float2(OutlineHash(ci + 11.1), OutlineHash(ci + 23.7)) * 0.7 + 0.15;
                        float d = length(frac(q) - pt) * cell;
                        float tw = saturate(sin(time * spark2.z * TWO_PI + h * 97.0));
                        float spark = pow(saturate(1.0 - d / (cell * 0.22)), 2.0) * tw * tw;
                        float4 sc = OutlineData(id, OL_COL_SPARKLE);
                        res.rgb += sc.rgb * sc.a * spark * (1.0 - t);
                    }
                }
                return res;
            }


            // Слои внутри силуэта записи id (premultiplied, до эффектов записи):
            // заливка (+ текстура), внутренний контур, rim, сканер; растворение режет всё
            float4 InsideLayers(uint id, float rimValue, float edgeDist, float2 p, float time)
            {
                float4 acc = 0;
                float4 widths = OutlineData(id, OL_COL_WIDTHS);
                float4 fill = OutlineData(id, OL_COL_FILL);
                float4 inner = OutlineData(id, OL_COL_INNER);
                float4 rim = OutlineData(id, OL_COL_RIM);
                float4 scan = OutlineData(id, OL_COL_SCAN);
                float4 texs = OutlineData(id, OL_COL_TEXTURES);
                float dissolve = DissolveAmount(id);

                // координаты пространства паттерна — нужны текстуре заливки, сканеру и растворению
                float2 q = p;
                float pxSize = 1.0;
                float3 pos = 0;
                bool hasPos = false;
                bool needCoords = (texs.y >= 0.0 && texs.w > 0.0) || scan.a > 0.0 || dissolve > 0.0;
                if (needCoords)
                    OutlineSpaceCoords(id, p, true, q, pxSize, pos, hasPos);

                if (fill.a > 0.0)
                {
                    float3 fc = fill.rgb;
                    float fa = fill.a;
                    if (texs.y >= 0.0 && texs.w > 0.0)
                    {
                        float tile = max(texs.z, 1e-4);
                        float4 tx = OutlineStyleTexture(texs.y, q / tile, tile / max(pxSize, 1e-5));
                        fc = lerp(fc, fc * tx.rgb, texs.w);
                        fa *= lerp(1.0, tx.a, texs.w);
                    }
                    Over(acc, fc, fa * OutlinePatternFor(id, LAYER_FILL, p, time, true));
                }
                if (widths.y > 0.0 && inner.a > 0.0)
                {
                    float t = edgeDist / widths.y;
                    if (t < 1.0)
                        Over(acc, inner.rgb, inner.a * OutlineLut(id * 2u + 1u, t) * OutlinePatternFor(id, LAYER_INNER, p, time, true));
                }
                if (rim.a > 0.0)
                    Over(acc, rim.rgb, rim.a * pow(saturate(rimValue), widths.z));

                // сканер: полоса бежит вдоль направления в пространстве паттерна
                if (scan.a > 0.0)
                {
                    float4 s2 = OutlineData(id, OL_COL_SCAN2);
                    float4 s3 = OutlineData(id, OL_COL_SCAN3);
                    // по плоскости развёртки: для поверхности y — высота на боковых гранях, x — горизонталь
                    float coord = dot(q, normalize(s2.xy + float2(0.0, 1e-5)));
                    float period = max(s2.w, 1e-4);
                    float d = abs(frac(coord / period - time * s3.z) - 0.5) * period;
                    float band = 1.0 - smoothstep(s3.x * 0.5, s3.x * 0.5 + max(s3.y, pxSize), d);
                    // полоса светит поверх, не требуя заливки
                    acc.rgb += scan.rgb * scan.a * band;
                    acc.a = saturate(acc.a + scan.a * band * 0.5);
                }

                // растворение: шумовой порог + светящаяся кромка (для прозрачных записей кромку рисует основа)
                if (dissolve > 0.0)
                {
                    float keep, edge;
                    DissolveTerms(id, q, dissolve, keep, edge);
                    acc *= keep;
                    if (OutlineData(id, OL_COL_SEETHRU).x < 0.5)
                    {
                        float4 ec = OutlineData(id, OL_COL_DISSOLVE_EDGE);
                        acc = float4(acc.rgb + ec.rgb * ec.a * edge, saturate(acc.a + ec.a * edge));
                    }
                }
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

                // растворение по fade заменяет общее затухание внутренних слоёв
                bool dissolveByFade = inside && OutlineData(id, OL_COL_DISSOLVE).w > 0.5;
                if (!dissolveByFade)
                    acc *= misc.x;
                acc.a *= 1.0 - misc.y;
                return acc;
            }

            float SegmentDistance(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a;
                float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-4));
                return distance(p, a + ab * t);
            }

            // Расстояние от p до края записи id: до сида pos0, а вблизи края при поле ниже ×1 — до ломаной
            // через два ближайших сида этой записи из окрестности 3×3 пикселей поля.
            float RefinedSeedDistance(int2 pf, float2 p, uint id, uint2 pos0, bool innerField)
            {
                float2 a = SeedToFrame(pos0);
                float d0 = distance(p, a);
                float block = _OutlineSeedSize.w; // пикселей кадра на пиксель поля
                if (block <= 1.001 || d0 > block * 3.0)
                    return d0;

                float2 b = a;
                float db = 1e20;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        int2 q = clamp(pf + int2(x, y), int2(_OutlineRect.xy), int2(_OutlineRect.zw) - 1);
                        float4 raw = innerField
                            ? LOAD_TEXTURE2D(_OutlineSeedsInner, uint2(q))
                            : LOAD_TEXTURE2D(_OutlineSeeds, uint2(q));
                        uint2 pos;
                        uint cid;
                        OutlineDecodeSeed(raw, pos, cid);
                        if (cid != id || all(pos == pos0))
                            continue;
                        float2 c = SeedToFrame(pos);
                        // соседний сид по краю — не дальше ~2.5 блоков, иначе ломаная срежет зазор
                        if (distance(c, a) > block * 2.5)
                            continue;
                        float dc = distance(p, c);
                        if (dc < db)
                        {
                            db = dc;
                            b = c;
                        }
                    }
                }
                return db < 1e19 ? min(d0, SegmentDistance(p, a, b)) : d0;
            }

            // Свечение записи id в точке p; dist — расстояние до её края от центра сида (уже уточнённое)
            float4 OuterEntryAt(uint id, uint2 seedPos, float dist, float2 p, float time)
            {
                float4 seedMask = OutlineLoadMask(int2(seedPos));
                float edgeDist = max(dist - EdgeOffset(seedMask), 0.0);
                float4 layer = OuterLayer(id, edgeDist, p, time);
                // видимость берём у пикселя-сида: какая часть силуэта «излучает» этот контур
                bool visible = seedMask.b > 0.5;
                return EntryEffects(layer, id, visible, false, p, time);
            }

            float4 OuterEntry(uint id, uint2 seedPos, float2 p, float time, int2 pf)
            {
                return OuterEntryAt(id, seedPos, RefinedSeedDistance(pf, p, id, seedPos, false), p, time);
            }

            #define OL_MAX_CAND 8

            // Окрестность 3×3 пикселя поля, прочитанная один раз: из неё и кандидаты, и уточнение края
            struct Neighborhood
            {
                uint2 pos[9];
                uint id[9];
                uint n;
            };

            // Уточнённое расстояние до края записи id по уже прочитанной окрестности (без новых выборок)
            float RefinedFromCache(Neighborhood nb, float2 p, uint id, uint2 pos0)
            {
                float2 a = SeedToFrame(pos0);
                float d0 = distance(p, a);
                float block = _OutlineSeedSize.w;
                if (nb.n <= 1u || d0 > block * 3.0)
                    return d0;
                float2 b = a;
                float db = 1e20;
                for (uint k = 0u; k < nb.n; k++)
                {
                    if (nb.id[k] != id || all(nb.pos[k] == pos0))
                        continue;
                    float2 c = SeedToFrame(nb.pos[k]);
                    if (distance(c, a) > block * 2.5)
                        continue;
                    float dc = distance(p, c);
                    if (dc < db)
                    {
                        db = dc;
                        b = c;
                    }
                }
                return db < 1e19 ? min(d0, SegmentDistance(p, a, b)) : d0;
            }

            // Добавить кандидата (id, сид), если такой записи ещё нет среди кандидатов
            void AddCandidate(uint2 pos, uint id, inout uint ids[OL_MAX_CAND], inout uint2 poss[OL_MAX_CAND], inout uint count)
            {
                if (id == 0u || OutlineEntryInvW(id) <= 0.0)
                    return;
                bool fresh = true;
                for (uint k = 0u; k < count; k++)
                    fresh = fresh && ids[k] != id;
                if (fresh && count < OL_MAX_CAND)
                {
                    ids[count] = id;
                    poss[count] = pos;
                    count++;
                }
            }

            void LoadSeed(int2 q, out uint2 pos, out uint id)
            {
                q = clamp(q, int2(_OutlineRect.xy), int2(_OutlineRect.zw) - 1);
                OutlineDecodeSeed(LOAD_TEXTURE2D(_OutlineSeeds, uint2(q)), pos, id);
            }

            // Снаружи. Кандидаты: сид пикселя поля; при поле ниже ×1 — сиды соседей 3×3 (пиксель поля покрывает
            // блок кадра, и в разных точках блока выигрывают разные записи — без этого узкое свечение соседа
            // «прокалывает» широкое дырами); при мягком стыке — ещё 4 сида на радиусе стыка.
            // Лучший кандидат выбирается заново для точки кадра p.
            float4 OuterComposite(int2 pf, float2 p, float time)
            {
                float block = _OutlineSeedSize.w;
                float blendPx = max(_OutlineParams.z, 0.0);

                Neighborhood nb = (Neighborhood)0;
                nb.n = 1u;
                LoadSeed(pf, nb.pos[0], nb.id[0]);
                if (nb.id[0] == 0u)
                    return 0;

                // за свечением ближайшей записи (с запасом на блок поля и стык) — пусто, без соседей:
                // сиды соседних пикселей поля дают отступ не меньше, чем этот, минус размер блока
                float d0 = distance(p, SeedToFrame(nb.pos[0]));
                float invW0 = OutlineEntryInvW(nb.id[0]);
                if ((d0 - block * 2.0 - HalfField()) * invW0 > 1.0 + blendPx * invW0)
                    return 0;

                uint ids[OL_MAX_CAND];
                uint2 poss[OL_MAX_CAND];
                [unroll]
                for (int c0 = 0; c0 < OL_MAX_CAND; c0++)
                {
                    ids[c0] = 0u;
                    poss[c0] = 0u;
                }
                uint count = 0u;
                AddCandidate(nb.pos[0], nb.id[0], ids, poss, count);

                // 4 выборки на радиусе стыка (без стыка — соседи ±1): новые записи-кандидаты и признак,
                // что рядом граница областей разных записей
                int r = blendPx > 0.0 ? max(1, (int)round(blendPx * _OutlineSeedSize.z)) : 1;
                int2 taps[4] = { int2(r, 0), int2(-r, 0), int2(0, r), int2(0, -r) };
                bool mixed = false;
                [unroll]
                for (int t = 0; t < 4; t++)
                {
                    uint2 pos;
                    uint id;
                    LoadSeed(pf + taps[t], pos, id);
                    mixed = mixed || (id != 0u && id != nb.id[0]);
                    if (blendPx > 0.0)
                        AddCandidate(pos, id, ids, poss, count);
                }

                // Поле ниже ×1: окрестность 3×3 нужна только у края (уточнение расстояния) и у границы записей
                // (иначе узкое свечение соседа пробивает дыры в широком). В толще свечения одной записи — не нужна.
                if (block > 1.001 && (mixed || d0 <= block * 3.0))
                {
                    const int2 offs[8] = { int2(-1, -1), int2(0, -1), int2(1, -1), int2(-1, 0),
                                           int2(1, 0), int2(-1, 1), int2(0, 1), int2(1, 1) };
                    [unroll]
                    for (int k = 0; k < 8; k++)
                    {
                        LoadSeed(pf + offs[k], nb.pos[k + 1], nb.id[k + 1]);
                        AddCandidate(nb.pos[k + 1], nb.id[k + 1], ids, poss, count);
                    }
                    nb.n = 9u;
                }

                // уточнённые расстояния и нормированные отступы кандидатов
                float dists[OL_MAX_CAND];
                float scores[OL_MAX_CAND];
                float best = 1e20;
                uint bestIdx = 0u;
                for (uint j = 0u; j < count; j++)
                {
                    dists[j] = RefinedFromCache(nb, p, ids[j], poss[j]);
                    scores[j] = dists[j] * OutlineEntryInvW(ids[j]) - OutlineEntryPrio(ids[j]);
                    if (scores[j] < best)
                    {
                        best = scores[j];
                        bestIdx = j;
                    }
                }
                if (count == 0u)
                    return 0;

                uint idB = ids[bestIdx];
                float invWB = OutlineEntryInvW(idB);
                // за свечением лучшего кандидата (с запасом на радиус стыка) — пусто
                if ((dists[bestIdx] - HalfField()) * invWB > 1.0 + blendPx * invWB)
                    return 0;

                if (count == 1u || blendPx <= 0.0)
                    return OuterEntryAt(idB, poss[bestIdx], dists[bestIdx], p, time);

                // разница в «ширинах» лучшей записи, при которой вес падает вдвое
                float softness = max(blendPx * invWB, 1e-4);
                float4 sum = 0;
                float wsum = 0;
                for (uint n = 0u; n < count; n++)
                {
                    float w = exp2(-(scores[n] - best) / softness);
                    if (w < 0.05)
                        continue;
                    sum += OuterEntryAt(ids[n], poss[n], dists[n], p, time) * w;
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
                    return float4((OutlineIdColor(selfId) * lerp(0.35, 1.0, m.b) + m.g * 0.25) * m.a, 1);
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

                float innerDist = innerId != 0u ? RefinedSeedDistance(pf, p, innerId, innerPos, hasInnerField) : 1e6;
                float offset = innerId != 0u ? EdgeOffset(OutlineLoadMask(int2(innerPos))) : 0.5;
                float edgeDist;
                if (selfId == 0u)
                {
                    edgeDist = max(innerDist - offset, 0.0);
                }
                else
                {
                    // внутри своей группы край на полпикселя дальше сида; на стыке с чужой — ближе
                    bool sameGroup = innerId != 0u
                        && OutlineEntryGroup(innerId) == OutlineEntryGroup(selfId);
                    edgeDist = sameGroup ? max(innerDist + offset, 0.0) : max(innerDist - offset, 0.0);
                }

                if (debugMode == 4u)
                {
                    float4 oc = LOAD_TEXTURE2D(_OutlineObjColor, uint2(p));
                    return oc.a > 0.0 ? float4(oc.rgb, 1) : float4(0.3, 0, 0.3, 1);
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
                {
                    float4 o = OuterComposite(pf, p, time);
                    // пустой результат не пишем — экономит запись и смешение в цвет камеры
                    if (o.a <= 0.0 && all(o.rgb <= 0.0))
                        discard;
                    return o;
                }

                // --- внутри силуэта ---
                float4 result = EntryEffects(InsideLayers(selfId, m.g, edgeDist, p, time),
                                             selfId, m.b > 0.5, true, p, time);

                // прозрачность/маскировка: основа из фона и объекта под слоями стиля (только видимая часть —
                // перекрытую сценой камера уже нарисовала сама)
                if (OutlineData(selfId, OL_COL_SEETHRU).x > 0.5 && m.b > 0.5)
                {
                    bool hasEdge = hasInnerField && innerId != 0u;
                    float4 seeBase = SeeThroughBase(selfId, p, edgeDist, SeedToFrame(innerPos), hasEdge, time);
                    result = OverPremul(result, seeBase);
                }

                // частично покрытый пиксель края: непокрытая доля получает свечение у самого края
                float cov = m.a;
                if (cov < 0.999)
                {
                    float4 edgeGlow = EntryEffects(OuterLayer(selfId, 0.0, p, time), selfId, m.b > 0.5, false, p, time);
                    result = result * cov + edgeGlow * (1.0 - cov);
                }

                // наложение свечения чужой группы на стыке
                if (_OutlineParams2.x > 0.5)
                {
                    uint2 op;
                    uint oid;
                    OutlineDecodeSeed(LOAD_TEXTURE2D(_OutlineSeeds, uint2(pf)), op, oid);
                    if (oid != 0u && OutlineEntryGroup(oid) != OutlineEntryGroup(selfId))
                    {
                        // сиды чужой группы лежат на самом стыке: расстояние до сида = глубина внутрь своего силуэта
                        float depth = max(distance(p, SeedToFrame(op)) - 0.5, 0.0);
                        bool lower = OutlineEntryPrio(oid) < OutlineEntryPrio(selfId);
                        float fadeR = max(_OutlineParams.z, 1.0);
                        float invWo = OutlineEntryInvW(oid);
                        // дешёвый отказ до выборок: глубже радиуса затухания (ниже приоритетом) или за шириной свечения
                        bool reach = lower ? depth < fadeR : (depth - _OutlineSeedSize.w * 2.0) * invWo < 1.0;
                        if (reach)
                        {
                            float4 glow = OuterEntry(oid, op, p, time, pf);
                            // группа с приоритетом ниже своего: свечение не обрывается на краю силуэта,
                            // а гаснет вглубь него на радиусе стыка
                            if (lower)
                                glow *= 1.0 - smoothstep(0.0, fadeR, depth);
                            result = OverPremul(glow, result);
                        }
                    }
                }
                if (result.a <= 0.0 && all(result.rgb <= 0.0))
                    discard;
                return result;
            }
            ENDHLSL
        }
    }
}
