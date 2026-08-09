Shader "AR/TensionDistortion"
{
    // Distorsion de lente (barrel, radial desde el centro de pantalla) + aberracion
    // cromatica, aplicada a lo que YA se renderizo (GrabPass) — no hay post-process/URP
    // en el proyecto (pipeline built-in). _TensionDistort (0..1) lo publica TensionSystem.
    Properties { }
    SubShader
    {
        // Transparent+150: despues de DarknessOverlay (Transparent+100), asi la
        // distorsion tambien deforma la oscuridad/cono de linterna ya compuestos.
        Tags { "Queue"="Transparent+150" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_TensionGrab" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TensionGrab;
            uniform float _TensionDistort;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos    : SV_POSITION;
                float4 uvgrab : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.uvgrab = ComputeGrabScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv       = i.uvgrab.xy / i.uvgrab.w;
                float2 centered = uv - 0.5;
                float  r2       = dot(centered, centered);

                // Barrel: cuanto mas lejos del centro y mas tension, mas se empuja el uv.
                float2 warped = uv + centered * r2 * (_TensionDistort * 0.35);

                // Aberracion cromatica: cada canal se resuelve con un offset radial propio.
                float2 dir = centered * rsqrt(max(r2, 1e-6));
                float  ca  = _TensionDistort * 0.010;
                fixed  r   = tex2D(_TensionGrab, warped + dir * ca).r;
                fixed  g   = tex2D(_TensionGrab, warped).g;
                fixed  b   = tex2D(_TensionGrab, warped - dir * ca).b;

                return fixed4(r, g, b, 1);
            }
            ENDCG
        }
    }
}
