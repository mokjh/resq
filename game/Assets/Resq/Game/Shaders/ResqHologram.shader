// RESQ placement hologram and route lines: additive cyan with fresnel rim and drifting scanlines for ghosts;
// with _Dash > 0 the mesh's U coordinate (metres along the path) becomes marching dashes for evacuation routes.
Shader "Resq/Hologram"
{
    Properties
    {
        _Color ("Colour", Color) = (0.35, 0.95, 1.0, 1)
        _Intensity ("Intensity", Range(0, 3)) = 1.1
        _Dash ("Dash Length (m, 0 = solid)", Float) = 0
        _DashSpeed ("Dash Speed (m/s)", Float) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" }
        Pass
        {
            Name "Hologram"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Intensity; float _Dash; float _DashSpeed;
            CBUFFER_END
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; float3 wpos:TEXCOORD0; half3 n:TEXCOORD1; float2 uv:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS; o.n = TransformObjectToWorldNormal(i.n); o.uv = i.uv;
                return o;
            }
            half4 frag(V i):SV_Target
            {
                half a;
                if (_Dash > 0)
                {
                    float phase = frac((i.uv.x - _Time.y * _DashSpeed) / _Dash);
                    a = step(phase, 0.55) * 0.9;
                }
                else
                {
                    half3 n = normalize(i.n);
                    half3 v = normalize(GetWorldSpaceViewDir(i.wpos));
                    half rim = pow(1 - saturate(abs(dot(n, v))), 2.2);
                    half scan = 0.55 + 0.45 * sin(i.wpos.y * 900 - _Time.y * 6);
                    a = saturate(0.22 + rim * 0.9) * scan;
                }
                return half4(_Color.rgb * _Intensity, a * _Color.a);
            }
            ENDHLSL
        }
    }
}
