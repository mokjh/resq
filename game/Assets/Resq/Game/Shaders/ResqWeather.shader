// RESQ room weather: rain, snow, embers and the cloud under the ceiling. Vertex colour with a soft round falloff from
// the quad's UVs, so a billboard reads as a flake or spark and a stretched quad as a tapered streak, with no texture.
// Alpha is blended separately so it composites correctly over passthrough. Drawn before the UI glass, so text stays clear.
Shader "Resq/Weather"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" }
        Pass
        {
            Name "Weather"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos:POSITION; half4 col:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; half4 col:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.col = i.col;
                o.uv = i.uv * 2 - 1;
                return o;
            }
            half4 frag(V i):SV_Target
            {
                half r = saturate(1 - dot(i.uv, i.uv));
                return half4(i.col.rgb, i.col.a * r);
            }
            ENDHLSL
        }
    }
}
