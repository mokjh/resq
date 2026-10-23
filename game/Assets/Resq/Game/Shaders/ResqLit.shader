// RESQ Lit — the shared toon material for the art kit (one material, one draw state for the whole diorama).
// Inputs follow the Blender kit contract (art/lookdev/*.json): vertex RGB = full albedo (sRGB), vertex A = baked AO,
// UV = palette-atlas cell whose alpha is smoothness. The atlas is NOT multiplied into albedo again.
// Lighting: two-band toon ramp on the main light with a soft terminator, URP main-light shadows, SH ambient
// scaled by AO, a cool rim for readability against passthrough, and a small specular glint from atlas smoothness.
// Quest budget: one main light, no additional lights, no normal maps, no per-pixel texture beyond one atlas fetch.
Shader "Resq/Lit"
{
    Properties
    {
        [NoScaleOffset] _Atlas ("Palette Atlas (A = smoothness)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _RampSoftness ("Terminator Softness", Range(0.01, 0.5)) = 0.12
        _ShadowTone ("Shadow Tone", Color) = (0.55, 0.6, 0.72, 1)
        _AOStrength ("AO Strength", Range(0, 1)) = 0.85
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 0.9
        _RimColor ("Rim Colour", Color) = (0.75, 0.85, 1, 1)
        _RimPower ("Rim Power", Range(1, 8)) = 3.5
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.25
        _Glint ("Specular Glint", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Atlas); SAMPLER(sampler_Atlas);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint; half _RampSoftness; half4 _ShadowTone; half _AOStrength; half _AmbientBoost;
                half4 _RimColor; half _RimPower; half _RimStrength; half _Glint;
            CBUFFER_END
            struct A { float4 pos:POSITION; float3 n:NORMAL; half4 col:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 pos:SV_POSITION; float3 wpos:TEXCOORD0; half3 n:TEXCOORD1; half4 col:COLOR; float2 uv:TEXCOORD2;
                half fog:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO
            };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = TransformObjectToWorldNormal(i.n);
                half4 c = i.col;
                c.rgb *= _Tint.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                c.rgb = SRGBToLinear(c.rgb);
                #endif
                o.col = c;
                o.uv = i.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 frag(V i):SV_Target
            {
                half3 n = normalize(i.n);
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.wpos));
                Light l = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                half ao = lerp(1, i.col.a, _AOStrength);
                // two-band toon ramp with a soft terminator; shadows fold into the dark band
                half ndl = dot(n, l.direction);
                half band = smoothstep(-_RampSoftness, _RampSoftness, ndl) * l.shadowAttenuation;
                half3 shadowCol = _ShadowTone.rgb;
                half3 lightTerm = lerp(shadowCol, l.color, band);
                half3 ambient = SampleSH(n) * _AmbientBoost * ao;
                half3 albedo = i.col.rgb;
                half3 color = albedo * (lightTerm * lerp(0.6, 1, ao) * 0.75 + ambient);
                // glint from atlas smoothness (alpha) — one fetch, used only for the highlight
                half smooth = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, i.uv).a;
                half3 h = normalize(l.direction + viewDir);
                half spec = pow(saturate(dot(n, h)), lerp(8, 96, smooth)) * smooth * _Glint * band;
                color += l.color * spec;
                // cool rim keeps silhouettes readable against busy passthrough
                half rim = pow(1 - saturate(dot(n, viewDir)), _RimPower) * _RimStrength;
                color += _RimColor.rgb * rim * ao;
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 pos:POSITION; float3 n:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(A i):SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 w = TransformObjectToWorld(i.pos.xyz);
                float3 n = TransformObjectToWorldNormal(i.n);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(w, n, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(i.pos.xyz); return o; }
            half4 frag(V i):SV_Target { return 0; }
            ENDHLSL
        }
    }
}
