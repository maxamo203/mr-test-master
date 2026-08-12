Shader "AR/VelethApparition"
{
    Properties
    {
        _Color ("Color", Color) = (0.005, 0.008, 0.012, 0.88)
        _EdgeColor ("Edge", Color) = (0.35, 0.015, 0.02, 1)
        _Threat ("Threat", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            fixed4 _Color;
            fixed4 _EdgeColor;
            float _Threat;

            v2f vert(appdata v)
            {
                v2f o;
                float pulse = sin(_Time.y * (2.3 + _Threat * 5.0) + v.vertex.y * 7.0) *
                              (0.012 + _Threat * 0.018);
                v.vertex.xyz += v.normal * pulse;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float fresnel = pow(1.0 - saturate(abs(dot(normalize(i.worldNormal), i.viewDir))), 2.2);
                float bands = 0.86 + 0.14 * sin(i.uv.y * 75.0 - _Time.y * 9.0);
                fixed4 color = lerp(_Color, _EdgeColor, saturate(fresnel + _Threat * 0.35));
                color.a *= bands * saturate(i.uv.y * 5.0);
                return color;
            }
            ENDCG
        }
    }
}

