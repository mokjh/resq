// RESQ Terrain — the board's ground (Look 3, concept art direction). Same lighting model as Resq/Lit (toon band,
// URP main-light shadows, SH ambient × baked AO, cool rim) plus cheap procedural surface detail so the miniature
// reads as grass, soil and layered rock rather than flat colour: world-space value noise speckles flats (grass
// tufts / snow sparkle / ash), slopes get horizontal striations and a perturbed normal. No textures, no extra lights.
Shader "Resq/Terrain"
{
    Properties
    {
        _RampSoftness ("Terminator Softness", Range(0.01, 0.5)) = 0.18
        _ShadowTone ("Shadow Tone", Color) = (0.55, 0.6, 0.72, 1)
        _AOStrength ("AO Strength", Range(0, 1)) = 0.9
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 0.95
        _RimColor ("Rim Colour", Color) = (1, 0.92, 0.8, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.12
        _Speckle ("Flat Speckle", Range(0, 0.5)) = 0.16
        _SpeckleScale ("Speckle Scale (per metre)", Float) = 900
        _Strata ("Slope Striations", Range(0, 0.6)) = 0.22
        _StrataScale ("Striations per metre", Float) = 520
        _Bump ("Slope Bump", Range(0, 1)) = 0.35
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
            CBUFFER_START(UnityPerMaterial)
                half _RampSoftness; half4 _ShadowTone; half _AOStrength; half _AmbientBoost;
                half4 _RimColor; half _RimStrength; half _Speckle; float _SpeckleScale; half _Strata; float _StrataScale; half _Bump;
            CBUFFER_END
            struct A { float4 pos:POSITION; float3 n:NORMAL; half4 col:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 pos:SV_POSITION; float3 wpos:TEXCOORD0; half3 n:TEXCOORD1; half4 col:COLOR; float3 opos:TEXCOORD2;
                half fog:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO
            };
            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS; o.opos = i.pos.xyz;
                o.n = TransformObjectToWorldNormal(i.n);
                half4 c = i.col;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                c.rgb = SRGBToLinear(c.rgb);
                #endif
                o.col = c;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 frag(V i):SV_Target
            {
                half3 n = normalize(i.n);
                // detail lives in object space so it sticks to the board when the player moves it around the table
                float3 q = i.opos;
                half flat = saturate((n.y - 0.75) * 4);
                // flats: two octaves of speckle (tufts and soil flecks)
                float sp = Noise(q.xz * _SpeckleScale) * 0.65 + Noise(q.xz * _SpeckleScale * 2.7 + 13.1) * 0.35;
                half flatMod = 1 + (sp - 0.5) * 2 * _Speckle;
                // slopes: wavy horizontal striations with crumbly breaks
                float wav = q.y * _StrataScale + Noise(q.xz * _StrataScale * 0.35) * 2.2;
                float st = abs(frac(wav) - 0.5) * 2;
                float brk = Noise(float2(q.x + q.z, q.y) * _StrataScale * 1.3);
                half slopeMod = 1 + (smoothstep(0.15, 0.6, st) - 0.5) * _Strata - (brk > 0.78 ? _Strata * 0.6 : 0);
                half mod = lerp(slopeMod, flatMod, flat);
                // bumpy normal on slopes only (striations catch the low sun)
                float3 bn = n;
                bn.y += (st - 0.5) * _Bump * (1 - flat);
                bn = normalize(bn);
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.wpos));
                Light l = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                half ao = lerp(1, i.col.a, _AOStrength);
                half ndl = dot(bn, l.direction);
                half band = smoothstep(-_RampSoftness, _RampSoftness, ndl) * l.shadowAttenuation;
                half3 lightTerm = lerp(_ShadowTone.rgb, l.color, band) * lerp(0.75, 1.0, saturate(ndl));
                half3 ambient = SampleSH(bn) * _AmbientBoost * ao;
                half3 albedo = i.col.rgb * mod;
                half3 color = albedo * (lightTerm * lerp(0.6, 1, ao) * 0.75 + ambient);
                half rim = pow(1 - saturate(dot(n, viewDir)), 4) * _RimStrength;
                color += _RimColor.rgb * rim * ao * band;
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }
        UsePass "Resq/Lit/SHADOWCASTER"
        UsePass "Resq/Lit/DEPTHONLY"
    }
}
