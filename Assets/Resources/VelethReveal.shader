Shader "AR/VelethReveal"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _RevealPlaneY ("Reveal Plane Y", Float) = 0
        _RevealActive ("Reveal Active", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            fixed4 _BaseColor;
            float _RevealPlaneY;
            float _RevealActive;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float worldY : TEXCOORD1;
            };

            v2f vert(appdata input)
            {
                v2f output;
                float4 worldPosition = mul(unity_ObjectToWorld, input.vertex);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.worldY = worldPosition.y;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                if (_RevealActive > 0.5) clip(input.worldY - _RevealPlaneY);
                return tex2D(_BaseMap, input.uv) * _BaseColor;
            }
            ENDCG
        }
    }
}
