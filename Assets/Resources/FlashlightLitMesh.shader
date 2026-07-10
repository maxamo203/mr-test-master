// Ilumina la malla del entorno (reconstruida por AR/LiDAR) con la linterna, en tiempo
// real. Usa las propiedades GLOBALES que Flashlight.cs ya publica cada frame
// (_FlashlightPos/Dir/Range/CosOuter/CosInner/Intensity/Color). Es aditivo: dentro del
// cono "pinta" luz sobre la superficie (revela su forma por la normal + distancia); fuera
// del cono no aporta nada (se ve el feed de la cámara). Barato: sin sombras ni luces reales.
//
// Va en Resources para que entre al build (Resources.Load) y no salga magenta por stripping.
Shader "Custom/FlashlightLitMesh"
{
    Properties { }

    SubShader
    {
        Tags { "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Blend One One    // aditivo: suma luz al feed
        ZWrite Off
        ZTest LEqual
        Cull Off         // la malla del LiDAR puede venir con winding inconsistente

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            uniform float4 _FlashlightPos;
            uniform float4 _FlashlightDir;
            uniform float  _FlashlightRange;
            uniform float  _FlashlightCosOuter;
            uniform float  _FlashlightCosInner;
            uniform float  _FlashlightIntensity;
            uniform float4 _FlashlightColor;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 pos   : SV_POSITION;
                float3 wpos  : TEXCOORD0;
                float3 wnorm : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.wpos  = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnorm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (_FlashlightIntensity <= 0.001) return fixed4(0, 0, 0, 1);

                float3 toFrag = i.wpos - _FlashlightPos.xyz;
                float  dist   = length(toFrag);
                float3 L      = toFrag / max(dist, 1e-4);

                // Cono de la linterna.
                float c    = dot(L, normalize(_FlashlightDir.xyz));
                float cone = smoothstep(_FlashlightCosOuter, _FlashlightCosInner, c);
                if (cone <= 0.0) return fixed4(0, 0, 0, 1);

                // Atenuación por distancia.
                float atten = saturate(1.0 - dist / max(_FlashlightRange, 0.01));

                // Sombreado por normal (superficies mirando a la linterna brillan más), con
                // un piso para que la superficie se vea aunque la normal no sea perfecta.
                float3 N   = normalize(i.wnorm);
                float  ndl = saturate(dot(N, -L));
                float  lit = _FlashlightIntensity * cone * atten * (0.35 + 0.65 * ndl);

                fixed3 col = _FlashlightColor.rgb * lit;
                return fixed4(col, 1); // aditivo: el alpha no se usa
            }
            ENDCG
        }
    }

    Fallback Off
}
