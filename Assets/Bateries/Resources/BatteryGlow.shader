// Glow radial unlit para las pilas. Es un billboard (quad que mira a la camara) con
// blend aditivo: emite su propio color, se ve en oscuridad total y NO depende de que
// haya una superficie donde "impacte" la luz. Muy barato: sin iluminacion, sin sombras.
//
// Recorte de piso: _FloorY es la Y (world) del piso de referencia; los fragmentos por
// debajo se descartan (clip), asi el halo no "chorrea" por debajo del piso aunque no
// exista una malla de piso. La oclusion por paredes la maneja el C# (linecast).
//
// Va en Resources para garantizar que entre al build (Resources.Load), evitando el
// magenta por shader stripping en device.
Shader "Custom/BatteryGlow"
{
    Properties
    {
        _Color     ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1.6
        _Softness  ("Softness", Range(0.01, 1)) = 0.55
        _Alpha     ("Alpha (fade)", Range(0, 1)) = 1
        _FloorY    ("Floor Y (world)", Float) = -100000
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha One   // aditivo: suma luz (glow)
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float  worldY : TEXCOORD1;
            };

            fixed4 _Color;
            float  _Intensity;
            float  _Softness;
            float  _Alpha;
            float  _FloorY;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                o.worldY = mul(unity_ObjectToWorld, v.vertex).y;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Recorte de piso: descartar lo que este por debajo de la Y de referencia.
                clip(i.worldY - _FloorY);

                // Distancia radial desde el centro del quad (uv 0..1 => 0 centro, 1 borde).
                float2 d = i.uv - 0.5;
                float  r = saturate(length(d) * 2.0);
                float  a = saturate(1.0 - r);
                a = pow(a, 1.0 / max(_Softness, 0.01)); // caida suave

                fixed4 col = _Color;
                col.rgb *= _Intensity;
                col.a    = a * _Color.a * _Alpha; // _Alpha = fundido de oclusion
                return col;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
