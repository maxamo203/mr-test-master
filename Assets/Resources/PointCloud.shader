// Shader para la nube de puntos LiDAR (MeshTopology.Points).
// PSIZE ajusta el tamano del punto (soportado en Metal y GLES3).
// Vive en Resources para que el build no lo stripee (ver memoria del proyecto:
// los shaders solo referenciados en runtime salen magenta si se stripean).
Shader "Custom/PointCloud"
{
    Properties
    {
        _Color     ("Color", Color) = (0.2, 1.0, 0.6, 1.0)
        _PointSize ("Point Size (px)", Range(1, 20)) = 6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Cull Off
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float  _PointSize;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos  : SV_POSITION;
                float  size : PSIZE;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos  = UnityObjectToClipPos(v.vertex);
                o.size = _PointSize;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Color"
}
