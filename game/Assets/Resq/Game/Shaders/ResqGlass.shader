// RESQ HUD glass: unlit vertex-colour panels and icons with alpha, drawn just before text (TMP sits at 3000)
// so labels always read on top of their panel. A soft top sheen keeps the frosted-glass feel without blur
// (Quest can't afford a screen-space blur on top of passthrough).
Shader "Resq/Glass"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
        _Sheen ("Top Sheen (linear)", Range(0,0.2)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" }
        Pass
        {
            Name "Glass"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Sheen;
            CBUFFER_END
            struct A { float4 pos:POSITION; half4 col:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; half4 col:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = TransformObjectToHClip(i.pos.xyz);
                half4 c = i.col;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                c.rgb = SRGBToLinear(c.rgb);
                #endif
                o.col = c; o.uv = i.uv;
                return o;
            }
            half4 frag(V i):SV_Target
            {
                half4 c = i.col;
                c.rgb += _Sheen * saturate(i.uv.y * 2.5 - 1.5) * (c.a < 0.95 ? 1 : 0); // a faint top highlight on panels only
                return c;
            }
            ENDHLSL
        }
    }
}
