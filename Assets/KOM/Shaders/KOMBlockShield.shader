// Escudo de bloqueo como AURA: cascara sobre la malla del luchador.
//
// No es un cartel delante de la camara. Se dibuja sobre una copia del SkinnedMeshRenderer
// (igual que el aura de super), asi que sigue la pose y rodea el cuerpo. Lo que lo hace
// "escudo" y no "aura generica" es la DIRECTRIZ: el filo fresnel se refuerza en las caras
// que miran al rival (_Forward), de modo que la barrera se lee del lado del enemigo.
//
// Es sutil a proposito: el filo se queda en ~0.35 de alfa y el relleno casi no existe.
// Un escudo opaco tapa al luchador y se ve como un cartel; uno tenue se lee como energia.
Shader "KOM/BlockShield"
{
    Properties
    {
        _Color ("Color", Color) = (0.42,0.80,1,1)
        _HotColor ("Hot", Color) = (0.88,0.97,1,1)
        _Intensity ("Intensity", Float) = 1.0
        _RimPower ("Rim Power", Float) = 2.4
        _Expand ("Expand", Float) = 0.012
        _Alpha ("Alpha", Float) = 0
        _Flash ("Flash", Float) = 0
        _Guard ("Guard", Float) = 1
        _Open ("Open", Float) = 0
        _Seed ("Seed", Float) = 0
        _Forward ("Forward (world)", Vector) = (1,0,0,0)
        _FeetY ("Feet Y (world)", Float) = 0
        _BandCenter ("Band Center", Float) = 1.05
        _BandHeight ("Band Height", Float) = 0.95
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+35"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        ZWrite Off
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ShieldAura"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _HotColor;
                float _Intensity;
                float _RimPower;
                float _Expand;
                float _Alpha;
                float _Flash;
                float _Guard;
                float _Open;
                float _Seed;
                float4 _Forward;
                float _FeetY;
                float _BandCenter;
                float _BandHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionWS2 : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 inflated = v.positionOS.xyz + normalize(v.normalOS) * _Expand;
                VertexPositionInputs pos = GetVertexPositionInputs(inflated);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionWS2 = pos.positionWS;
                return o;
            }

            float hash11(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                float facing = saturate(dot(n, viewDir));

                // FILO: la cascara solo se ve de canto. Es lo que la hace aura.
                float fres = pow(1.0 - facing, _RimPower);

                // HACIA EL RIVAL: las caras que miran al enemigo se encienden mas. El
                // escudo se lee del lado del golpe, no del lado de la camara.
                float front = saturate(dot(n, normalize(_Forward.xyz)));

                // FAJA del torso: el escudo cubre pecho y hombros, no los pies.
                // Se mide en ALTURA DE MUNDO sobre los pies: la malla del personaje esta
                // centrada en el origen, asi que su Y local no sirve como referencia.
                float heightAboveFeet = i.positionWS2.y - _FeetY;
                float band = 1.0 - saturate(abs(heightAboveFeet - _BandCenter) / max(0.05, _BandHeight));
                band = smoothstep(0.0, 1.0, band);

                float guard = saturate(_Guard);
                float shield = fres * (0.32 + 1.15 * front) * (0.35 + 0.65 * band);
                shield *= lerp(0.55, 1.20, guard);

                // Barrido lento de energia que sube por la cara frontal: da vida al
                // aura sin convertirse en un parpadeo.
                float sweepPos = frac(_Seed * 0.09);
                float sweep = smoothstep(0.30, 0.0, abs(saturate(heightAboveFeet / 1.8) - sweepPos)) * front * band * 0.45;

                // Relleno casi nulo: solo un velo para que la barrera tenga cuerpo.
                float veil = (1.0 - fres) * front * band * 0.05;

                float pulse = 0.90 + 0.10 * sin(_Seed * 6.283 + _Open * 2.2);
                float reveal = smoothstep(0.0, 1.0, saturate(_Open));

                float a = saturate(shield + veil + sweep) * _Alpha * reveal * pulse * 1.0 * _Intensity;
                float3 col = _Color.rgb * (0.55 + shield * 1.5);
                col = lerp(col, _HotColor.rgb * 1.5, saturate(_Flash));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
