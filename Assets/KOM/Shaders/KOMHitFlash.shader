// Destello de impacto sobre el cuerpo del que recibe el golpe.
//
// Se dibuja sobre una COPIA del SkinnedMeshRenderer (misma tecnica que el aura y el
// escudo), en aditivo. En un juego en blanco y negro el destello es la unica forma de
// que el golpe "entre" en el personaje: la silueta se quema a blanco durante 2-4
// frames y el ojo registra el contacto aunque la camara tiemble.
//
// _Rim mezcla dos lecturas:
//   0.0 = silueta plana quemada (impacto limpio y seco)
//   1.0 = solo contorno (bloqueo / golpe absorbido por la guardia)
Shader "KOM/HitFlash"
{
    Properties
    {
        _Color     ("Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 0
        _Rim       ("Rim Weight", Range(0,1)) = 0.25
        _RimPower  ("Rim Power", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+45"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha One
        ZWrite Off
        Cull Back

        Pass
        {
            Name "HitFlash"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Rim;
                float _RimPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                float fres = pow(1.0 - saturate(dot(n, viewDir)), _RimPower);
                float a = _Intensity * lerp(1.0, saturate(0.18 + 1.55 * fres), _Rim);
                return half4(_Color.rgb, saturate(a));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
