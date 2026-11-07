// PROPS RECOLECTABLES EN BLANCO Y NEGRO.
//
// El mundo es B/N (KOM/StageBW) y los objetos que caen en la arena tambien: si fueran a
// color romperian la identidad visual. Pero un objeto B/N sobre un escenario B/N
// desaparece, asi que el mismo material dibuja un AURA BLANCA de fresnel en el borde: el
// contorno se enciende y el prop se lee contra cualquier fondo sin necesitar color.
//
// Es un shader propio y no KOM/StageBW a proposito: StageBW aplica el charco de luz y la
// bruma del escenario por POSICION MUNDIAL, y a un objeto que flota a 0.9 m lo apagaria
// como si fuera suelo. Aqui la iluminacion es la misma (half-lambert + sombra principal)
// pero sin nada atado al suelo.
Shader "KOM/PropBW"
{
    Properties
    {
        [MainTexture] _BaseMap ("Textura", 2D) = "white" {}
        [MainColor] _BaseColor ("Tinte", Color) = (1,1,1,1)
        // BANDA DE VALOR. En un mundo B/N el color no existe y lo unico que distingue un
        // objeto es su valor. La concha es clara y el vidrio oscuro: sin fijar la banda, la
        // primera sale quemada y el segundo se pierde contra el suelo. Cada prop declara en
        // que rango vive y la textura solo aporta el detalle de dentro.
        _ValueFloor ("Valor minimo", Range(0, 1)) = 0.20
        _ValueCeil ("Valor maximo", Range(0, 1)) = 0.90
        _Contrast ("Contraste", Range(0.5, 3)) = 1.00
        _LightInfluence ("Influencia de luz", Range(0, 1)) = 0.45
        _Emission ("Emision", Range(0, 0.5)) = 0.02
        // CONTORNO OSCURO. En un mundo B/N el blanco no separa: el objeto se lee por su
        // borde. Oscurecer el filo (fresnel invertido) lo despega del fondo tanto si
        // detras hay pared blanca como si hay suelo gris. Es el truco de tinta.
        _EdgeDarken ("Contorno oscuro", Range(0, 1)) = 0.0
        _RimColor ("Aura (color)", Color) = (1,1,1,1)
        _RimPower ("Aura (dureza)", Range(0.5, 8)) = 2.6
        _RimIntensity ("Aura (intensidad)", Range(0, 4)) = 0
        _Cutoff ("Recorte alfa", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _RimColor;
                float _ValueFloor;
                float _ValueCeil;
                float _Contrast;
                float _LightInfluence;
                float _Emission;
                float _EdgeDarken;
                float _RimPower;
                float _RimIntensity;
                float _Cutoff;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(tex.a - _Cutoff);
                float3 albedo = tex.rgb * _BaseColor.rgb;
                float lum = dot(albedo, float3(0.2126, 0.7152, 0.0722));
                // La textura solo reparte detalle DENTRO de la banda del prop.
                float band = lerp(_ValueFloor, _ValueCeil, saturate(pow(saturate(lum), _Contrast)));

                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = saturate(dot(normalWS, mainLight.direction));
                float wrap = ndl * 0.5 + 0.5;
                // La luz matiza el valor, no lo decide. Se satura a 1 porque la principal
                // es HDR: sin el tope, un foco fuerte empujaba el prop a blanco puro.
                float lit = saturate(wrap * mainLight.shadowAttenuation * mainLight.color.g);
                float value = saturate(band * lerp(1.0, 0.68 + lit * 0.52, _LightInfluence));
                value = saturate(value + _Emission);

                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _RimPower);

                // Contorno oscuro: el filo se apaga para que la silueta se lea contra
                // cualquier fondo. Sin esto un prop claro sobre pared blanca desaparece.
                value *= (1.0 - fresnel * _EdgeDarken);

                // Aura opcional (0 por defecto): solo el vidrio la usa, muy baja.
                float3 color = value.xxx + _RimColor.rgb * fresnel * _RimIntensity;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
