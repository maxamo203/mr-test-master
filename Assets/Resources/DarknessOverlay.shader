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
            uniform float _FlashlightCosOuter;
            uniform float _FlashlightCosInner;
            uniform float _FlashlightCosHalo;
            uniform float _FlashlightHaloStrength;
            uniform float _FlashlightCosMidHalo;
            uniform float _FlashlightMidHaloStrength;
            uniform float _FlashlightCosFarHalo;
            uniform float _FlashlightFarHaloStrength;
            uniform float _FlashlightIntensity;
            uniform float _FlashlightFlicker;
            uniform float _OverlayDarkness;
            uniform float4 _FlashlightColor;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
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
                return fixed4(0, 0, 0, alpha);
            }
            ENDCG
        }
    }
}