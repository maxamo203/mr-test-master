Shader "Mortuorium/OriginGizmo"
{
    // Built-in RP port. Unlit, always-on-top gizmo: ZTest Always ignores the depth buffer
    // for itself, and the vertex shader also pins its own depth to the near plane with
    // ZWrite On — belt and suspenders against AR environment occlusion, which composites
    // from the depth buffer on some ARFoundation backends.
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Overlay" "Queue" = "Overlay" }

        Pass
        {
            ZTest Always
            ZWrite On
            Cull Off

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 pos : SV_POSITION; };

            fixed4 _BaseColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // Near is z/w = 1 on reversed-Z platforms (Vulkan/Metal/D3D) and 0 or -1
                // elsewhere — the macro knows which.
                o.pos.z = o.pos.w * UNITY_NEAR_CLIP_VALUE;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return _BaseColor; }
            ENDCG
        }
    }
}
