Shader "AR/Occluder"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background+1" }

        Pass
        {
            Name "Occluder"
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(0,0,0,0); }
            ENDCG
        }

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
            uniform float _FlashlightRange;
            uniform float _FlashlightCosOuter;
            uniform float _FlashlightCosInner;
            uniform float _FlashlightCosHalo;
            uniform float _FlashlightHaloStrength;
            uniform float _FlashlightCosMidHalo;
            uniform float _FlashlightMidHaloStrength;
            uniform float _FlashlightCosFarHalo;
            uniform float _FlashlightFarHaloStrength;
            uniform float _FlashlightIntensity;
            uniform float4 _FlashlightColor;
            uniform float _DarknessAmount;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; float3 worldNormal : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float falloff = 0.0;
                if (_FlashlightIntensity > 0.001)
                {
                    float3 toFrag = i.worldPos - _FlashlightPos.xyz;
                    float dist = length(toFrag);
                    if (dist < _FlashlightRange)
                    {
                        float3 L = toFrag / max(dist, 0.0001);
                        float c = dot(L, normalize(_FlashlightDir.xyz));
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
                        float cone = max(core, max(layer75, max(layer50, layer20)));
                        float atten = saturate(1.0 - dist / _FlashlightRange);
                        atten *= atten;
                        float ndotl = saturate(dot(normalize(i.worldNormal), -L));
                        falloff = saturate(cone * atten * ndotl * _FlashlightIntensity);
                    }
                }

                float3 dark = float3(_DarknessAmount, _DarknessAmount, _DarknessAmount);
                return fixed4(lerp(dark, _FlashlightColor.rgb, falloff), 1.0);
            }
            ENDCG
        }
    }
}