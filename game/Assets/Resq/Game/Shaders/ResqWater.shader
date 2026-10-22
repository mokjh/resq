// RESQ stylised water: depth tint from vertex colour R, fresnel rim, soft highlight, shoreline foam.
// Alpha blends colour but accumulates alpha with One/OneMinusSrcAlpha so passthrough never shows through.
Shader "Resq/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.42,0.70,0.72,1)
        _Deep ("Deep", Color) = (0.16,0.36,0.42,1)
        _Foam ("Foam", Color) = (0.93,0.96,0.94,1)
        _Opacity ("Opacity", Range(0,1)) = 0.86
        _FlowSpeed ("Flow Speed", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Deep, _Foam; half _Opacity; half _FlowSpeed;
            CBUFFER_END
            struct A { float4 pos:POSITION; float3 n:NORMAL; half4 col:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; float3 wpos:TEXCOORD0; half3 n:TEXCOORD1; half4 col:COLOR; float2 uv:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS; o.n = TransformObjectToWorldNormal(i.n);
                o.col = i.col; o.uv = i.uv; return o;
            }
            half4 frag(V i):SV_Target
            {
                half depth = i.col.r;
                half wet = i.col.a;
                clip(wet - 0.02);
                half3 n = normalize(i.n);
                half3 v = normalize(GetWorldSpaceViewDir(i.wpos));
                Light l = GetMainLight();
                half3 baseCol = lerp(_Shallow.rgb, _Deep.rgb, saturate(depth * 1.4));
                // gentle animated ripples from the uv grid, no textures
                float t = _Time.y * _FlowSpeed;
                half ripple = sin(i.uv.x * 180 + t * 2.1) * sin(i.uv.y * 150 - t * 1.7) * 0.5 + 0.5;
                half fres = pow(1 - saturate(dot(n, v)), 4);
                half spec = pow(saturate(dot(reflect(-l.direction, n), v)), 48) * 0.6;
                half foam = saturate((0.06 - depth) * 18) * (0.55 + 0.45 * ripple);
                half3 col = baseCol * (0.55 + 0.45 * saturate(dot(n, l.direction))) + SampleSH(n) * 0.25;
                col = lerp(col, _Foam.rgb, foam * 0.7) + spec + fres * 0.18;
                return half4(col, saturate(_Opacity * wet + foam * 0.2));
            }
            ENDHLSL
        }
    }
}
