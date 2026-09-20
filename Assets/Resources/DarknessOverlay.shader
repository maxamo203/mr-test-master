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
            uniform float  _FlashlightCosOuter;
            uniform float  _FlashlightCosInner;
            uniform float  _FlashlightIntensity;
            uniform float  _FlashlightFlicker;
            uniform float  _OverlayDarkness;
            uniform float4 _FlashlightColor;

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

            fixed4 frag (v2f i) : SV_Target
            {
                float3 viewRay = normalize(i.worldPos - _WorldSpaceCameraPos);
                float coneFactor = 0.0;
                if (_FlashlightIntensity > 0.001)
                {
                    float coneCos = dot(viewRay, _FlashlightDir.xyz);
                    // _FlashlightFlicker (0..1) es el titileo por batería baja: un dip cierra
                    // parcialmente el agujero de la linterna, no sólo su brillo sobre la malla.
                    coneFactor = smoothstep(_FlashlightCosOuter, _FlashlightCosInner, coneCos) * _FlashlightFlicker;
                }
                float alpha = _OverlayDarkness * (1.0 - coneFactor);
                return fixed4(0, 0, 0, alpha);
            }
            ENDCG
        }
    }
}
