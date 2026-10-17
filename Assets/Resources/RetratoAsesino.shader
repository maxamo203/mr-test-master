// Retrato del asesino al morir (LocalDeath): la copia congelada se dibuja POR ENCIMA de
// todo — profundidad real de ARKit/ARCore (AROcclusionManager), paredes escaneadas
// (SceneOccluderMode) y planos AR —, que si no la tapaban apenas había una superficie real
// más cerca que ella (te atrapan contra una pared, junto a un mueble…).
//
// Dos pasadas por renderer: la primera "borra" la profundidad en la huella de la malla
// (escribe el plano lejano, ZTest Always) y la segunda dibuja normal (ZTest LEqual), así la
// malla se ocluye bien a sí misma pero nada del mundo la tapa. Cola Overlay: va después de
// la escena y del fondo de cámara. En Resources para que entre al build (Resources.Load).
//
// Mismas propiedades que Mortuorium/Meshy Entity PBR: el material se arma copiando las
// del original (Material.CopyPropertiesFromMaterial).
Shader "Hidden/Mortuorium/RetratoAsesino"
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
        Tags { "RenderType"="Opaque" "Queue"="Overlay" "IgnoreProjector"="True" }
        LOD 300

        // Pasada 1: profundidad al plano lejano en la huella de la malla.
        Pass
        {
            ZTest Always
            ZWrite On
            ColorMask 0
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            float frag(float4 pos : SV_POSITION) : SV_Depth
            {
            #if defined(UNITY_REVERSED_Z)
                return 0.0;
            #else
                return 1.0;
            #endif
            }
            ENDCG
        }

        // Pasada 2: la superficie, con la misma iluminación que el shader de las entidades.
        ZTest LEqual
        ZWrite On

        CGPROGRAM
        #pragma surface surf Standard noshadow nofog
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
}
