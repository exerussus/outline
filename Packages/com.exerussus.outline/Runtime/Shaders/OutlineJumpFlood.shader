// Jump Flood по граничным пикселям маски.
// Сид = пиксель объекта, у которого 4-сосед пуст или принадлежит другой группе.
//
// Внешнее поле (_OutlineSeeds): взвешенное сравнение d / ширина_записи - приоритет, чтобы разные
// ширины и приоритеты не обрезали друг друга. Пиксель ВНУТРИ объекта принимает сиды только чужих групп
// (наложение на стыке); сиды своей группы хранятся со штрафом — только чтобы не рвать распространение поля.
// Внутреннее поле (_OutlineSeedsInner): обычное расстояние до ближайшего края (для внутреннего контура).
//
// Проходы: 0 Init, 1 Step — только внешнее поле; 2 InitDual, 3 StepDual — оба поля (MRT).
Shader "Hidden/Exerussus/Outline/JumpFlood"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        HLSLINCLUDE
        #pragma target 3.5
        #include "OutlineCommon.hlsl"

        struct DualOut
        {
            float4 outer : SV_Target0;
            float4 inner : SV_Target1;
        };

        // пиксель поля → пиксель маски (центр соответствующего блока)
        int2 FieldToMask(int2 pf)
        {
            return int2((float2(pf) + 0.5) * _OutlineSeedSize.w);
        }

        bool DiffersFrom(int2 p, float group)
        {
            uint n = OutlineMaskId(OutlineLoadMask(p));
            if (n == 0u)
                return true;
            return OutlineEntryGroup(n) != group;
        }

        // Сид поля = граничный пиксель КАДРА внутри блока, который покрывает пиксель поля
        // (ближайший к центру блока). Координаты хранятся в пикселях кадра — поле в пониженном
        // разрешении даёт расстояния с точностью кадра, а тонкие объекты не теряются.
        float4 InitSeed(int2 pf)
        {
            if (!OutlineInRect(pf))
                return 0;

            int k = clamp((int)ceil(_OutlineSeedSize.w - 1e-3), 1, 4);
            int2 base = int2(floor(float2(pf) * _OutlineSeedSize.w));
            float2 center = OutlineFieldToFrame(pf);
            int2 frameMax = int2(_OutlineMaskSize.xy) - 1;

            float bestD = 1e20;
            uint2 bestPos = 0;
            uint bestId = 0u;

            [loop]
            for (int y = 0; y < k; y++)
            {
                [loop]
                for (int x = 0; x < k; x++)
                {
                    int2 pm = base + int2(x, y);
                    if (any(pm > frameMax))
                        continue;
                    uint id = OutlineMaskId(OutlineLoadMask(pm));
                    if (id == 0u)
                        continue;

                    float group = OutlineEntryGroup(id);
                    // у края экрана клэмп даёт самого себя — это не граница, и это правильно
                    bool edge = DiffersFrom(pm + int2(1, 0), group)
                             || DiffersFrom(pm - int2(1, 0), group)
                             || DiffersFrom(pm + int2(0, 1), group)
                             || DiffersFrom(pm - int2(0, 1), group);
                    if (!edge)
                        continue;

                    float d = distance(float2(pm) + 0.5, center);
                    if (d < bestD)
                    {
                        bestD = d;
                        bestPos = uint2(pm);
                        bestId = id;
                    }
                }
            }
            return bestId != 0u ? OutlineEncodeSeed(bestPos, bestId) : 0;
        }

        float4 StepOuter(int2 pf, int step)
        {
            uint selfId = OutlineMaskId(OutlineLoadMask(FieldToMask(pf)));
            bool overlay = _OutlineParams2.x > 0.5;
            float2 pc = OutlineFieldToFrame(pf);
            float selfGroup = 0;
            if (selfId != 0u)
            {
                selfGroup = OutlineEntryGroup(selfId);
            }

            float bestScore = 1e20;
            float4 best = 0;

            [unroll]
            for (int y = -1; y <= 1; y++)
            {
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    int2 q = pf + int2(x, y) * step;
                    if (!OutlineInRect(q))
                        continue;

                    float4 raw = LOAD_TEXTURE2D(_OutlineSeeds, uint2(q));
                    uint2 pos;
                    uint id;
                    OutlineDecodeSeed(raw, pos, id);
                    if (id == 0u)
                        continue;

                    float invW = OutlineEntryInvW(id);
                    if (invW <= 0.0)
                        continue; // запись погашена (fade = 0)

                    float prio = OutlineEntryPrio(id);
                    float score = length(float2(pos) + 0.5 - pc) * invW - prio;

                    if (selfId != 0u)
                    {
                        // чужая группа любого приоритета: с меньшим композит гасит свечение вглубь силуэта
                        bool valid = overlay && OutlineEntryGroup(id) != selfGroup;
                        if (!valid)
                            score += OL_INVALID_PENALTY;
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = raw;
                    }
                }
            }
            return best;
        }

        float4 StepInner(int2 pf, int step)
        {
            float2 pc = OutlineFieldToFrame(pf);
            float bestScore = 1e20;
            float4 best = 0;

            [unroll]
            for (int y = -1; y <= 1; y++)
            {
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    int2 q = pf + int2(x, y) * step;
                    if (!OutlineInRect(q))
                        continue;

                    float4 raw = LOAD_TEXTURE2D(_OutlineSeedsInner, uint2(q));
                    uint2 pos;
                    uint id;
                    OutlineDecodeSeed(raw, pos, id);
                    if (id == 0u)
                        continue;

                    float d = length(float2(pos) + 0.5 - pc);
                    if (d < bestScore)
                    {
                        bestScore = d;
                        best = raw;
                    }
                }
            }
            return best;
        }
        ENDHLSL

        Pass
        {
            Name "Init"
            HLSLPROGRAM
            #pragma vertex OutlineFullscreenVert
            #pragma fragment Frag
            float4 Frag(OutlineVaryings input) : SV_Target
            {
                return InitSeed(int2(input.positionCS.xy));
            }
            ENDHLSL
        }

        Pass
        {
            Name "Step"
            HLSLPROGRAM
            #pragma vertex OutlineFullscreenVert
            #pragma fragment Frag
            float4 Frag(OutlineVaryings input) : SV_Target
            {
                int2 pf = int2(input.positionCS.xy);
                if (!OutlineInRect(pf))
                    return 0;
                return StepOuter(pf, (int)_OutlineParams.y);
            }
            ENDHLSL
        }

        Pass
        {
            Name "InitDual"
            HLSLPROGRAM
            #pragma vertex OutlineFullscreenVert
            #pragma fragment Frag
            DualOut Frag(OutlineVaryings input)
            {
                DualOut o;
                o.outer = InitSeed(int2(input.positionCS.xy));
                o.inner = o.outer;
                return o;
            }
            ENDHLSL
        }

        Pass
        {
            Name "StepDual"
            HLSLPROGRAM
            #pragma vertex OutlineFullscreenVert
            #pragma fragment Frag
            DualOut Frag(OutlineVaryings input)
            {
                DualOut o;
                int2 pf = int2(input.positionCS.xy);
                if (!OutlineInRect(pf))
                {
                    o.outer = 0;
                    o.inner = 0;
                    return o;
                }
                int step = (int)_OutlineParams.y;
                o.outer = StepOuter(pf, step);
                o.inner = StepInner(pf, step);
                return o;
            }
            ENDHLSL
        }
    }
}
