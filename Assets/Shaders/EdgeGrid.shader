// Shader para paredes y cubos del escaneo (Built-in Render Pipeline).
//
// Hace tres cosas sobre un relleno semitransparente:
//   1) Remarca las ARISTAS del mesh (donde se encuentran dos caras) con un color
//      modificable (negro por defecto). Se detectan por la discontinuidad de la
//      normal entre caras vecinas (no necesita datos extra en el mesh).
//   2) Dibuja un GRID interno de lineas verticales y horizontales sobre las caras.
//      El espaciado (que tan cerca estan) es parametrizable en metros con
//      _GridSpacing.
//   3) Permite prender/apagar lineas horizontales y verticales por separado.
//
// El grid se calcula en OBJECT-space escalado a metros: asi las lineas quedan
// pegadas a la caja (no se deslizan cuando el SLAM corrige la pose del anchor) y
// el espaciado sigue siendo metrico, consistente entre paredes (transform
// identity) y cubos (escalados). Para un cubo, "vertical/horizontal" siguen los
// ejes locales del cubo (rotan con el). Estetica "fantasma": relleno tenue +
// lineas/aristas opacas.
Shader "Custom/EdgeGrid"
{
    Properties
    {
        _Color         ("Fill Color", Color)          = (0.8, 0.85, 0.95, 0.12)
        _EdgeColor     ("Edge Color", Color)          = (0, 0, 0, 1)
        _GridColor     ("Grid Line Color", Color)     = (0, 0, 0, 1)
        _GridSpacing   ("Grid Spacing (m)", Float)    = 0.25
        _GridLineWidth ("Grid Line Half-Width (m)", Float) = 0.004
        _EdgeWidth     ("Edge Sensitivity", Range(0.05, 3)) = 0.7
        _ShowHorizontal("Horizontal Lines (0/1)", Float) = 1
        _ShowVertical  ("Vertical Lines (0/1)", Float)   = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float3 gpos    : TEXCOORD0; // object-space metrico (metros)
                float3 onormal : TEXCOORD1; // normal object-space (ejes del grid)
                float3 wnormal : TEXCOORD2; // normal world-space (deteccion de aristas)
            };

            fixed4 _Color;
            fixed4 _EdgeColor;
            fixed4 _GridColor;
            float  _GridSpacing;
            float  _GridLineWidth;
            float  _EdgeWidth;
            float  _ShowHorizontal;
            float  _ShowVertical;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                // Escala (lossy) del objeto desde las columnas de la matriz, para
                // convertir el object-space normalizado del cubo a metros.
                float3 scale = float3(
                    length(unity_ObjectToWorld._m00_m10_m20),
                    length(unity_ObjectToWorld._m01_m11_m21),
                    length(unity_ObjectToWorld._m02_m12_m22));

                o.gpos    = v.vertex.xyz * scale;
                o.onormal = v.normal;
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            // Intensidad de linea (0..1) para una coordenada metrica: 1 cerca de un
            // plano de grid, con antialiasing por derivada de pantalla.
            float lineMask(float coord)
            {
                float m  = coord / max(_GridSpacing, 1e-4);
                float f  = frac(m);
                float d  = min(f, 1.0 - f) * _GridSpacing; // metros a la linea mas cercana
                float aa = fwidth(coord);                  // ~tamano de pixel en metros
                return 1.0 - smoothstep(_GridLineWidth, _GridLineWidth + aa + 1e-5, d);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 an = abs(normalize(i.onormal));

                // ── Grid interno (object-space metrico) ──
                float gx = lineMask(i.gpos.x);
                float gy = lineMask(i.gpos.y);
                float gz = lineMask(i.gpos.z);

                // Planos Y => lineas horizontales (visibles en caras no perpendiculares a Y).
                float horiz = gy * saturate(1.0 - an.y);
                // Planos X/Z => lineas verticales.
                float vert  = max(gx * saturate(1.0 - an.x), gz * saturate(1.0 - an.z));

                float grid = 0.0;
                if (_ShowHorizontal > 0.5) grid = max(grid, horiz);
                if (_ShowVertical   > 0.5) grid = max(grid, vert);

                // ── Aristas: discontinuidad de la normal world entre caras ──
                float edgeAmount = length(fwidth(normalize(i.wnormal)));
                float edge = smoothstep(_EdgeWidth * 0.5, _EdgeWidth, edgeAmount);

                // ── Composicion: relleno -> grid -> aristas (las aristas ganan) ──
                fixed4 col = _Color;

                float gA = grid * _GridColor.a;
                col.rgb = lerp(col.rgb, _GridColor.rgb, gA);
                col.a   = max(col.a, gA);

                float eA = edge * _EdgeColor.a;
                col.rgb = lerp(col.rgb, _EdgeColor.rgb, eA);
                col.a   = max(col.a, eA);

                return col;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Transparent"
}
