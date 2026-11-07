// PUBLICO DEL ZOCALO EN BLANCO Y NEGRO (sprites animados).
//
// Cada espectador es un quad billboard cilindrico (gira en Y hacia la camara, no se
// inclina) con una pareja de fotogramas en el atlas. La pareja se elige en el horno
// (aplauso o celebracion), nunca se mezclan las dos familias: los cuatro fotogramas
// originales del paquete son poses sueltas y reproducirlos en orden hacia aletear los
// brazos. Aqui solo se alterna dentro de la pareja, con fase y velocidad por asiento.
//
// RELOJ. El fotograma NO se elige con _Time.y * fps(energia). Ese producto explota: si
// la euforia cambia el fps mientras _Time.y vale 60 s, la fase se mueve a
// 60 * dfps/dt fotogramas por segundo, o sea cientos, y la grada parpadea entre las dos
// poses como ruido. Cuanto mas dura el asalto, peor. Ahora el reloj se INTEGRA en el
// CPU (KomStageCrowd): _CrowdCycle acumula fotogramas y _CrowdTime acumula segundos.
// El ritmo de cada asiento es una constante que multiplica ese acumulado, asi que
// subir la euforia acelera la grada sin romper la fase.
//
// El valor final usa el MISMO pipeline que KOM/StageBW (ganancia/contraste/bruma globales)
// para que el publico pertenezca al escenario.
//
// PIES CLAVADOS. La grada no se desplaza en vertical: ni salto de euforia ni respiracion
// trasladada. Un sprite de cuerpo entero que sube y baja sobre el escalon se lee como un
// recorte que levita, no como alguien que brinca (no hay flexion de rodillas ni aterrizaje
// que lo justifiquen). La euforia se expresa con el balanceo (que inclina el cuerpo desde
// los pies) y con el pulso de escala, que crece desde el ancla del pie: los pies no se
// despegan del escalon ni un milimetro.
//
// Atributos por vertice:
//   TEXCOORD0  uv local del fotograma (0..1)
//   TEXCOORD1  (esquina.x, esquina.y, espejo, fase)
//   TEXCOORD2  (u0, v0, cellU, cellV)  base de la pareja en el atlas
//   TEXCOORD3  (ritmo del ciclo, -, -, -)  multiplica el reloj por personaje
//   COLOR      (tinte, tinte, tinte, profundidad de fila)
Shader "KOM/CrowdBW"
{
    Properties
    {
        [MainTexture] _BaseMap ("Atlas de publico", 2D) = "white" {}
        _Cutoff ("Recorte alfa", Range(0, 1)) = 0.35
        _CrowdLit ("Luz horneada", Range(0, 2)) = 0.85
        _BlackPoint ("Punto negro", Range(0, 0.4)) = 0.02
        _Contrast ("Contraste", Range(0.5, 3)) = 1.0
        _DepthDarken ("Oscurecer filas de fondo", Range(0.5, 1)) = 0.90

        [Header(Animacion)]
        _CrowdFrames ("Fotogramas por ciclo", Range(1, 8)) = 2
        _CrowdFps ("Fotogramas por segundo", Range(2, 24)) = 4.2
        _CrowdCycle ("Reloj del ciclo (fotogramas)", Float) = 0
        _CrowdTime ("Reloj de gesto (segundos)", Float) = 0
        _CrowdEnergy ("Energia (0-1)", Range(0, 1)) = 0
        _CrowdSway ("Balanceo", Range(0, 0.05)) = 0.006
        _CrowdPulse ("Respiracion (escala)", Range(0, 0.05)) = 0.008
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 uv2        : TEXCOORD1;
                float4 atlas      : TEXCOORD2;
                float4 cycle      : TEXCOORD3;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 localUv    : TEXCOORD0;
                float4 atlas      : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float  tint       : TEXCOORD3;
                float  depth      : TEXCOORD4;
                float  flip       : TEXCOORD5;
                float  phase      : TEXCOORD6;
                float  cycleScale : TEXCOORD7;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float _CrowdLit;
                float _BlackPoint;
                float _Contrast;
                float _DepthDarken;
                float _CrowdFrames;
                float _CrowdFps;
                float _CrowdCycle;
                float _CrowdTime;
                float _CrowdEnergy;
                float _CrowdSway;
                float _CrowdPulse;
            CBUFFER_END

            // Ajustes globales del escenario (StageBuilder.BuildRealStageLighting).
            float _KOMWorldTone;
            float _KOMWorldGain;
            float _KOMWorldContrast;
            float _KOMHazeEnable;
            float _KOMHazeStart;
            float _KOMHazeEnd;
            float _KOMHazeValue;

            Varyings vert(Attributes input)
            {
                Varyings o;

                float3 anchorWS = TransformObjectToWorld(input.positionOS.xyz);

                float3 toCam = _WorldSpaceCameraPos - anchorWS;
                toCam.y = 0.0;
                float len = length(toCam);
                toCam = len > 0.0001 ? toCam / len : float3(0.0, 0.0, 1.0);

                float3 up = float3(0.0, 1.0, 0.0);
                // `toCam` va del ancla del pie a la camara. `cross(up, toCam)` apunta a la
                // IZQUIERDA de pantalla: el quad salia espejado y, con arte de 3/4, los dos
                // flancos miraban hacia FUERA de la arena (el espejo del layout se calcula
                // suponiendo que +X del quad es la derecha de la pantalla). El orden
                // correcto es `cross(toCam, up)`: +X del quad = derecha de pantalla = +U de
                // la textura, y el espejo de los laterales vuelve a apuntar al centro.
                float3 right = normalize(cross(toCam, up));

                float2 off = input.uv2.xy;
                float phase = input.uv2.w;
                float tau = phase * 6.2831853;
                float energy = saturate(_CrowdEnergy);

                // Respiracion permanente: solo ESCALA desde los pies (`off.y` es la altura
                // local del vertice, 0 en el borde inferior). No traslada el quad: si se
                // sumara un desplazamiento vertical, los pies se despegarian del escalon.
                float breathe = sin(_CrowdTime * (0.85 + frac(phase * 13.0) * 0.55) + tau);
                off.y *= 1.0 + _CrowdPulse * breathe;

                // Balanceo: inclina el cuerpo entero (proporcional a la altura), no lo
                // traslada, asi los pies siguen clavados en el escalon.
                float sway = sin(_CrowdTime * 0.55 + tau) * (_CrowdSway + energy * 0.012);
                off.x += off.y * sway;

                float3 worldPos = anchorWS + right * off.x + up * off.y;

                o.positionCS = TransformWorldToHClip(worldPos);
                o.positionWS = worldPos;
                o.localUv = input.uv;
                o.atlas = input.atlas;
                o.tint = input.color.r;
                o.depth = input.color.a;
                o.flip = input.uv2.z;
                o.phase = phase;
                o.cycleScale = input.cycle.x > 0.01 ? input.cycle.x : 1.0;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Fotograma por miembro: el reloj ya viene integrado (fotogramas
                // acumulados); aqui solo se multiplica por el ritmo CONSTANTE del asiento
                // y por su ritmo propio. Nada de multiplicar un reloj absoluto por un fps
                // variable: eso era lo que hacia parpadear la grada.
                float seatRate = 0.94 + frac(input.phase * 11.0) * 0.12;
                float t = _CrowdCycle * input.cycleScale * seatRate
                        + input.phase * _CrowdFrames;
                float frame = floor(fmod(t, _CrowdFrames));

                float2 uvLocal = input.localUv;
                if (input.flip > 0.5) uvLocal.x = 1.0 - uvLocal.x;
                float2 uv = input.atlas.xy
                          + float2(frame * input.atlas.z, 0.0)
                          + uvLocal * input.atlas.zw;

                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                clip(tex.a - _Cutoff);

                float lum = dot(tex.rgb, float3(0.2126, 0.7152, 0.0722));
                lum = saturate(lum * _CrowdLit * input.tint);

                float value = saturate((lum - _BlackPoint) / max(0.001, 1.0 - _BlackPoint));
                value = saturate(pow(value, _Contrast * lerp(1.0, max(0.05, _KOMWorldContrast), _KOMWorldTone)));
                value *= lerp(1.0, _KOMWorldGain, _KOMWorldTone);
                value *= lerp(1.0, _DepthDarken, input.depth);

                float dist = distance(input.positionWS, _WorldSpaceCameraPos);
                float haze = saturate((dist - _KOMHazeStart) / max(0.001, _KOMHazeEnd - _KOMHazeStart));
                value = lerp(value, _KOMHazeValue, haze * saturate(_KOMHazeEnable));

                return half4(value, value, value, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
