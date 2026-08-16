using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Aparicion procedural: evita depender de un modelo temporal y de CreatePrimitive
// (que puede fallar con modulos de fisica eliminados por IL2CPP). La silueta nace de
// una malla liviana, translucida y siempre disponible en Android/iOS.
[RequireComponent(typeof(VelethEntity))]
public class VelethGfx : MonoBehaviour
{
    private const string ShaderResource = "VelethApparition";
    private Mesh _mesh;
    private Material _material;

    private void Awake()
    {
        var visual = new GameObject("AparicionVeleth");
        visual.transform.SetParent(transform, false);

        _mesh = BuildWraithMesh(14);
        var filter = visual.AddComponent<MeshFilter>();
        filter.sharedMesh = _mesh;

        var renderer = visual.AddComponent<MeshRenderer>();
        var shader = Resources.Load<Shader>(ShaderResource);
        if (shader == null) shader = Shader.Find("AR/VelethApparition");
        if (shader == null)
        {
            Debug.LogError("[Veleth] No se encontro el shader de la aparicion.");
            renderer.enabled = false;
            return;
        }

        _material = new Material(shader)
        {
            name = "Veleth (runtime)",
            hideFlags = HideFlags.DontSave,
        };
        _material.SetColor("_Color", new Color(0.005f, 0.008f, 0.012f, 0.88f));
        _material.SetColor("_EdgeColor", new Color(0.35f, 0.015f, 0.02f, 1f));
        renderer.sharedMaterial = _material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        CreateEyes(visual.transform, shader);
    }

    private void Update()
    {
        if (_material == null) return;
        var entity = GetComponent<VelethEntity>();
        _material.SetFloat("_Threat", entity.State == VelethState.Grabbing ? 1f : 0f);
    }

    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_material != null) Destroy(_material);
    }

    private static Mesh BuildWraithMesh(int segments)
    {
        float[] heights = { 0f, 0.55f, 1.2f, 1.75f, 2.08f };
        float[] radii   = { 0.18f, 0.43f, 0.34f, 0.28f, 0.06f };
        var vertices = new List<Vector3>(segments * heights.Length);
        var normals = new List<Vector3>(vertices.Capacity);
        var uv = new List<Vector2>(vertices.Capacity);
        var triangles = new List<int>((heights.Length - 1) * segments * 6);

        for (int ring = 0; ring < heights.Length; ring++)
        for (int segment = 0; segment < segments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / segments;
            float wobble = ring == 1 ? 1f + 0.12f * Mathf.Sin(angle * 3f) : 1f;
            var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices.Add(radial * radii[ring] * wobble + Vector3.up * heights[ring]);
            normals.Add(radial);
            uv.Add(new Vector2(segment / (float)segments, heights[ring] / heights[^1]));
        }

        for (int ring = 0; ring < heights.Length - 1; ring++)
        for (int segment = 0; segment < segments; segment++)
        {
            int next = (segment + 1) % segments;
            int a = ring * segments + segment;
            int b = ring * segments + next;
            int c = (ring + 1) * segments + segment;
            int d = (ring + 1) * segments + next;
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(b); triangles.Add(c); triangles.Add(d);
        }

        var mesh = new Mesh { name = "VelethWraith" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CreateEyes(Transform parent, Shader shader)
    {
        var eyes = new GameObject("OjosVeleth");
        eyes.transform.SetParent(parent, false);
        eyes.transform.localPosition = new Vector3(0f, 1.78f, 0.245f);

        var mesh = new Mesh { name = "VelethEyes" };
        mesh.vertices = new[]
        {
            new Vector3(-0.13f, -0.025f, 0f), new Vector3(-0.035f, -0.015f, 0f),
            new Vector3(-0.04f,  0.03f, 0f),  new Vector3(-0.12f,  0.025f, 0f),
            new Vector3( 0.035f,-0.015f, 0f), new Vector3( 0.13f, -0.025f, 0f),
            new Vector3( 0.12f,  0.025f, 0f), new Vector3( 0.04f,  0.03f, 0f),
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var filter = eyes.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = eyes.AddComponent<MeshRenderer>();
        var material = new Material(shader)
        {
            name = "Ojos Veleth (runtime)",
            hideFlags = HideFlags.DontSave,
        };
        material.SetColor("_Color", new Color(0.65f, 0.005f, 0.005f, 1f));
        material.SetColor("_EdgeColor", new Color(1f, 0.04f, 0.01f, 1f));
        material.SetFloat("_Threat", 1f);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }
}

public static class VelethPresentation
{
    private static AudioClip _invocationClip;

    // La invocacion sale del AudioCatalog como cualquier otro sonido del juego. Si ese slot
    // esta vacio (hoy el proyecto no tiene ni un archivo de audio) se cae al clip
    // sintetizado por codigo que Veleth ya tenia, para no perder el unico sonido que habia.
    public static void PlayInvocation(Vector3 position)
    {
        var cat = AudioManager.Catalogo;
        if (cat != null && cat.velethInvocacion != null && !cat.velethInvocacion.Vacia)
        {
            AudioManager.Sonar(c => c.velethInvocacion, position);
            return;
        }

        if (_invocationClip == null) _invocationClip = BuildInvocationClip();
        AudioSource.PlayClipAtPoint(_invocationClip, position, GameOptions.VolumenEfectos);
    }

    private static AudioClip BuildInvocationClip()
    {
        const int sampleRate = 22050;
        const float duration = 2.4f;
        int count = Mathf.CeilToInt(sampleRate * duration);
        var samples = new float[count];
        uint noise = 0x6D2B79F5u;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Sin(Mathf.Clamp01(t / 0.08f) * Mathf.PI * 0.5f) *
                             Mathf.Pow(1f - t / duration, 1.7f);
            float descending = Mathf.Lerp(170f, 42f, t / duration);
            noise = noise * 1664525u + 1013904223u;
            float n = ((noise >> 8) & 0xFFFF) / 32767.5f - 1f;
            float tone = Mathf.Sin(Mathf.PI * 2f * descending * t) * 0.55f +
                         Mathf.Sin(Mathf.PI * 2f * descending * 0.49f * t) * 0.3f;
            samples[i] = Mathf.Clamp((tone + n * 0.18f) * envelope, -1f, 1f);
        }

        var clip = AudioClip.Create("Invocacion de Veleth", count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}

