using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    /// <summary>Root component of a generated house. Guarantees a NavMesh exists at runtime.</summary>
    [DefaultExecutionOrder(-100)]
    public class HouseInstance : MonoBehaviour
    {
        public string layoutName;
        public string displayName;
        public NavMeshSurface surface;
        public Transform floor;
        public Bounds floorBounds;
        public Vector3 robotSpawn;

        void Awake()
        {
            if (surface == null) surface = GetComponent<NavMeshSurface>();
            // Baked data is normally saved with the scene; build on the fly if it's missing
            // (e.g. a house generated at runtime from a preset).
            if (surface != null && surface.navMeshData == null)
                surface.BuildNavMesh();
        }

        public void RebuildNavMesh()
        {
            if (surface == null) surface = GetComponent<NavMeshSurface>();
            surface.BuildNavMesh();
        }

        /// <summary>Walkable NavMesh area in m² (triangulation of all loaded NavMesh data).</summary>
        public static float NavMeshArea(out int triangles)
        {
            var tri = NavMesh.CalculateTriangulation();
            float area = 0f;
            for (int i = 0; i < tri.indices.Length; i += 3)
            {
                var a = tri.vertices[tri.indices[i]];
                var b = tri.vertices[tri.indices[i + 1]];
                var c = tri.vertices[tri.indices[i + 2]];
                area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
            triangles = tri.indices.Length / 3;
            return area;
        }
    }
}
