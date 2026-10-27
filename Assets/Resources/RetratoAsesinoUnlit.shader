// Retrato del asesino (LocalDeath) para entidades SIN iluminación, como Veleth
// (AR/VelethReveal). Mismo truco que RetratoAsesino.shader para dibujarse por encima de
// todo: la primera pasada "borra" la profundidad en la huella de la malla y después se
// dibuja normal. En Resources para que entre al build (Resources.Load).
//
// La base es la textura tal cual (como se ve en partida, va por Emission) y ENCIMA se suman
// las luces de Unity — la blanca de la cámara y la roja de abajo, con su parpadeo —, con
// las mismas reglas de intensidad que el retrato iluminado (en espacio lineal Unity eleva la
// intensidad de las luces a 2.2; emularlas a mano no lo respetaba y la roja no se notaba).
//
// LocalDeath.MaterialesRetrato le pasa la textura y el tinte del original (que pueden
// llamarse _BaseMap / _BaseColor) en _MainTex / _Color.
Shader "Hidden/Mortuorium/RetratoAsesinoUnlit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" "IgnoreProjector"="True" }

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

        // Pasadas 2+: textura sin luz (Emission) + cada luz de Unity como Lambert sumado.
        ZTest LEqual
        ZWrite On

        CGPROGRAM
        #pragma surface surf Retrato noshadow nofog noambient nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;

        struct Input
        {
            float2 uv_MainTex;
        };

        half4 LightingRetrato(SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndl = saturate(dot(s.Normal, lightDir));
            return half4(s.Albedo * _LightColor0.rgb * (ndl * atten), 1);
        }

        void surf(Input input, inout SurfaceOutput output)
        {
            fixed3 albedo = tex2D(_MainTex, input.uv_MainTex).rgb * _Color.rgb;
            output.Albedo = albedo;
            output.Emission = albedo;
            output.Alpha = 1;
        }
        ENDCG
    }
}
