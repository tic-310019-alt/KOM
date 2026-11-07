// Orbe de energia procedural. Sin texturas: plasma FBM en espacio de objeto +
// bandas de remolino + fresnel. Aditivo HDR para que el bloom lo tome.
Shader "KOM/EnergyOrb"
{
    Properties
    {
        _Color ("Plasma Color", Color) = (1,0.78,0.25,1)
        _CoreColor ("Core Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 2.2
        _RimPower ("Rim Power", Float) = 2.0
        _NoiseScale ("Noise Scale", Float) = 7.0
        _Speed ("Speed", Float) = 1.6
        _Swirl ("Swirl", Float) = 3.0
        _Alpha ("Alpha", Float) = 1.0
        _Bands ("Bands", Float) = 9.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            Name "Orb"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _CoreColor;
                float _Intensity;
                float _RimPower;
                float _NoiseScale;
                float _Speed;
                float _Swirl;
                float _Alpha;
                float _Bands;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            float hash11(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453);
            }

            float noise3(float3 x)
            {
                float3 p = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n = p.x + p.y * 57.0 + 113.0 * p.z;
                float v000 = hash11(n);
                float v100 = hash11(n + 1.0);
                float v010 = hash11(n + 57.0);
                float v110 = hash11(n + 58.0);
                float v001 = hash11(n + 113.0);
                float v101 = hash11(n + 114.0);
                float v011 = hash11(n + 170.0);
                float v111 = hash11(n + 171.0);
                float v00 = lerp(v000, v100, f.x);
                float v10 = lerp(v010, v110, f.x);
                float v01 = lerp(v001, v101, f.x);
                float v11 = lerp(v011, v111, f.x);
                return lerp(lerp(v00, v10, f.y), lerp(v01, v11, f.y), f.z);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                float fres = pow(1.0 - saturate(dot(n, viewDir)), _RimPower);

                float t = _Time.y * _Speed;
                float3 q = i.positionOS * _NoiseScale;
                float n1 = noise3(q + float3(0.0, -t, t * 0.35));
                float n2 = noise3(q * 2.35 + float3(t * 0.7, t * 0.25, -t * 0.9));
                float n3 = noise3(q * 4.7 - float3(t * 0.4, -t * 0.9, t * 0.3));
                float plasma = saturate(n1 * 0.55 + n2 * 0.32 + n3 * 0.18);

                // Bandas de remolino: giran alrededor del eje local Y y suben.
                float ang = atan2(i.positionOS.z, i.positionOS.x);
                float bands = 0.5 + 0.5 * sin(ang * _Swirl + i.positionOS.y * _Bands - t * 5.5);
                float bandMask = pow(bands, 2.2);

                float energy = saturate(plasma * 0.62 + bandMask * 0.38);
                // Cuerpo del plasma: color propio, NO blanco. Solo las venas mas
                // densas y el borde se van a blanco caliente. Sin esto el orbe se
                // satura a una bola blanca plana.
                float3 col = lerp(_Color.rgb * 0.45, _Color.rgb, energy);
                float veins = smoothstep(0.58, 0.98, energy);
                col = lerp(col, _CoreColor.rgb, veins * 0.75);
                col += _Color.rgb * fres * 1.35;
                col *= _Intensity * (0.55 + 0.75 * energy);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
