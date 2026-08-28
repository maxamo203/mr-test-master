Shader "Mortuorium/Meshy Entity PBR"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0,2)) = 1
        _MetallicMap ("Metallic", 2D) = "black" {}
        _MetallicScale ("Metallic Scale", Range(0,1)) = 1
        _RoughnessMap ("Roughness", 2D) = "white" {}
        _RoughnessScale ("Roughness Scale", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _MetallicMap;
        sampler2D _RoughnessMap;
        fixed4 _Color;
        half _NormalStrength;
        half _MetallicScale;
        half _RoughnessScale;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 albedo = tex2D(_MainTex, input.uv_MainTex) * _Color;
            half metallic = tex2D(_MetallicMap, input.uv_MainTex).r;
            half roughness = tex2D(_RoughnessMap, input.uv_MainTex).r;

            output.Albedo = albedo.rgb;
            output.Alpha = 1;
            output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_MainTex), _NormalStrength);
            output.Metallic = saturate(metallic * _MetallicScale);
            output.Smoothness = saturate(1.0h - roughness * _RoughnessScale);
        }
        ENDCG
    }

    FallBack "Standard"
}
