// Glow analitico para los impactos. Una sola familia de material cubre las tres
// formas que necesita un golpe:
//
//   _Shape 0  ELIPSE  nucleo, halo, estela (elipse estirada), columna vertical.
//   _Shape 1  ANILLO  frente de onda circular.
//   _Shape 2  ARCO    medialuna de barrido para patadas.
//
// Por que analitico y no textura: el quad aditivo se lee por `alfa x intensidad`.
// Con intensidades de 3-7 (necesarias para que el nucleo queme y alimente el bloom)
// el alfa de las esquinas de una textura (1e-3) sigue siendo visible y el quad
// aparece como un CUADRADO de luz. Aqui el alfa cae a 0 de forma exacta a cualquier
// intensidad, y el borde de un anillo o un arco queda limpio a cualquier tamano.
//
// `_Ellipse` deforma el espacio antes de medir: (1,1) punto redondo, (0.85,2.05)
// chispa alargada, (0.32,3.0) columna vertical.
Shader "KOM/ImpactGlow"
{
    Properties
    {
        _Color     ("Color", Color) = (1,1,1,1)
        _HotColor  ("Hot Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 2
        _Power     ("Falloff Power", Float) = 3
        _Ellipse   ("Ellipse", Vector) = (1,1,0,0)
        _Core      ("Core Mix", Range(0,1)) = 0.5
        _Shape     ("Shape", Float) = 0
        _Radius    ("Ring Radius", Float) = 0.86
        _Width     ("Ring Width", Float) = 0.09
        _ArcSpan   ("Arc Span (rad)", Float) = 1.75
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ImpactGlow"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _HotColor;
                float _Intensity;
                float _Power;
                float4 _Ellipse;
                float _Core;
                float _Shape;
                float _Radius;
                float _Width;
                float _ArcSpan;
            CBUFFER_END

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

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                p *= _Ellipse.xy;
                float r = length(p);

                float a;
                if (_Shape < 0.5)
                {
                    // Elipse: caida radial pura.
                    a = pow(saturate(1.0 - r), _Power);
                }
                else if (_Shape < 1.5)
                {
                    // Anillo: banda radial con el borde suave.
                    float d = abs(r - _Radius);
                    a = pow(saturate(1.0 - d / _Width), _Power);
                }
                else
                {
                    // Arco: banda radial recortada en angulo (abre hacia +X). El
                    // grosor se AFINA en las puntas: una medialuna de grosor
                    // constante se lee como un anillo roto, no como un tajo.
                    float ang = atan2(p.y, p.x);
                    float da = abs(atan2(sin(ang - 3.14159265), cos(ang - 3.14159265)));
                    float arc = saturate(1.0 - da / _ArcSpan);
                    float w = _Width * (0.28 + 0.72 * arc);
                    float band = pow(saturate(1.0 - abs(r - _Radius) / max(w, 0.001)), _Power);
                    a = band * arc * arc;
                }

                // El centro va a blanco caliente: la chispa es luz, no el color del
                // personaje. En anillos y arcos el nucleo no aplica.
                float coreMix = _Shape < 0.5 ? saturate(pow(saturate(1.0 - r), _Power * 1.7) * _Core) : 0.0;
                float3 col = lerp(_Color.rgb, _HotColor.rgb, coreMix);
                return half4(col * a * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
