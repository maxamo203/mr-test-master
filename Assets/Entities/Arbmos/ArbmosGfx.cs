using UnityEngine;

// Gfx procedural compartido del Arbmos: textura redonda suave + quad + material de
// particula. Lo usan el humo (ArbmosSmokeAura) y los ojos (ArbmosEye). Todo generado por
// codigo (sin assets de imagen), cacheado y compartido entre instancias.
internal static class ArbmosGfx
{
    private static Texture2D _roundTex;
    private static Mesh      _quadMesh;

    // Textura radial suave (blanco con alfa que cae del centro al borde) => particulas /
    // orbes REDONDOS en vez del cuadrado duro del quad.
    public static Texture2D RoundTexture()
    {
        if (_roundTex != null) return _roundTex;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; // 0 centro .. 1 borde
            float a = Mathf.Clamp01(1f - d);
            a = a * a;                                     // caida mas suave (redondo difuso)
            px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        _roundTex = tex;
        return _roundTex;
    }

    // Quad unitario centrado en XY (normal +Z), para los orbes. Sin GameObject.CreatePrimitive
    // (evita el collider implicito que puede fallar bajo IL2CPP).
    public static Mesh QuadMesh()
    {
        if (_quadMesh != null) return _quadMesh;
        var mesh = new Mesh { name = "ArbmosQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
        };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        _quadMesh = mesh;
        return _quadMesh;
    }

    // Material de particula/orbe con la textura redonda. Shader defensivo: en device los
    // materiales runtime salen magenta si el shader no esta en "Always Included" (ver
    // memoria del proyecto); de ahi la cadena de fallbacks. Solo shaders con Blend
    // SrcAlpha (respetan el alfa => redondo); se evita Mobile/Particles/Additive (Blend
    // One One: ignora el alfa => se veria cuadrado).
    public static Material ParticleMaterial(bool additive, Color tint)
    {
        Shader sh = null;
        if (additive) sh = Shader.Find("Legacy Shaders/Particles/Additive");
        if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (sh == null) sh = Shader.Find("Mobile/Particles/Alpha Blended");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");

        var m = new Material(sh) { name = additive ? "ArbmosEyeMat" : "ArbmosSmokeMat" };
        m.mainTexture = RoundTexture();
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
        if (m.HasProperty("_Color"))     m.SetColor("_Color", tint);
        return m;
    }
}
