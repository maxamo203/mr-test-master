Shader "AR/RitualBookDarkness"
{
    Properties
    {
        _DarknessColor ("Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _DarknessColor;
            float4 _DarknessCenterWorld;
            float _DarknessAmount;
            float _DarknessMaxRadius;
            float _DarknessSoftness;
            float _DarknessOpacity;
            float4 _DarknessAxisXWorld;
            float4 _DarknessAxisZWorld;
            float4 _DarknessHalfSizeWorld;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 halfSize = max(float2(0.0001, 0.0001), _DarknessHalfSizeWorld.xy);
                float3 delta = input.worldPosition - _DarknessCenterWorld.xyz;
                float2 normalizedPosition = abs(float2(
                    dot(delta, _DarknessAxisXWorld.xyz) / halfSize.x,
                    dot(delta, _DarknessAxisZWorld.xyz) / halfSize.y));

                // max(x,z) forma un rectangulo que conserva la proporcion del libro.
                // Su area crece con radius^2: sqrt(amount) hace que 50% logico sea
                // aproximadamente 50% de superficie negra, no 25% como antes.
                float normalizedDistance = max(normalizedPosition.x, normalizedPosition.y);
                float radius = sqrt(saturate(_DarknessAmount));
                float normalizedSoftness = max(0.0001, _DarknessSoftness / min(halfSize.x, halfSize.y));
                float radialMask = 1.0 - smoothstep(radius - normalizedSoftness,
                                                    radius + normalizedSoftness,
                                                    normalizedDistance);
                float visible = step(0.0001, _DarknessAmount);
                float alpha = radialMask * visible * _DarknessOpacity * _DarknessColor.a;
                return fixed4(_DarknessColor.rgb, alpha);
            }
            ENDCG
        }
    }
}
