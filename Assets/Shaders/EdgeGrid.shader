// Shader para paredes y cubos del escaneo (Built-in Render Pipeline).
//
// Hace tres cosas sobre un relleno semitransparente:
//   1) Remarca las ARISTAS de la caja. Para cubos usa deteccion geometrica en
//      object-space (un borde es donde DOS de los tres ejes locales llegan al
//      limite de la caja) => las aristas SIEMPRE estan, no dependen del angulo de
//      camara (_BoxEdges = 1). Para paredes (con huecos de puerta) usa la
//      discontinuidad de la normal entre caras (_BoxEdges = 0).
//   2) Dibuja un GRID interno de lineas verticales y horizontales sobre las caras,
//      espaciado parametrico en metros (_GridSpacing).
//   3) Permite prender/apagar horizontales y verticales por separado.
//
// COORDENADAS DEL GRID (clave para que las lineas "sigan" al objeto):
//   - _GridFromUV = 0 (cubos): el grid se calcula en OBJECT-space escalado a metros.
//     Como el object-space rota con el transform, las lineas siguen la rotacion del
//     cubo (incluida inclinacion).
//   - _GridFromUV = 1 (paredes): el grid usa las coordenadas (u,v,w) horneadas en el
//     mesh por WallMeshBuilder (u a lo largo de la base, v en altura, w en grosor).
//     Asi, si la pared esta inclinada (su base sube/baja), las lineas siguen esa
//     inclinacion en vez de quedar siempre horizontales en el mundo.
//   El eje .y de la coordenada del grid es SIEMPRE la "altura" (define horizontales);
//   .x y .z son los otros dos (definen verticales). La normal usada para enmascarar
//   que lineas se ven en cada cara viene en el mismo frame que la coordenada.
Shader "Custom/EdgeGrid"
{
    Properties
    {
        _Color         ("Fill Color", Color)          = (0.8, 0.85, 0.95, 0.12)
        _EdgeColor     ("Edge Color", Color)          = (0, 0, 0, 1)
        _GridColor     ("Grid Line Color", Color)     = (0, 0, 0, 1)
        _GridSpacing   ("Grid Spacing (m)", Float)    = 0.25
        _GridLineWidth ("Grid Line Half-Width (m)", Float) = 0.004
        _EdgeWidth     ("Edge Sensitivity (normal disc.)", Range(0.05, 3)) = 0.7
        _EdgeLineWidth ("Box Edge Half-Width (m)", Float) = 0.006
        _BoxEdges      ("Box Edges object-space (0/1)", Float) = 0
        _GridFromUV    ("Grid coords from baked UV (0/1)", Float) = 0
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
                float3 guvw   : TEXCOORD1; // metrico (u,v,w) horneado (paredes)
                float3 gnrm   : TEXCOORD2; // normal en frame (u,v,w) horneada (paredes)
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float3 gpos    : TEXCOORD0; // coordenada metrica del grid (objeto o uvw)
                float3 gnormal : TEXCOORD1; // normal en el frame del grid (enmascarado)
                float3 wnormal : TEXCOORD2; // normal world-space (aristas por discontinuidad)
                float3 ghalf   : TEXCOORD3; // media-extension de la caja (object-space, cubos)
            };

            fixed4 _Color;
            fixed4 _EdgeColor;
            fixed4 _GridColor;
            float  _GridSpacing;
            float  _GridLineWidth;
            float  _EdgeWidth;
            float  _EdgeLineWidth;
            float  _BoxEdges;
            float  _GridFromUV;
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

                float3 objMetric = v.vertex.xyz * scale;

                // Cubo => object-space metrico (rota con el cubo); pared => (u,v,w) horneado.
                o.gpos    = lerp(objMetric, v.guvw, _GridFromUV);
                o.gnormal = lerp(v.normal,  v.gnrm, _GridFromUV);
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                o.ghalf   = scale * 0.5;
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
                float3 an = abs(normalize(i.gnormal));

                // ── Grid interno ──
                float gx = lineMask(i.gpos.x);
                float gy = lineMask(i.gpos.y);
                float gz = lineMask(i.gpos.z);

                // Plano de altura (.y) => lineas horizontales (en caras no perpendiculares a .y).
                float horiz = gy * saturate(1.0 - an.y);
                // Planos .x/.z => lineas verticales.
                float vert  = max(gx * saturate(1.0 - an.x), gz * saturate(1.0 - an.z));

                float grid = 0.0;
                if (_ShowHorizontal > 0.5) grid = max(grid, horiz);
                if (_ShowVertical   > 0.5) grid = max(grid, vert);

                // ── Aristas (a): discontinuidad de la normal world entre caras (paredes) ──
                float edgeAmount = length(fwidth(normalize(i.wnormal)));
                float edgeN = smoothstep(_EdgeWidth * 0.5, _EdgeWidth, edgeAmount);

                // ── Aristas (b): geometricas en object-space (cubos) ──
                // Distancia metrica de cada eje al borde de la caja. En una arista DOS
                // ejes llegan al borde => la 2da menor distancia es ~0. Asi las 12
                // aristas siempre se dibujan, sin importar el angulo de camara.
                float3 dE   = i.ghalf - abs(i.gpos);
                float  dmin = min(dE.x, min(dE.y, dE.z));
                float  dmax = max(dE.x, max(dE.y, dE.z));
                float  dmid = dE.x + dE.y + dE.z - dmin - dmax; // la del medio
                float  aaE  = fwidth(dmid);
                float  boxEdge = (1.0 - smoothstep(_EdgeLineWidth, _EdgeLineWidth + aaE + 1e-5, dmid)) * _BoxEdges;

                float edge = max(edgeN, boxEdge);

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
