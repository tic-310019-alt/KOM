// Plata estilizada para la cadena. No usa la iluminacion de escena (el escenario
// es oscuro y un metal PBR puro se lee negro): key light fija + especular Blinn +
// fresnel frio + venas magicas del color del personaje. Asi el eslabon SIEMPRE
// tiene forma legible: cara clara, canto oscuro y brillo puntual.
Shader "KOM/ChainSilver"
{
    Properties
    {
        _Color ("Base Color", Color) = (0.52,0.57,0.65,1)
        _ShadeColor ("Shade Color", Color) = (0.07,0.09,0.13,1)
        _SpecColor2 ("Specular Color", Color) = (1,1,1,1)
        _SpecIntensity ("Specular Intensity", Float) = 2.2
        _Shininess ("Shininess", Float) = 42.0
        _RimColor ("Rim Color", Color) = (0.78,0.86,1,1)
        _RimPower ("Rim Power", Float) = 3.0
        _RimIntensity ("Rim Intensity", Float) = 0.55
        _GlowColor ("Glow Color", Color) = (0.4,0.9,0.85,1)
        _Glow ("Glow", Float) = 0.10
        _LightDir ("Key Light Dir", Vector) = (0.35,0.8,0.45,0)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+10"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Back
        ZWrite On

        Pass
        {
            Name "Silver"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _ShadeColor;
                half4 _SpecColor2;
                float _SpecIntensity;
                float _Shininess;
                half4 _RimColor;
                float _RimPower;
                float _RimIntensity;
                half4 _GlowColor;
                float _Glow;
                float4 _LightDir;
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
                float3 l = normalize(_LightDir.xyz);
                float3 h = normalize(l + viewDir);

                // Difusa con wrap: el canto del eslabon nunca queda 100% negro.
                float ndl = dot(n, l);
                float wrap = saturate((ndl + 0.45) / 1.45);

                // Gradiente de estudio: arriba claro, abajo oscuro (lectura de volumen).
                float sky = saturate(n.y * 0.5 + 0.5);
                float3 baseCol = lerp(_ShadeColor.rgb, _Color.rgb, saturate(sky * 0.55 + wrap * 0.45));

                float spec = pow(saturate(dot(n, h)), _Shininess) * _SpecIntensity;
                float specSoft = pow(saturate(dot(n, h)), 14.0) * 0.45;

                // Reflexion de entorno falsa: es lo que hace que un metal se lea
                // como metal aunque la escena sea casi negra. El vector reflejado
                // barre el tubo del eslabon de claro a oscuro.
                float3 r = reflect(-viewDir, n);
                float env = saturate(r.y * 0.5 + 0.5);
                float3 refl = lerp(_ShadeColor.rgb * 1.6, _SpecColor2.rgb, pow(env, 2.4));

                float fres = pow(1.0 - saturate(dot(n, viewDir)), _RimPower);

                float3 col = lerp(baseCol * (0.35 + 0.85 * wrap), refl, 0.42);
                col += _SpecColor2.rgb * (spec + specSoft);
                col += _RimColor.rgb * fres * _RimIntensity;
                col += _GlowColor.rgb * _Glow * (0.4 + 0.6 * fres);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
