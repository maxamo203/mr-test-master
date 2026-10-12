Shader "AR/DarknessOverlay"
{
    Properties { }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            uniform float4 _FlashlightDir;
            uniform float  _FlashlightIntensity;
            uniform float  _FlashlightFlicker;
            uniform float  _OverlayDarkness;

            uniform float  _OverlayConeTan;    // tan del radio angular base del agujero
            uniform float  _OverlaySoftness;   // ancho del degradé del borde (fraccion del radio)
            uniform float  _OverlaySpikes;     // largo de las puntas (fraccion del radio)
            uniform float  _OverlayRays;       // 0..1 cuanto aclaran los rayos sueltos

            // Cono con halos (Arbmos V2): lo publica Flashlight.
            uniform float _FlashlightCosOuter;
            uniform float _FlashlightCosInner;
            uniform float _FlashlightCosHalo;
            uniform float _FlashlightHaloStrength;
            uniform float _FlashlightCosMidHalo;
            uniform float _FlashlightMidHaloStrength;
            uniform float _FlashlightCosFarHalo;
            uniform float _FlashlightFarHaloStrength;

            // 1 = cono con halos (Rediseño 2.0 prendido), 0 = viñeta con puntas (como en
            // main). Lo publica DarknessOverlay según Rediseno.Flag; es uniforme para todo
            // el draw, así que la rama no divide warps.
            uniform float _OverlayHalos;

            #define TWO_PI 6.28318530718

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            // Value noise 1D PERIODICO sobre el angulo polar u (0..1): n celdas que dan la
            // vuelta completa, asi no hay costura en u = 0/1. 'shift' (en celdas) lo hace
            // girar despacio para que las puntas no queden congeladas.
            float ringNoise(float u, float n, float seed, float shift)
            {
                float x = u * n + shift;
                float i = floor(x);
                float f = frac(x);
                float a = hash11(i       - n * floor( i      / n) + seed);
                float b = hash11((i + 1) - n * floor((i + 1) / n) + seed);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(a, b, f);
            }

            // Viñeta con puntas: agujero que se cierra con la batería (DarknessOverlay).
            float AlphaVineta(v2f i)
            {
                float alpha = _OverlayDarkness;

                if (_FlashlightIntensity > 0.001)
                {
                    float3 viewRay = normalize(i.worldPos - _WorldSpaceCameraPos);
                    float3 dir     = _FlashlightDir.xyz;
                    float  c       = dot(viewRay, dir);

                    float hole = 0.0;
                    if (c > 0.0)
                    {
                        // Distancia al eje del haz en espacio "tangente" (radio 1 = borde
                        // base). Evita acos y es estable en todo el rango util (< 90°).
                        float3 perp = viewRay - dir * c;
                        float  r    = length(perp) / (c * _OverlayConeTan);

                        // Angulo polar alrededor del haz, medido con los ejes de la camara
                        // (por ojo en Cardboard) para que el patron no "nade" al girar.
                        float3 camRight = UNITY_MATRIX_V[0].xyz;
                        float3 camUp    = UNITY_MATRIX_V[1].xyz;
                        float  phi = atan2(dot(perp, camUp), dot(perp, camRight) + 1e-5);
                        float  u   = phi / TWO_PI + 0.5;

                        float t = _Time.y;
                        float s1 = ringNoise(u, 19.0,  3.0,  t * 0.15);
                        float s2 = ringNoise(u, 53.0, 17.0, -t * 0.22);
                        float s3 = ringNoise(u, 127.0, 41.0, t * 0.35);

                        // Borde irregular: ondas anchas + puntas finas. pow() afila los
                        // picos (puntas largas y delgadas en vez de lomas redondeadas).
                        float spikes = s1 * 0.45 + s2 * 0.30 + pow(s3, 3.0) * 0.6;
                        // Sesgado hacia adentro (media ~ -0.25): las puntas de oscuridad se
                        // meten en la luz en vez de agrandar el agujero fuera de pantalla.
                        float edge   = 1.0 + _OverlaySpikes * (spikes * 2.0 - 1.3);

                        hole = 1.0 - smoothstep(edge - _OverlaySoftness, edge + _OverlaySoftness, r);

                        // Rayos sueltos que se escapan hacia la oscuridad, apagandose
                        // con la distancia al borde.
                        float ray  = pow(ringNoise(u, 211.0, 89.0, t * 0.5), 8.0);
                        float fade = saturate(1.0 - (r - edge) / 0.9);
                        hole = max(hole, ray * fade * _OverlayRays);
                    }

                    // _FlashlightFlicker (0..1) es el titileo por bateria baja: un dip cierra
                    // parcialmente el agujero, no solo su brillo sobre la malla.
                    alpha *= 1.0 - saturate(hole) * _FlashlightFlicker;
                }
                return alpha;
            }

            // Cono liso con tres capas de halo (Flashlight).
            float AlphaHalos(v2f i)
            {
                float reveal = 0.0;
                if (_FlashlightIntensity > 0.001)
                {
                    float3 viewRay = normalize(i.worldPos - _WorldSpaceCameraPos);
                    float c = dot(viewRay, normalize(_FlashlightDir.xyz));
                    float core = smoothstep(_FlashlightCosOuter, _FlashlightCosInner, c);
                    float layer75 = pow(saturate(smoothstep(
                        _FlashlightCosHalo, _FlashlightCosOuter, c)), 1.2) *
                        saturate(_FlashlightHaloStrength);
                    float layer50 = pow(saturate(smoothstep(
                        _FlashlightCosMidHalo, _FlashlightCosHalo, c)), 1.3) *
                        saturate(_FlashlightMidHaloStrength);
                    float layer20 = pow(saturate(smoothstep(
                        _FlashlightCosFarHalo, _FlashlightCosMidHalo, c)), 1.45) *
                        saturate(_FlashlightFarHaloStrength);
                    reveal = max(core, max(layer75, max(layer50, layer20))) *
                             _FlashlightFlicker;
                }

                float alpha = _OverlayDarkness * (1.0 - saturate(reveal));
                return alpha;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float alpha = _OverlayHalos > 0.5 ? AlphaHalos(i) : AlphaVineta(i);
                return fixed4(0, 0, 0, alpha);
            }
            ENDCG
        }
    }
}
