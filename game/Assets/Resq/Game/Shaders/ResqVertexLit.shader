// RESQ miniature-world opaque shader: vertex colour albedo, main-light lambert with soft wrap,
// URP main-light shadows and spherical-harmonics ambient. Cheap enough for Quest (no extra lights).
Shader "Resq/VertexLit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _Wrap ("Light Wrap", Range(0,1)) = 0.35
        _AmbientBoost ("Ambient Boost", Range(0,2)) = 0.8
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint; half _Wrap; half _AmbientBoost;
            CBUFFER_END
            struct A { float4 pos:POSITION; float3 n:NORMAL; half4 col:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; float3 wpos:TEXCOORD0; half3 n:TEXCOORD1; half4 col:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = TransformObjectToWorldNormal(i.n);
                half4 c = i.col * _Tint;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                c.rgb = SRGBToLinear(c.rgb); // authored palette colours are sRGB
                #endif
                o.col = c;
                return o;
            }
            half4 frag(V i):SV_Target
            {
                half3 n = normalize(i.n);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                half ndl = saturate((dot(n, l.direction) + _Wrap) / (1 + _Wrap));
                half3 lit = l.color * ndl * l.shadowAttenuation;
                half3 amb = SampleSH(n) * _AmbientBoost;
                return half4(i.col.rgb * (lit + amb), 1);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 pos:POSITION; float3 n:NORMAL; };
            float4 vert(A i):SV_POSITION
            {
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
    }
}
