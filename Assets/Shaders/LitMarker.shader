// Shader lit simple para las ESFERAS/marcadores del escaneo (Built-in Render
// Pipeline). Reemplaza a Unlit/Color en piso, handles de pared/cubo, esferas de
// calibracion, puertas y previews: agrega sombreado difuso + ambiente (ShadeSH9)
// + un reflejo especular (Blinn-Phong), asi las bolas se ven 3D y no planas.
//
// IMPORTANTE: un solo Pass / una sola variante (sin keywords), para que NO se
// stripee en el build de celular (a diferencia de "Standard", que sale magenta).
// Esta en Always Included Shaders (GraphicsSettings) para garantizar su inclusion.
Shader "Custom/LitMarker"
{
    Properties
    {
        _Color    ("Color", Color)            = (1, 1, 1, 1)
        _Gloss    ("Specular Power", Range(4, 128)) = 64
        _SpecInt  ("Specular Intensity", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Color;
            float  _Gloss;
            float  _SpecInt;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float3 wnormal : TEXCOORD0;
                float3 wpos    : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                o.wpos    = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.wnormal);
                float3 L = normalize(_WorldSpaceLightPos0.xyz); // directional principal
                float  ndl = saturate(dot(n, L));

                // Especular (reflejo) Blinn-Phong, solo donde hay luz directa.
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 H = normalize(L + V);
                float  spec = pow(saturate(dot(n, H)), _Gloss) * step(0.001, ndl);

                fixed3 ambient = ShadeSH9(float4(n, 1.0));        // ambiente (probe/skybox)
                fixed3 col = _Color.rgb * (ambient + _LightColor0.rgb * ndl)
                           + _LightColor0.rgb * spec * _SpecInt;  // brillo especular

                return fixed4(col, _Color.a);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
