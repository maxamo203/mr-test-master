// Oclusor depth-only para las paredes/cubos/piso ESCANEADOS (SceneRegistry +
// FloorPoint). Escribe profundidad pero NO color: el objeto se vuelve invisible
// pero sigue tapando lo que queda detras (los Sorken). Lo usa SceneOccluderMode
// como material cuando se activa el "modo oclusion".
//
// IMPORTANTE: una sola variante (sin keywords) para que NO se stripee en el build
// de celular. Esta en Always Included Shaders (GraphicsSettings) porque solo se
// referencia por Shader.Find en runtime (ningun material en escena lo usa), asi
// que si no, no entraria al build.
//
// Queue=Background+1 (igual que AR/Occluder): renderiza DESPUES del AR Camera
// Background (que en iOS/Metal limpia el depth al blit) y ANTES de la geometria
// (el Sorken en Geometry=2000), asi su depth si frena al Sorken que quede detras.
Shader "Hidden/SceneOccluder"
{
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

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return fixed4(0, 0, 0, 0); }
            ENDCG
        }
    }
}
