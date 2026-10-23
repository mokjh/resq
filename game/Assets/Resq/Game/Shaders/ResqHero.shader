// RESQ Hero — textured landmark and vehicle models (Meshy hero assets, one 2K→1K base-colour texture each).
// Same light model as Resq/Lit so hero props sit in the same diorama as the vertex-colour kit: toon band with a soft
// terminator, URP main-light shadows, SH ambient, warm rim. Meshy textures carry some baked shading, so the light
// term is gentler (_Flatten) to avoid double-darkening. One texture fetch, no normal maps.
Shader "Resq/Hero"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Colour", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _RampSoftness ("Terminator Softness", Range(0.01, 0.5)) = 0.2
        _ShadowTone ("Shadow Tone", Color) = (0.6, 0.64, 0.75, 1)
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 0.9
        _Flatten ("Flatten Baked Shading", Range(0, 1)) = 0.35
        _RimColor ("Rim Colour", Color) = (1, 0.92, 0.8, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.18
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
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _Tint; half _RampSoftness; half4 _ShadowTone; half _AmbientBoost; half _Flatten;
                half4 _RimColor; half _RimStrength;
            CBUFFER_END
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; float3 wpos:TEXCOORD0; half3 n:TEXCOORD1; float2 uv:TEXCOORD2; half fog:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = TransformObjectToWorldNormal(i.n);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 frag(V i):SV_Target
            {
                half3 n = normalize(i.n);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _Tint.rgb;
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.wpos));
                Light l = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                half ndl = dot(n, l.direction);
                half band = smoothstep(-_RampSoftness, _RampSoftness, ndl) * l.shadowAttenuation;
                half3 lightTerm = lerp(_ShadowTone.rgb, l.color, lerp(band, 0.5 + band * 0.5, _Flatten));
                half3 ambient = SampleSH(n) * _AmbientBoost;
                half3 color = albedo * (lightTerm * 0.75 + ambient);
                half rim = pow(1 - saturate(dot(n, viewDir)), 4) * _RimStrength;
                color += _RimColor.rgb * rim * band;
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }
        UsePass "Resq/Lit/SHADOWCASTER"
        UsePass "Resq/Lit/DEPTHONLY"
    }
}
