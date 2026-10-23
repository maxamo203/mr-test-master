// Humo y haces de luz de la escena del banner (BannerEntidadesSetup). Igual que los
// shaders de partícula legacy (color = 2 × tinte × textura) pero con:
//   - soft particles siempre activas: el plano se desvanece al acercarse a lo que tiene
//     detrás (piso, criaturas, telón), así no se ve la línea recta donde lo corta;
//   - desvanecido cerca de la cámara, para que un plano que pasa por delante no tape.
// Necesita la textura de profundidad de la cámara (la pide BannerCamaraFX). Vive fuera de
// Resources y sólo lo usan materiales de Assets/Promo: no viaja al build.
Shader "Mortuorium/Banner Niebla"
{
    Properties
    {
        _MainTex ("Textura", 2D) = "white" {}
        _TintColor ("Tinte", Color) = (0.5, 0.5, 0.5, 0.5)
        _Suavidad ("Suavidad contra objetos (m)", Range(0.01, 3)) = 0.8
        _CercaInicio ("Cámara: invisible hasta (m)", Range(0, 3)) = 0.4
        _CercaRango ("Cámara: aparece en (m)", Range(0.01, 4)) = 1.2
        [Toggle] _Aditivo ("Aditivo (luz) / mezcla (humo)", Float) = 1
        [HideInInspector] _SrcBlend ("", Float) = 5
        [HideInInspector] _DstBlend ("", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TintColor;
            float _Suavidad;
            float _CercaInicio;
            float _CercaRango;
            float _Aditivo;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 proj : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.proj = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.proj.z);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = 2.0 * _TintColor * tex2D(_MainTex, i.uv);

                float escena = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.proj)));
                float contraObjetos = saturate((escena - i.proj.z) / _Suavidad);
                float contraCamara = saturate((i.proj.z - _CercaInicio) / _CercaRango);
                c.a *= contraObjetos * contraCamara;

                // La luz aditiva se apaga con la niebla (a negro); el humo se funde en ella.
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, lerp(unity_FogColor, fixed4(0, 0, 0, 0), _Aditivo));
                return c;
            }
            ENDCG
        }
    }
}
