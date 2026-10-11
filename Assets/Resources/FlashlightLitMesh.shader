Shader "Custom/FlashlightLitMesh"
{
    Properties { }
    SubShader
    {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
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

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 wnorm : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnorm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_FlashlightIntensity <= 0.001) return fixed4(0,0,0,1);
                float3 toFrag = i.wpos - _FlashlightPos.xyz;
                float dist = length(toFrag);
                float3 L = toFrag / max(dist, 1e-4);
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
                if (cone <= 0.001) return fixed4(0,0,0,1);

                float atten = saturate(1.0 - dist / max(_FlashlightRange, 0.01));
                float3 N = normalize(i.wnorm);
                float ndl = saturate(dot(N, -L));
                float lit = _FlashlightIntensity * cone * atten * (0.35 + 0.65 * ndl);
                return fixed4(_FlashlightColor.rgb * lit, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}