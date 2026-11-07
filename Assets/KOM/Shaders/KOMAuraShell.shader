// Aura de contorno sobre una COPIA del SkinnedMeshRenderer del personaje.
//
// Tecnica: SOLO fresnel sobre caras delanteras, con dos cascaras (una al ras de la
// piel y otra inflada ~2 cm). El fresnel vale 0 cuando la normal mira a camara y 1 en
// el contorno, asi que la intensidad cae de forma SUAVE hacia dentro y hacia fuera:
// no hay borde duro ni "silueta fantasma" (eso lo producia dibujar las caras
// traseras de un casco inflado, que terminan en un filo visible).
//
// El halo exterior lo pone el BLOOM del pipeline: el contorno va en HDR (>1) y el
// post-proceso lo expande de forma natural.
Shader "KOM/AuraShell"
{
    Properties
    {
        _Color ("Aura Color", Color) = (1,0.8,0.3,1)
        _HotColor ("Hot Color", Color) = (1,0.96,0.78,1)
        _Intensity ("Intensity", Float) = 1.4
        _Expand ("Expand", Float) = 0.02
        _NoiseScale ("Noise Scale", Float) = 6.0
        _Speed ("Speed", Float) = 1.7
        _RimPower ("Rim Power", Float) = 3.2
        _BodyHeight ("Body Height", Float) = 1.8
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+30"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        ZWrite Off
        Cull Back

        // ================= Contorno al ras de la piel =================
        Pass
        {
            Name "RimCore"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _HotColor;
                float _Intensity;
                float _Expand;
                float _NoiseScale;
                float _Speed;
                float _RimPower;
                float _BodyHeight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            float hash11(float n) { return frac(sin(n * 12.9898) * 43758.5453); }
            float noise3(float3 x)
            {
                float3 p = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f);
                float n = p.x + p.y * 57.0 + 113.0 * p.z;
                float v00 = lerp(hash11(n), hash11(n + 1.0), f.x);
                float v10 = lerp(hash11(n + 57.0), hash11(n + 58.0), f.x);
                float v01 = lerp(hash11(n + 113.0), hash11(n + 114.0), f.x);
                float v11 = lerp(hash11(n + 170.0), hash11(n + 171.0), f.x);
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
                float body = saturate(i.positionOS.y / max(0.2, _BodyHeight));
                float heightMask = lerp(1.0, 0.55, saturate(body * body));
                // Lenguas de energia: el contorno respira y se rompe.
                float t = _Time.y * _Speed;
                float f1 = noise3(i.positionOS * (_NoiseScale * 0.55) + float3(0.0, -t, 0.0));
                float f2 = noise3(i.positionOS * (_NoiseScale * 1.6) + float3(t * 0.4, -t * 1.4, 0.0));
                float flick = saturate(f1 * 0.68 + f2 * 0.5);
                float a = _Intensity * fres * heightMask * (0.35 + 1.15 * flick);
                float3 col = lerp(_Color.rgb, _HotColor.rgb, saturate(fres * 0.55 + flick * 0.25));
                return half4(col * a, 1.0);
            }
            ENDHLSL
        }

        // ================= Cascara inflada: ensancha el contorno =================
        Pass
        {
            Name "RimShell"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _HotColor;
                float _Intensity;
                float _Expand;
                float _NoiseScale;
                float _Speed;
                float _RimPower;
                float _BodyHeight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float flame : TEXCOORD3;
            };

            float hash11(float n) { return frac(sin(n * 12.9898) * 43758.5453); }
            float noise3(float3 x)
            {
                float3 p = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f);
                float n = p.x + p.y * 57.0 + 113.0 * p.z;
                float v00 = lerp(hash11(n), hash11(n + 1.0), f.x);
                float v10 = lerp(hash11(n + 57.0), hash11(n + 58.0), f.x);
                float v01 = lerp(hash11(n + 113.0), hash11(n + 114.0), f.x);
                float v11 = lerp(hash11(n + 170.0), hash11(n + 171.0), f.x);
                return lerp(lerp(v00, v10, f.y), lerp(v01, v11, f.y), f.z);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 nrm = normalize(v.normalOS);
                float t = _Time.y * _Speed;
                float f1 = noise3(v.positionOS.xyz * (_NoiseScale * 0.5) + float3(0.0, -t, 0.0));
                float f2 = noise3(v.positionOS.xyz * (_NoiseScale * 1.4) + float3(t * 0.35, -t * 1.5, 0.0));
                float flick = saturate(f1 * 0.7 + f2 * 0.45);
                float3 pos = v.positionOS.xyz + nrm * (_Expand * (0.45 + 1.25 * flick));
                o.positionWS = TransformObjectToWorld(pos);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(nrm);
                o.positionOS = v.positionOS.xyz;
                o.flame = flick;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                // Fresnel mas ancho que el nucleo: se funde con el hacia dentro.
                float fres = pow(1.0 - saturate(dot(n, viewDir)), 2.0);
                float body = saturate(i.positionOS.y / max(0.2, _BodyHeight));
                float heightMask = lerp(1.0, 0.45, saturate(body * body));
                float a = _Intensity * 0.70 * fres * heightMask * (0.30 + 1.1 * i.flame);
                float3 col = lerp(_Color.rgb, _HotColor.rgb, saturate(i.flame * 0.4));
                return half4(col * a, 1.0);
            }
            ENDHLSL
        }
    }
}
