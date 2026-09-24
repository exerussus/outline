// Маска подсветки. Рисуется поштучно (DrawRenderer) материалом-двойником на пару (исходный материал, запись):
// текстура/цвет альфы копируются с исходного материала на CPU, id и порог клипа — свойства двойника.
// Выход RGBA8: r = id/255, g = rim (1 - |N·V|), b = 1 видим / 0 перекрыт сценой, a = 1.
// С _OUTLINE_SURFACE — второй таргет RG16F: координата развёртки поверхности (объект или мир, по записи).
Shader "Hidden/Exerussus/Outline/Mask"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _OutlineId ("Outline Id", Float) = 0
        _OutlineClip ("Clip Threshold (<0 = нет)", Float) = -1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "OutlineMask"
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _OUTLINE_SURFACE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _OutlineId;
            float _OutlineClip;
            CBUFFER_END

            float4 _OutlineMaskGlobals; // x occlusionBias, z есть глубина сцены
            float _OutlineMaskObjectSpace[64]; // по id записи: 1 — позиция в координатах объекта, 0 — мира

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
            };

            struct MaskOut
            {
                half4 mask : SV_Target0;
            #if defined(_OUTLINE_SURFACE)
                float4 surface : SV_Target1;
            #endif
            };

            float DominantAxis(float3 n)
            {
                float3 a = abs(n);
                return a.x >= a.y && a.x >= a.z ? 0.0 : (a.y >= a.z ? 1.0 : 2.0);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.positionOS = input.positionOS.xyz;
                o.normalOS = input.normalOS;
                return o;
            }

            float EyeDepth(float raw)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    raw = 1.0 - raw;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            MaskOut Frag(Varyings input)
            {
                uint id = (uint)round(_OutlineId);
                if (id == 0u)
                    discard;

                // --- альфа: порог уже решён на CPU (Auto/ForceClip/Ignore) ---
                if (_OutlineClip >= 0.0)
                {
                    half coverage = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(coverage - _OutlineClip);
                }

                // --- rim ---
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim = 1.0 - abs(dot(n, v));

                // --- видимость относительно сцены ---
                half visible = 1.0;
                if (_OutlineMaskGlobals.z > 0.5)
                {
                    float sceneEye = EyeDepth(LoadSceneDepth(uint2(input.positionCS.xy)));
                    float selfEye = EyeDepth(input.positionCS.z);
                    float bias = _OutlineMaskGlobals.x + selfEye * 0.002;
                    visible = selfEye <= sceneEye + bias ? 1.0 : 0.0;
                }

                MaskOut o;
                o.mask = half4(id / 255.0, rim, visible, 1.0);
            #if defined(_OUTLINE_SURFACE)
                bool objectSpace = _OutlineMaskObjectSpace[id] > 0.5;
                float3 pos = objectSpace ? input.positionOS : input.positionWS;
                float3 nrm = objectSpace ? input.normalOS : input.normalWS;
                // развёртка по доминирующей оси нормали; вертикаль мира/объекта (y) на боковых гранях всегда во второй
                // координате — сканер вдоль y работает без 3D-позиции. Пишется в RG16F: вдвое меньше памяти
                float axis = DominantAxis(nrm);
                float2 q = axis < 0.5 ? pos.zy : (axis < 1.5 ? pos.xz : pos.xy);
                o.surface = float4(q, 0.0, 0.0);
            #endif
                return o;
            }
            ENDHLSL
        }

        // Цвет объекта для прозрачности/растворения: упрощённое освещение (основной свет + сферические гармоники)
        // по _BaseMap/_BaseColor исходного материала. Тот же материал-двойник, что у маски, — рисуется поштучно
        // и не зависит от вариантов шейдера исходного материала (GPU Resident Drawer).
        Pass
        {
            Name "OutlineObjectColor"
            ZWrite On
            ZTest LEqual
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _OutlineId;
            float _OutlineClip;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                if (_OutlineClip >= 0.0)
                    clip(albedo.a - _OutlineClip);
                float3 n = normalize(input.normalWS);
                Light light = GetMainLight();
                half3 lit = light.color * saturate(dot(n, light.direction)) + SampleSH(n);
                return half4(albedo.rgb * lit, 1.0);
            }
            ENDHLSL
        }

        // Координаты развёртки поверхности без MSAA — отдельный проход в RG16F со своей глубиной. Вторая цель у
        // MSAA-маски на встроенных GPU дорога (×4 памяти на сэмплы); координатам сглаживание не нужно.
        Pass
        {
            Name "OutlineSurface"
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _OutlineId;
            float _OutlineClip;
            CBUFFER_END

            float _OutlineMaskObjectSpace[64];

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.positionOS = input.positionOS.xyz;
                o.normalOS = input.normalOS;
                return o;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                uint id = (uint)round(_OutlineId);
                if (id == 0u)
                    discard;
                if (_OutlineClip >= 0.0)
                {
                    half coverage = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(coverage - _OutlineClip);
                }
                bool objectSpace = _OutlineMaskObjectSpace[id] > 0.5;
                float3 pos = objectSpace ? input.positionOS : input.positionWS;
                float3 a = abs(objectSpace ? input.normalOS : input.normalWS);
                // как в проходе маски: x → zy, y → xz, z → xy
                float2 q = a.x >= a.y && a.x >= a.z ? pos.zy : (a.y >= a.z ? pos.xz : pos.xy);
                return float4(q, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
}
