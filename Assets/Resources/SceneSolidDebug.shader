// Material opaco de diagnostico para paredes y obstaculos escaneados.
// Es visible, escribe profundidad y por eso deja de mostrar cualquier modelo 3D
// que realmente quede detras de la geometria desde la camara del jugador.
Shader "Hidden/SceneSolidDebug"
{
    Properties
    {
        _Color ("Surface Color", Color) = (0.12, 0.14, 0.16, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Blend Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
            };

            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Relieve leve para distinguir esquinas sin convertirlo en una vista
                // de debug transparente o wireframe.
                float3 n = normalize(i.normal);
                float light = 0.55 + 0.45 * saturate(dot(n, normalize(float3(0.35, 0.8, 0.25))));
                return fixed4(_Color.rgb * light, 1);
            }
            ENDCG
        }
    }
}
