// Mundo en BLANCO Y NEGRO. Todo el escenario (y solo el escenario) pasa por aqui: la
// textura se conserva pero se colapsa a luminancia, y luego se le mete contraste para que
// la escena se lea grafica. Los personajes, sus VFX y el HUD NO usan este shader, asi que
// conservan su color: ese contraste es la identidad visual del juego.
//
// Se apoya en la luz principal de URP (direccional con sombras) + ambiente SH, que es lo
// que monta KomStageSetup. Sin PBR: el color ya no existe.
//
// No lleva pase de sombra a proposito: el escenario no necesita proyectar sobre si mismo
// (las paredes grandes ya vienen con shadowCastingMode Off) pero SI recibir la sombra de
// los luchadores, y eso lo resuelve el muestreo del shadow map en el fragmento.
Shader "KOM/StageBW"
{
    Properties
    {
        [MainTexture] _BaseMap ("Textura", 2D) = "white" {}
        [MainColor] _BaseColor ("Tinte", Color) = (1,1,1,1)
        _Contrast ("Contraste", Range(0.5, 3)) = 1.25
        _BlackPoint ("Punto negro", Range(0, 0.4)) = 0.04
        _Ambient ("Ambiente", Range(0, 1)) = 0.35
        _Emission ("Emision (luminancia)", Range(0, 2)) = 0
        // Recorte alfa. El emblema de la tarima (material Roseton) es un plano pegado al
        // suelo con fondo transparente: sin recorte, el fondo se dibuja NEGRO opaco y el
        // suelo de la plataforma se lee como un agujero. Las texturas opacas traen alfa=1
        // y no se recortan.
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
                float _Contrast;
                float _BlackPoint;
                float _Ambient;
                float _Emission;
                float _Cutoff;
            CBUFFER_END

            // Ajustes GLOBALES por escenario (Shader.SetGlobalFloat desde
            // StageBuilder.BuildRealStageLighting). No van en el CBUFFER del material
            // porque son de la escena, no de la pieza: asi se retoca el tono de todo el
            // mundo sin reescribir los 112 materiales.
            // Los `Enable` valen 0 por defecto: sin nadie que los ponga, el shader se
            // comporta como siempre (los menus y el inspector de materiales no se rompen).
            float _KOMWorldTone;      // 0 = sin tocar, 1 = aplicar gain/contraste
            float _KOMWorldGain;      // multiplicador de valor final
            float _KOMWorldContrast;  // exponente extra
            float _KOMPoolEnable;     // 0 = sin charco de luz
            float _KOMPoolInner;      // radio donde el suelo esta a plena luz (m)
            float _KOMPoolOuter;      // radio donde el suelo llega a su valor minimo (m)
            float _KOMPoolFloor;      // valor del suelo en el borde (0..1)
            float _KOMPoolHeight;     // altura por debajo de la cual se aplica (m)
            float _KOMHazeEnable;     // 0 = sin bruma
            float _KOMHazeStart;      // distancia a la que empieza (m)
            float _KOMHazeEnd;        // distancia a la que esta al maximo (m)
            float _KOMHazeValue;      // valor hacia el que se va lo lejano (0..1)

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
                // Luminancia Rec.709: conserva la estructura (mosaico, letreros, veta)
                // pero sin color.
                float lum = dot(albedo, float3(0.2126, 0.7152, 0.0722));

                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = saturate(dot(normalWS, mainLight.direction));
                // Half-lambert: la escena no se va a negro en las caras en sombra.
                float wrap = ndl * 0.5 + 0.5;

                float lit = wrap * mainLight.shadowAttenuation * mainLight.color.g + _Ambient;
                float value = saturate(lum * lit);
                value = saturate((value - _BlackPoint) / max(0.001, 1.0 - _BlackPoint));
                value = saturate(pow(value, _Contrast * lerp(1.0, max(0.05, _KOMWorldContrast), _KOMWorldTone)));
                value *= lerp(1.0, _KOMWorldGain, _KOMWorldTone);

                // CHARCO DE LUZ DE ARENA: el suelo se apaga hacia los bordes y deja a los
                // luchadores en el centro de la lectura. Solo afecta a lo que esta por
                // debajo de _KOMPoolHeight, asi que el fondo y las paredes no se oscurecen
                // (si se apagara todo, el decorado desapareceria).
                float radial = length(input.positionWS.xz);
                float pool = lerp(1.0, _KOMPoolFloor,
                                  saturate((radial - _KOMPoolInner) / max(0.001, _KOMPoolOuter - _KOMPoolInner)));
                float ground = saturate(1.0 - input.positionWS.y / max(0.001, _KOMPoolHeight));
                value *= lerp(1.0, pool, saturate(_KOMPoolEnable) * ground);

                // BRUMA DE PROFUNDIDAD: lo lejano sube hacia un gris medio. Es lo que
                // separa el decorado del fondo del suelo de pelea; sin ella toda la nave
                // queda a un solo gris y la escena se lee plana.
                float dist = distance(input.positionWS, _WorldSpaceCameraPos);
                float haze = saturate((dist - _KOMHazeStart) / max(0.001, _KOMHazeEnd - _KOMHazeStart));
                value = lerp(value, _KOMHazeValue, haze * saturate(_KOMHazeEnable));

                value = saturate(value + _Emission);
                // half4 y no half: devolver un escalar escribe solo el canal R y el mundo
                // entero sale ROJO (bug visto en el primer render).
                return half4(value, value, value, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
