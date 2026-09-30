using UnityEngine;

namespace CleanerBot
{
    /// <summary>
    /// Renders a CoverageGrid as a transparent overlay just above the floor.
    /// Colour = number of passes (1 = light teal ... 4+ = deep blue). Remaining (uncleaned) area is optionally tinted.
    /// </summary>
    public class CoveragePainter
    {
        public static readonly Color32 Remaining = new Color32(255, 196, 90, 60);   // "dust" still to clean
        public static readonly Color32[] PassColors =
        {
            new Color32(0, 0, 0, 0),            // 0 passes
            new Color32(80, 215, 200, 150),     // 1
            new Color32(40, 160, 220, 185),     // 2
            new Color32(30, 100, 210, 210),     // 3
            new Color32(60, 40, 170, 230),      // 4+
        };

        public Texture2D Texture => _tex;
        public GameObject Overlay => _quad;

        readonly Texture2D _tex;
        readonly Color32[] _px;
        readonly Material _mat;
        readonly GameObject _quad;

        public CoveragePainter(CoverageGrid grid, Shader shader, Transform parent, float height)
        {
            _tex = new Texture2D(grid.Width, grid.Height, TextureFormat.RGBA32, false)
            {
                name = "CoverageTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _px = new Color32[grid.CellCount];

            var size = new Vector2(grid.Width * grid.CellSize, grid.Height * grid.CellSize);
            _mat = new Material(shader) { name = "CoverageOverlay (runtime)" };
            _mat.SetTexture("_CoverageTex", _tex);
            _mat.SetVector("_Rect", new Vector4(grid.Origin.x, grid.Origin.y, size.x, size.y));

            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "CoverageOverlay";
            var col = _quad.GetComponent<Collider>();
            if (col != null) Kill(col);          // must not block raycasts / NavMesh
            _quad.transform.SetParent(parent, false);
            _quad.transform.position = new Vector3(grid.Origin.x + size.x * 0.5f, height, grid.Origin.y + size.y * 0.5f);
            _quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = _quad.GetComponent<Renderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public void Refresh(CoverageGrid grid, bool showRemaining)
        {
            var passes = grid.Passes;
            var clean = grid.Cleanable;
            var clear = PassColors[0];
            int maxIdx = PassColors.Length - 1;
            for (int i = 0; i < _px.Length; i++)
            {
                int p = passes[i];
                if (p > 0) _px[i] = PassColors[p < maxIdx ? p : maxIdx];
                else _px[i] = showRemaining && clean[i] ? Remaining : clear;
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        public void SetVisible(bool visible) => _quad.SetActive(visible);

        public void Dispose()
        {
            if (_quad != null) Kill(_quad);
            if (_mat != null) Kill(_mat);
            if (_tex != null) Kill(_tex);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
