// RESQ real hands in mixed reality: the tracked hand mesh punches a soft hole in the eye buffer, so the passthrough
// layer underneath shows the player's own hand in front of the diorama. Drawn after opaque geometry and before anything
// transparent, depth-tested and depth-writing, so a hand behind the board stays hidden and glass behind the hand is
// rejected. The rim fades with the view angle so the silhouette has no hard edge; a few millimetres of inflation hide
// the small tracking offset. Blend Zero OneMinusSrcAlpha scales both colour and alpha by (1 - hole).
Shader "Resq/HandCutout"
{
    Properties
    {
        _Inflate ("Inflate (m)", Float) = 0.003
        _Soft ("Rim softness", Range(0.05,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+200" }
        Pass
        {
            Name "Cutout"
            Tags { "LightMode"="UniversalForward" }
            Blend Zero OneMinusSrcAlpha
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Inflate;
                half _Soft;
            CBUFFER_END
            struct A { float4 pos:POSITION; float3 n:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; half3 n:TEXCOORD0; half3 v:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 n = TransformObjectToWorldNormal(i.n);
                float3 w = TransformObjectToWorld(i.pos.xyz) + n * _Inflate;
                o.pos = TransformWorldToHClip(w);
                o.n = n;
                o.v = GetCameraPositionWS() - w;
                return o;
            }
            half4 frag(V i):SV_Target
            {
                half e = saturate(saturate(dot(normalize(i.n), normalize(i.v))) / _Soft);
                return half4(0, 0, 0, e * e * (3 - 2 * e));
            }
            ENDHLSL
        }
    }
}
