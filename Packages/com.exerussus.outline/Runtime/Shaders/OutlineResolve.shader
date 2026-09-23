// Резолв MSAA-маски в обычную: id — самый частый среди покрытых сэмплов, rim и видимость — среднее по его
// сэмплам, a — доля покрытых сэмплов (любым id). Покрытие даёт субпиксельное положение края в композите.
// Требует чтения MSAA-текстур в шейдере; где его нет, фича работает без сглаживания.
Shader "Hidden/Exerussus/Outline/Resolve"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            Name "Resolve"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma require msaatex
            #pragma multi_compile_local _ _OUTLINE_SURFACE
            #pragma vertex OutlineFullscreenVert
            #pragma fragment Frag
            #include "OutlineCommon.hlsl"

            #define OL_MAX_SAMPLES 8

            Texture2DMS<float4> _OutlineMaskMS;
            #if defined(_OUTLINE_SURFACE)
            Texture2DMS<float4> _OutlinePosMS;
            #endif

            struct ResolveOut
            {
                float4 mask : SV_Target0;
            #if defined(_OUTLINE_SURFACE)
                float4 surface : SV_Target1;
            #endif
            };
            float4 _OutlineResolveParams; // x число сэмплов

            ResolveOut Frag(OutlineVaryings input)
            {
                ResolveOut o = (ResolveOut)0;
                int2 p = int2(input.positionCS.xy);
                int n = clamp((int)_OutlineResolveParams.x, 1, OL_MAX_SAMPLES);

                float4 s[OL_MAX_SAMPLES];
                uint ids[OL_MAX_SAMPLES];
                uint covered = 0u;
                [unroll]
                for (int i = 0; i < OL_MAX_SAMPLES; i++)
                {
                    s[i] = 0;
                    ids[i] = 0u;
                    if (i < n)
                    {
                        s[i] = LOAD_TEXTURE2D_MSAA(_OutlineMaskMS, p, i);
                        ids[i] = OutlineMaskId(s[i]);
                        if (ids[i] != 0u)
                            covered++;
                    }
                }
                if (covered == 0u)
                    return o;

                // самый частый id среди покрытых сэмплов
                uint bestId = 0u;
                uint bestCount = 0u;
                [unroll]
                for (int a = 0; a < OL_MAX_SAMPLES; a++)
                {
                    uint c = 0u;
                    [unroll]
                    for (int b = 0; b < OL_MAX_SAMPLES; b++)
                        c += (ids[b] == ids[a] && ids[a] != 0u) ? 1u : 0u;
                    if (c > bestCount)
                    {
                        bestCount = c;
                        bestId = ids[a];
                    }
                }

                float rim = 0;
                float visible = 0;
                int firstBest = 0;
                [unroll]
                for (int k = OL_MAX_SAMPLES - 1; k >= 0; k--)
                {
                    if (ids[k] == bestId)
                    {
                        rim += s[k].g;
                        visible += s[k].b;
                        firstBest = k;
                    }
                }
                float inv = 1.0 / bestCount;
                // видимость бинарная: большинство сэмплов записи
                o.mask = float4(bestId / 255.0, rim * inv, visible * inv >= 0.5 ? 1.0 : 0.0, covered / (float)n);
            #if defined(_OUTLINE_SURFACE)
                // позиция — одного сэмпла записи: усреднение позиций разных граней дало бы ложную точку
                o.surface = LOAD_TEXTURE2D_MSAA(_OutlinePosMS, p, firstBest);
            #endif
                return o;
            }
            ENDHLSL
        }
    }
}
