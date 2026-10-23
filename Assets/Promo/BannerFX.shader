// Post-proceso de los renders promocionales (BannerCamaraFX.cs). Pipeline Built-in, sin
// paquete de post-processing: bloom por cadena de mips + composición (aberración,
// exposición, tonemapping ACES, contraste, saturación, virado sombras/luces, viñeta, grano).
// Vive fuera de Resources y sólo lo pide un componente editor-only: no viaja al build.
Shader "Hidden/Mortuorium/BannerFX"
{
    Properties { _MainTex ("", 2D) = "white" {} }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;

    float4 _Umbral;        // x umbral, y rodilla, z tope
    float _Bloom;
    float _Exposicion;
    float _Contraste;
    float _Saturacion;
    float4 _TintaSombras;
    float4 _TintaLuces;
    float _Virado;
    float _Vineta;
    float _VinetaCurva;
    float4 _ColorVineta;
    float _Aberracion;
    float _Grano;
    float _TamGrano;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    half3 Caja(float2 uv)
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
        return (tex2D(_MainTex, uv + d.xy).rgb + tex2D(_MainTex, uv + d.zy).rgb +
                tex2D(_MainTex, uv + d.xw).rgb + tex2D(_MainTex, uv + d.zw).rgb) * 0.25;
    }

    half3 Carpa(float2 uv)
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(1, 1, -1, 0);
        half3 s = tex2D(_MainTex, uv - d.xy).rgb;
        s += tex2D(_MainTex, uv - d.wy).rgb * 2;
        s += tex2D(_MainTex, uv - d.zy).rgb;
        s += tex2D(_MainTex, uv + d.zw).rgb * 2;
        s += tex2D(_MainTex, uv).rgb * 4;
        s += tex2D(_MainTex, uv + d.xw).rgb * 2;
        s += tex2D(_MainTex, uv + d.zy).rgb;
        s += tex2D(_MainTex, uv + d.wy).rgb * 2;
        s += tex2D(_MainTex, uv + d.xy).rgb;
        return s * (1.0 / 16);
    }

    // Umbral con rodilla suave: el brillo entra de a poco en vez de cortar en seco.
    half3 Prefiltro(half3 c)
    {
        half br = max(c.r, max(c.g, c.b));
        half suave = clamp(br - _Umbral.x + _Umbral.y, 0, 2 * _Umbral.y);
        suave = suave * suave / (4 * _Umbral.y + 1e-5);
        half aporte = max(suave, br - _Umbral.x) / max(br, 1e-5);
        return min(c * aporte, _Umbral.z);
    }

    half3 ACES(half3 x)
    {
        return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
    }

    half Luma(half3 c) { return dot(c, half3(0.2126, 0.7152, 0.0722)); }

    half4 FragPrefiltro(v2f i) : SV_Target { return half4(Prefiltro(Caja(i.uv)), 1); }
    half4 FragBajar(v2f i) : SV_Target { return half4(Caja(i.uv), 1); }
    half4 FragSubir(v2f i) : SV_Target { return half4(Carpa(i.uv), 1); }

    half4 FragComponer(v2f i) : SV_Target
    {
        float2 uv = i.uv;
        float2 dc = uv - 0.5;

        // Aberración cromática radial: nula en el centro, crece hacia los bordes.
        float2 corrimiento = dc * dot(dc, dc) * _Aberracion * 0.06;
        half3 c;
        c.r = tex2D(_MainTex, uv - corrimiento).r;
        c.g = tex2D(_MainTex, uv).g;
        c.b = tex2D(_MainTex, uv + corrimiento).b;

        c += tex2D(_BloomTex, uv).rgb * _Bloom;
        c = ACES(c * _Exposicion);

        // La gradación se hace en espacio gamma (perceptual) y se vuelve a lineal al final.
        c = pow(c, 1.0 / 2.2);
        c = saturate((c - 0.5) * _Contraste + 0.5);
        half l = Luma(c);
        c = lerp(l.xxx, c, _Saturacion);

        // Virado: tinta fría en las sombras, cálida en las luces, sin cambiar el brillo.
        half3 tinta = lerp(_TintaSombras.rgb, _TintaLuces.rgb, smoothstep(0.05, 0.75, l));
        c = lerp(c, c * tinta / max(Luma(tinta), 1e-3), _Virado);

        // Viñeta elíptica (sigue la forma del cuadro) hacia un color, no a negro puro.
        half d = length(dc) * 1.4142;
        half v = saturate(1 - _Vineta * pow(d, _VinetaCurva));
        c = lerp(_ColorVineta.rgb, c, v);

        // Grano fijo (la imagen es una foto); el tamaño escala con la resolución.
        float2 p = floor(uv * _MainTex_TexelSize.zw / _TamGrano);
        half ruido = frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
        c += ruido * _Grano * (1 - 0.6 * l);

        c = pow(saturate(c), 2.2);
        return half4(c, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragPrefiltro
        ENDCG }

        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragBajar
        ENDCG }

        Pass { Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragSubir
        ENDCG }

        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragComponer
        ENDCG }
    }
}
