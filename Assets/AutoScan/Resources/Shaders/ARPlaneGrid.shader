Shader "Mortuorium/ARPlaneGrid"
{
    // Built-in RP port of the URP grid shader from the standalone scanner project.
    // Unlit translucent grid drawn over an AR plane. Offset pulls it toward the camera so
    // environment-depth occlusion does not swallow a plane sitting on the real floor.
    Properties
    {
        _GridColor ("Grid Color",  Color)              = (0.2, 0.6, 1.0, 1.0)
        _GridSize  ("Grid Size",   Float)              = 0.25
        _LineWidth ("Line Width",  Range(0.005, 0.15)) = 0.025
        _FillAlpha ("Fill Alpha",  Range(0.0,   0.4))  = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                // Local-space XZ — always the plane surface regardless of orientation
                float2 localXZ : TEXCOORD0;
            };

            fixed4 _GridColor;
            float  _GridSize;
            float  _LineWidth;
            float  _FillAlpha;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.localXZ = v.vertex.xz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 cell   = frac(i.localXZ / _GridSize);
                float2 edges  = min(cell, 1.0 - cell);
                float  onGrid = saturate(step(edges.x, _LineWidth) + step(edges.y, _LineWidth));
                float  alpha  = lerp(_FillAlpha, _GridColor.a, onGrid);
                return fixed4(_GridColor.rgb, alpha);
            }
            ENDCG
        }
    }
}
