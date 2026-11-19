// Retro-cartoon toon shader for URP (Unity 6, Forward / Forward+).
// Cel-banded main + point lights, tinted shadows, rim light, hard specular blob, inverted-hull outline.
Shader "Sewer/Toon"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _ParallaxMap ("Height Map", 2D) = "white" {}
        _Parallax ("Parallax Depth (uv)", Range(0,0.08)) = 0
        _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)

        _Steps ("Light Bands", Range(1,6)) = 3
        _BandSoft ("Band Softness", Range(0.001,0.5)) = 0.08
        _ShadowColor ("Shadow Tint", Color) = (0.42,0.32,0.62,1)
        _Ambient ("Ambient Boost", Range(0,2)) = 1.0

        _RimColor ("Rim Color", Color) = (1,0.9,1,1)
        _RimStrength ("Rim Strength", Range(0,2)) = 0.35
        _RimPower ("Rim Power", Range(0.5,8)) = 3

        _SpecSize ("Spec Size", Range(0,1)) = 0.08
        _SpecStrength ("Spec Strength", Range(0,2)) = 0.35

        _OutlineWidth ("Outline Width (m)", Float) = 0.0
        _OutlineColor ("Outline Color", Color) = (0.06,0.03,0.09,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            float _Parallax;
            half4 _EmissionColor;
            half _Steps, _BandSoft, _Ambient;
            half4 _ShadowColor;
            half4 _RimColor;
            half _RimStrength, _RimPower;
            half _SpecSize, _SpecStrength;
            float _OutlineWidth;
            half4 _OutlineColor;
        CBUFFER_END

        TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);     SAMPLER(sampler_BumpMap);
        TEXTURE2D(_ParallaxMap); SAMPLER(sampler_ParallaxMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        ENDHLSL

        // ------------------------------------------------------------ main lit pass
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                float  fog        : TEXCOORD4;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, i.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half Band(half x)
            {
                half q = saturate(x) * _Steps;
                half f = frac(q);
                return (floor(q) + smoothstep(0.5 - _BandSoft, 0.5 + _BandSoft, f)) / _Steps;
            }

            half3 ToonLight(Light l, float3 N, float3 V, half3 albedo)
            {
                half ndl = dot(N, l.direction) * 0.5h + 0.5h;          // half-lambert: softer, more cartoon
                half atten = l.distanceAttenuation * l.shadowAttenuation;
                half lit = Band(ndl * ndl * atten * 1.6h);
                half3 diffuse = albedo * l.color * lit;
                float3 H = normalize(l.direction + V);
                half spec = smoothstep(1 - _SpecSize, 1 - _SpecSize * 0.6, dot(N, H)) * _SpecStrength * step(0.01h, atten);
                return diffuse + l.color * spec * atten;
            }

            // Parallax occlusion mapping: bricks stand out of the mortar and shift with the view (volume without extra geometry)
            float2 ParallaxUV(float2 uv, float3 vTS, out half height)
            {
                height = 1;
                if (_Parallax <= 0.0001) return uv;
                float2 dx = ddx(uv), dy = ddy(uv);
                const int STEPS = 14;
                float layer = 1.0 / STEPS;
                float2 delta = vTS.xy / max(vTS.z, 0.35) * _Parallax * layer;
                float2 cur = uv;
                float depth = 0;
                float mapD = 1 - SAMPLE_TEXTURE2D_GRAD(_ParallaxMap, sampler_ParallaxMap, cur, dx, dy).r;
                [loop] for (int s = 0; s < STEPS && depth < mapD; s++)
                {
                    cur -= delta; depth += layer;
                    mapD = 1 - SAMPLE_TEXTURE2D_GRAD(_ParallaxMap, sampler_ParallaxMap, cur, dx, dy).r;
                }
                float2 prev = cur + delta;
                float after = mapD - depth;
                float before = (1 - SAMPLE_TEXTURE2D_GRAD(_ParallaxMap, sampler_ParallaxMap, prev, dx, dy).r) - depth + layer;
                float w = after / min(after - before, -1e-4);
                float2 res = lerp(cur, prev, saturate(w));
                height = SAMPLE_TEXTURE2D_GRAD(_ParallaxMap, sampler_ParallaxMap, res, dx, dy).r;
                return res;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 bit = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                float3 Vw = normalize(GetWorldSpaceViewDir(i.positionWS));
                half hgt;
                float2 uv = ParallaxUV(i.uv, float3(dot(Vw, i.tangentWS.xyz), dot(Vw, bit), dot(Vw, i.normalWS)), hgt);
                i.uv = uv;

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 albedo = tex.rgb * lerp(0.62h, 1.0h, hgt);   // mortar sits in shadow

                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 N = normalize(mul(nTS, float3x3(i.tangentWS.xyz, bit, i.normalWS)));
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = N;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);

                // ambient, tinted toward the shadow colour
                half3 amb = SampleSH(N) * _Ambient;
                half3 col = albedo * amb * lerp(_ShadowColor.rgb, 1, 0.35h);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainL = GetMainLight(shadowCoord);
                col += ToonLight(mainL, N, V, albedo);

                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_FORWARD_PLUS
                for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS, half4(1,1,1,1));
                    col += ToonLight(l, N, V, albedo);
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                    col += ToonLight(l, N, V, albedo);
                LIGHT_LOOP_END
                #endif

                // rim
                half rim = pow(1 - saturate(dot(N, V)), _RimPower);
                col += _RimColor.rgb * smoothstep(0.45, 0.55, rim) * _RimStrength * (0.4h + albedo);

                col += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ outline (inverted hull)
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float fog : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 pWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(i.normalOS));
                float dist = distance(pWS, GetCameraPositionWS());
                float w = _OutlineWidth * clamp(1 + dist * 0.04, 1, 3);
                pWS += nWS * w;
                o.positionCS = _OutlineWidth > 0 ? TransformWorldToHClip(pWS) : float4(0, 0, -2, 1);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, i.fog), 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ own shadow/depth passes (same CBUFFER -> SRP Batcher compatible)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(A i) : SV_POSITION
            {
                float3 pWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(i.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - pWS);
                #else
                float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pWS, nWS, ld));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; };
            float4 vert(A i) : SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.n = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(V i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
