// Pantalla de impacto. Un solo quad delante de la camara que dibuja, en aditivo,
// las tres capas que convierten un golpe en un "impacto" legible:
//
//   1. FLASH   - levantamiento global de blanco (el "impact frame" del anime).
//   2. LINEAS  - lineas de foco radiales que nacen en el punto de impacto.
//   3. ANILLO  - onda de choque que se abre en espacio de pantalla.
//
// Todo se calcula en el fragment: no hay texturas ni particulas, asi que cuesta
// un draw call por impacto y se puede emitir a 60 Hz sin coste apreciable.
//
// El color lo pone el llamante (acento del atacante); el nucleo va a blanco caliente
// para que el bloom lo lea como una fuente de luz y no como una calcomania.
Shader "KOM/ImpactScreen"
{
    Properties
    {
        _Color        ("Color", Color) = (1,1,1,1)
        _HotColor     ("Hot Color", Color) = (1,1,1,1)
        _Flash        ("Flash", Range(0,2)) = 0
        _LinesT       ("Lines Progress", Range(0,1)) = 1
        _LinesA       ("Lines Amount", Range(0,2)) = 0
        _RingT        ("Ring Progress", Range(0,1)) = 1
        _RingA        ("Ring Amount", Range(0,2)) = 0
        _Center       ("Center (uv)", Vector) = (0.5,0.5,0,0)
        _Aspect       ("Aspect", Float) = 1.7777
        _Overscan     ("Overscan", Float) = 1.3
        _Spokes       ("Spokes", Float) = 26
        _Seed         ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Blend One One
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ImpactScreen"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _HotColor;
                float  _Flash;
                float  _LinesT;
                float  _LinesA;
                float  _RingT;
                float  _RingA;
                float4 _Center;
                float  _Aspect;
                float  _Overscan;
                float  _Spokes;
                float  _Seed;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float Hash11(float x)
            {
                return frac(sin(x * 127.1 + _Seed * 13.7) * 43758.5453);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Espacio polar con correccion de aspecto: las lineas no se estiran
                // cuando la ventana no es 16:9. El quad va sobredimensionado para que
                // el balanceo de camara no descubra las esquinas; `_Overscan` devuelve
                // su uv al uv real de pantalla para que `_Center` siga siendo exacto.
                float2 suv = (i.uv - 0.5) * _Overscan + 0.5;
                float2 p = suv - _Center.xy;
                p.x *= _Aspect;
                float r = length(p);
                float ang = atan2(p.y, p.x);

                // --- lineas de foco -------------------------------------------------
                float sector = (ang / 6.2831853 + 0.5) * _Spokes;
                float idx = floor(sector);
                float local = frac(sector) * 2.0 - 1.0;
                float h = Hash11(idx);

                // Grosor variable por radio: la punta se afila, como un trazo de tinta.
                float sharp = lerp(18.0, 9.0, _LinesT);
                float spokes = pow(saturate(1.0 - abs(local)), sharp);
                // Solo algunos radios: un abanico completo se lee como un cartel.
                spokes *= step(0.55, h);

                float r0 = lerp(0.015, 0.42, _LinesT);
                float lines = spokes
                            * smoothstep(r0 * 0.30, r0, r)
                            * (1.0 - smoothstep(r0, r0 + 0.34, r))
                            * _LinesA * pow(saturate(1.0 - _LinesT), 1.5);

                // --- anillo ---------------------------------------------------------
                float rr = _RingT * 1.15;
                float d = abs(r - rr);
                float ring = pow(saturate(1.0 - d / 0.075), 2.2)
                           + 0.30 * pow(saturate(1.0 - d / 0.26), 3.0);
                ring *= _RingA * (1.0 - _RingT);

                // --- flash ----------------------------------------------------------
                // SOLO radial: antes habia una base de 0.26 que levantaba TODA la
                // pantalla y convertia cada golpe en un flashazo. El golpe tiene que
                // encenderse donde golpea, no lavar el monitor.
                float core = pow(saturate(1.0 - r / 0.40), 3.2);
                float flash = _Flash * core;

                // El nucleo satura a blanco: es la chispa, no el color del personaje.
                float3 col = _HotColor.rgb * flash * 1.45
                           + _Color.rgb * (lines * 1.75 + ring * 1.35)
                           + _HotColor.rgb * core * _Flash * 0.9;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
