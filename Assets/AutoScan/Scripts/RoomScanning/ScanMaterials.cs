using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Materials for scanned geometry, and the one place that knows how to tint them.
    ///
    /// Scanned walls and furniture are meant to look like the reference game's, because
    /// they are the same objects seen in two apps: a wall scanned here is loaded there by
    /// <c>ScanLoader</c> and rebuilt as a <c>Scanner.WallObject</c>. If the two render
    /// differently, judging a scan against the game becomes guesswork.
    ///
    /// So the shared look is <b>Custom/EdgeGrid</b> — the game's shader, ported to URP in
    /// <c>Assets/Shaders/EdgeGrid.shader</c> — driven by the game's own material assets
    /// (<c>WallEdgeGrid.mat</c>, <c>CubeEdgeGrid.mat</c>), copied across unchanged.
    ///
    /// <para><b>Why this class exists.</b> Two shaders are now in play: EdgeGrid, whose
    /// fill colour is <c>_Color</c> and whose <c>_GridColor</c> is the (black) line colour,
    /// and Mortuorium's older <c>ARPlaneGrid</c>, whose <c>_GridColor</c> <em>is</em> the
    /// tint and whose fill is <c>_FillAlpha</c>. The same call has to mean the same thing
    /// on both, or a wall tinted "selected" would come back with black lines on one and a
    /// yellow body on the other. Every tint therefore goes through <see cref="Tint"/>.</para>
    /// </summary>
    public static class ScanMaterials
    {
        /// <summary>The game's shader, ported to URP. Also its name there, so
        /// <c>Shader.Find</c> and its material assets resolve identically.</summary>
        public const string EdgeGridShader = "Custom/EdgeGrid";

        /// <summary>Mortuorium's original grid shader. Still supported: it is what the
        /// AR planes use, and a scene that has not been re-wired still points at it.</summary>
        public const string PlaneGridShader = "Mortuorium/ARPlaneGrid";

        /// <summary>
        /// The game's resting wall fill — `WallEdgeGrid.mat`'s authored `_Color`. An
        /// automatically detected wall is drawn in exactly this, so "untouched wall here"
        /// and "wall in the game" are the same picture. The state colours below are
        /// Mortuorium's own: they mark facts the game has no concept of.
        /// </summary>
        public static readonly Color GameWallFill = new Color(0.80f, 0.85f, 0.95f, 0.12f);

        /// <summary>The game's selected tint (`WallObject.OnSelect`), at an alpha that
        /// still lets the real room show through the camera feed.</summary>
        public static readonly Color GameSelected = new Color(0.20f, 0.90f, 1f, 1f);

        /// <summary>
        /// Applies a state tint to a runtime material instance, whichever of the two
        /// shaders it uses.
        ///
        /// On EdgeGrid the tint is the fill and <paramref name="fillAlpha"/> its opacity;
        /// the black grid lines and edges are left as authored, because they are what make
        /// the surface readable. On ARPlaneGrid the tint is the grid colour and the fill
        /// is a separate alpha, which is what that shader means by the same two numbers.
        /// </summary>
        public static void Tint(Material material, Color color, float fillAlpha)
        {
            if (material == null) return;

            // _FillAlpha is the discriminator: only ARPlaneGrid has it, and it also has a
            // _GridColor, so testing for _GridColor first would take the wrong branch.
            if (material.HasProperty("_FillAlpha"))
            {
                if (material.HasProperty("_GridColor")) material.SetColor("_GridColor", color);
                material.SetFloat("_FillAlpha", fillAlpha);
                return;
            }

            var fill = new Color(color.r, color.g, color.b, fillAlpha);
            if (material.HasProperty("_Color")) material.SetColor("_Color", fill);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", fill);
        }

        /// <summary>
        /// A material to fall back to when no template is wired in the scene, or when the
        /// wired one turns out to have a stripped shader.
        /// </summary>
        public static Material CreateRuntime(ScanSurface surface)
        {
            var shader = Shader.Find(EdgeGridShader);
            if (shader != null)
            {
                var m = new Material(shader) { name = "EdgeGrid (runtime)" };
                ConfigureEdgeGrid(m, surface);
                return m;
            }

            shader = Shader.Find(PlaneGridShader);
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            var fallback = new Material(shader) { name = "ScanMat (runtime)" };
            if (fallback.HasProperty("_LineWidth")) fallback.SetFloat("_LineWidth", 0.030f);
            return fallback;
        }

        /// <summary>
        /// Forces the two mode switches that decide how EdgeGrid reads a surface. A
        /// material asset authored for the wrong one renders a plausible but wrong picture
        /// — lines that ignore a wall's slope, or box edges in the wrong place — so they
        /// are asserted per object rather than trusted from the asset.
        /// </summary>
        public static void ConfigureEdgeGrid(Material material, ScanSurface surface)
        {
            if (material == null || material.shader == null) return;
            if (material.shader.name != EdgeGridShader) return;

            // _BoxEdges is off for BOTH surfaces here, which is a deliberate departure from
            // the game's CubeEdgeGrid.mat.
            //
            // The object-space edge test derives the box half-extent from the transform's
            // lossy scale. That is right in the game, where a cube is a unit mesh scaled by
            // its transform — but Mortuorium bakes furniture size into the mesh in metres
            // and leaves localScale at 1 (see FurnitureObject), so the shader would place
            // the twelve edges on a fixed 1 m box whatever the real size. Normal
            // discontinuity gives the correct edges on both, since every face carries its
            // own vertices and its own normal.
            //
            // _GridFromUV differs, and that part does match the game: a wall's grid follows
            // the (u,v,w) WallMeshBuilder bakes into UV1, so it tracks a sloped base; a
            // cube's is metric object space, which rotates with the cube.
            if (material.HasProperty("_BoxEdges")) material.SetFloat("_BoxEdges", 0f);
            if (material.HasProperty("_GridFromUV"))
                material.SetFloat("_GridFromUV", surface == ScanSurface.Wall ? 1f : 0f);
        }
    }

    /// <summary>Which kind of scanned geometry a material is being applied to.</summary>
    public enum ScanSurface
    {
        /// <summary>A wall: grid from the baked (u,v,w), edges from normal discontinuity.</summary>
        Wall,
        /// <summary>A furniture box: grid from metric object space.</summary>
        Cube,
    }
}
