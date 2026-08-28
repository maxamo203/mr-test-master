Shader "AR/CameraFX"
{
    // Efecto de camara de pantalla completa, aplicado a lo que YA se renderizo (GrabPass)
    // — no hay post-process/URP en el proyecto (pipeline built-in). Fusiona DOS historias
    // en un unico pase para no pagar dos copias de framebuffer por ojo:
    //
    //  US-11.2 (tension): distorsion de lente (barrel) + aberracion cromatica, escaladas
    //          por _TensionDistort (0..1) que publica TensionSystem.
    //  US-11.1 (VHS / camara antigua): scanlines, grano, bandas de tracking, jitter de
    //          linea, viñeta y tinte/desaturacion. Intensidades por ingrediente en
    //          _VhsAmount/_VhsColor, que publica VHSSettings (0 = ingrediente apagado).
    //
    // Los ingredientes VHS tienen una BASE constante (la US pide "tension constante") y
    // ademas se refuerzan con la tension via _peak: en los momentos criticos la cinta se
    // rompe mas. Con todos los amounts en 0 y tension 0 el frag devuelve la imagen
    // original tal cual (el quad igual se apaga desde CameraFXOverlay: sin efecto no se
    // paga el GrabPass).
    Properties { }
    SubShader
    {
        // Transparent+150: despues de DarknessOverlay (Transparent+100), asi el efecto
        // tambien deforma la oscuridad/cono de linterna ya compuestos.
        Tags { "Queue"="Transparent+150" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_CameraFXGrab" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _CameraFXGrab;

            uniform float  _TensionDistort;   // 0..1 (TensionSystem)
            // x = scanlines, y = grano, z = bandas de tracking, w = jitter de linea
            uniform float4 _VhsAmount;
            // x = viñeta, y = tinte/desaturacion, z = refuerzo por tension, w = libre
            uniform float4 _VhsColor;
            uniform float4 _VhsTintColor;     // rgb del tinte (cinta vieja)

            // Cantidad de lineas del barrido. Fija: atarla a la resolucion real hace
            // aliasing feo en pantallas densas.
            #define VHS_SCANLINES 320.0

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

            // Hash barato (sin texturas de ruido): suficiente para grano y jitter.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv   = i.uvgrab.xy / i.uvgrab.w;
                float  t    = _TensionDistort;
                float  time = _Time.y;

                // Los ingredientes VHS suben con la tension sobre su base constante.
                float peak = 1.0 + t * _VhsColor.z;

                // ── Banda de tracking: franja que sube lentamente por la imagen. La
                //    distancia es toroidal (frac) para que entre por abajo sin salto.
                float bandPos  = frac(time * 0.11);
                float bandDist = abs(frac(uv.y - bandPos + 0.5) - 0.5);
                float band     = saturate(1.0 - bandDist / 0.055);
                band = band * band * _VhsAmount.z;

                // ── Jitter: corrimiento horizontal por FILA (la cinta no engancha bien).
                float row = floor(uv.y * 240.0);
                float jit = hash21(float2(row, floor(time * 22.0))) - 0.5;

                // ── Distorsion de lente (US-11.2): barrel radial desde el centro.
                float2 centered = uv - 0.5;
                float  r2       = dot(centered, centered);
                float2 warped   = uv + centered * r2 * (t * 0.35);

                // El jitter de linea y el arrastre de la banda son desplazamientos en x.
                warped.x += jit * _VhsAmount.w * 0.004 * peak + band * 0.018 * peak;

                // ── Aberracion cromatica (US-11.2), reforzada dentro de la banda.
                float2 dir = centered * rsqrt(max(r2, 1e-6));
                float  ca  = t * 0.010 + band * 0.004;
                float3 col;
                col.r = tex2D(_CameraFXGrab, warped + dir * ca).r;
                col.g = tex2D(_CameraFXGrab, warped).g;
                col.b = tex2D(_CameraFXGrab, warped - dir * ca).b;

                // ── Tinte + desaturacion (cinta vieja / camara antigua).
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, luma * _VhsTintColor.rgb, saturate(_VhsColor.y));

                // ── Scanlines: barrido horizontal que oscurece una de cada dos lineas.
                float sl = sin(uv.y * VHS_SCANLINES * 3.14159265) * 0.5 + 0.5;
                col *= 1.0 - sl * _VhsAmount.x * 0.22;

                // ── Grano de cinta: ruido blanco animado, sin textura.
                float n = hash21(uv * 1024.0 + frac(time) * 137.0);
                col += (n - 0.5) * _VhsAmount.y * 0.28 * peak;

                // ── Viñeta: bordes comidos, como una lente vieja.
                col *= 1.0 - saturate(r2 * 1.7) * _VhsColor.x * 0.85;

                // La banda de tracking ademas aclara (la cinta "quema" ahi).
                col += band * 0.10 * peak;

                return fixed4(saturate(col), 1);
            }
            ENDCG
        }
    }
}
