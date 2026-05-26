Shader "AR/Occluder"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }

        // Pass 1 — oclusor: escribe profundidad pero no color
        Pass
        {
            Name "Occluder"
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return fixed4(0,0,0,0); }
            ENDCG
        }

        // Pass 2 — oscurecer y revelar con la linterna (multiplicativo)
        // Resultado: framebuffer *= lerp(oscuro, claro, falloff_linterna)
        Pass
        {
            Name "DarkenAndReveal"
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Back

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
            uniform float  _DarknessAmount; // 0 = pitch black, 1 = sin oscurecer

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float falloff = 0.0;

                if (_FlashlightIntensity > 0.001)
                {
                    float3 toFrag = i.worldPos - _FlashlightPos.xyz;
                    float dist = length(toFrag);
                    if (dist < _FlashlightRange)
                    {
                        float3 dirToFrag = toFrag / max(dist, 0.0001);
                        float spotCos = dot(dirToFrag, _FlashlightDir.xyz);
                        if (spotCos > _FlashlightCosOuter)
                        {
                            float spotFactor = smoothstep(_FlashlightCosOuter, _FlashlightCosInner, spotCos);
                            float attn = saturate(1.0 - dist / _FlashlightRange);
                            attn = attn * attn;
                            float3 n = normalize(i.worldNormal);
                            float ndotl = saturate(dot(n, -dirToFrag));
                            falloff = saturate(spotFactor * attn * ndotl * _FlashlightIntensity);
                        }
                    }
                }

                // multiplicador: oscuro fuera del cono, color de linterna dentro
                float3 dark = float3(_DarknessAmount, _DarknessAmount, _DarknessAmount);
                float3 lit  = _FlashlightColor.rgb;
                float3 mult = lerp(dark, lit, falloff);
                return fixed4(mult, 1.0);
            }
            ENDCG
        }
    }
}
