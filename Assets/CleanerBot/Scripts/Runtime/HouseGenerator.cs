using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    public interface IMaterialProvider
    {
        Material Get(string key, Color color);
    }

    /// <summary>Creates in-memory URP Lit materials (used at runtime when switching houses).</summary>
    public class RuntimeMaterialProvider : IMaterialProvider
    {
        readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        public Material Get(string key, Color color)
        {
            if (!_cache.TryGetValue(key, out var m) || m == null)
            {
                m = new Material(Prims.FindLitShader()) { name = key };
                m.SetColor("_BaseColor", color);
                _cache[key] = m;
            }
            return m;
        }
    }

    /// <summary>Primitive helpers shared by the generators.</summary>
    public static class Prims
    {
        public static Shader FindLitShader()
        {
            var s = Shader.Find("Universal Render Pipeline/Lit");
            return s != null ? s : Shader.Find("Standard");
        }

        public static GameObject Box(Transform parent, string name, Vector3 localCenter, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>Upright cylinder whose base sits at localBase.</summary>
        public static GameObject Cyl(Transform parent, string name, Vector3 localBase, float radius, float height, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localBase + new Vector3(0f, height * 0.5f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        public static GameObject Sphere(Transform parent, string name, Vector3 localCenter, float radius, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = Vector3.one * radius * 2f;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }
    }

    /// <summary>Builds a house GameObject hierarchy (floor, walls, furniture, NavMeshSurface) from a HouseLayout.</summary>
    public static class HouseGenerator
    {
        public const int NotWalkableArea = 1;
        static readonly Color FloorColor = new Color(0.80f, 0.70f, 0.55f);
        static readonly Color WallColor = new Color(0.93f, 0.92f, 0.89f);

        public static HouseInstance Build(HouseLayout L, IMaterialProvider mats)
        {
            var root = new GameObject("House_" + L.name);
            var house = root.AddComponent<HouseInstance>();
            float t = L.wallThickness;

            // Floor: top surface at y = 0
            var floor = Prims.Box(root.transform, "Floor",
                new Vector3(L.width * 0.5f, -0.05f, L.depth * 0.5f),
                new Vector3(L.width + t, 0.1f, L.depth + t),
                mats.Get("Floor", FloorColor));

            // Walls (not walkable)
            var walls = new GameObject("Walls").transform;
            walls.SetParent(root.transform, false);
            MarkNotWalkable(walls.gameObject);
            var wallMat = mats.Get("Wall", WallColor);
            for (int i = 0; i < L.walls.Count; i++)
                BuildWall(walls, L.walls[i], i, t, L.wallHeight, wallMat);

            // Furniture (not walkable on top, but space underneath legged furniture stays walkable)
            var furn = new GameObject("Furniture").transform;
            furn.SetParent(root.transform, false);
            MarkNotWalkable(furn.gameObject);
            var counts = new Dictionary<string, int>();
            foreach (var f in L.furniture)
            {
                counts.TryGetValue(f.kind, out var n);
                counts[f.kind] = n + 1;
                var go = FurnitureFactory.Create(f, mats);
                go.name = $"{f.kind}_{n + 1}";
                go.transform.SetParent(furn, false);
            }

            // NavMesh surface over the whole house
            var surface = root.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true;   // fine voxels so gaps between chair/table legs are resolved
            surface.voxelSize = 0.03f;

            house.layoutName = L.name;
            house.displayName = L.displayName;
            house.surface = surface;
            house.floor = floor.transform;
            house.floorBounds = new Bounds(new Vector3(L.width * 0.5f, 0f, L.depth * 0.5f), new Vector3(L.width, 0f, L.depth));
            house.robotSpawn = new Vector3(L.robotSpawn.x, 0f, L.robotSpawn.y);
            house.robotSpawnYaw = L.robotSpawnYaw;
            house.dockLed = BuildDock(root.transform, house.robotSpawn, L.robotSpawnYaw, mats);
            return house;
        }

        /// <summary>Charging dock behind the robot's docked position. Visual only (no colliders -> NavMesh unchanged).</summary>
        static Renderer BuildDock(Transform parent, Vector3 robotPos, float yaw, IMaterialProvider mats)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var dock = new GameObject("Dock").transform;
            dock.SetParent(parent, false);
            dock.localRotation = rot;
            dock.localPosition = robotPos - rot * Vector3.forward * 0.22f;

            var body = mats.Get("DockBody", new Color(0.12f, 0.12f, 0.14f));
            NoCollider(Prims.Box(dock, "Plate", new Vector3(0f, 0.004f, 0.14f), new Vector3(0.34f, 0.008f, 0.32f), body));
            NoCollider(Prims.Box(dock, "Tower", new Vector3(0f, 0.06f, 0f), new Vector3(0.30f, 0.12f, 0.08f), body));
            NoCollider(Prims.Box(dock, "Contacts", new Vector3(0f, 0.012f, 0.1f), new Vector3(0.16f, 0.006f, 0.04f), mats.Get("Metal", new Color(0.55f, 0.57f, 0.60f))));
            var led = Prims.Box(dock, "LED", new Vector3(0f, 0.095f, 0.042f), new Vector3(0.06f, 0.018f, 0.006f), mats.Get("DockLED", new Color(0.2f, 0.9f, 0.4f)));
            NoCollider(led);
            return led.GetComponent<Renderer>();
        }

        static void NoCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);   // immediate: the NavMesh may be rebuilt this frame
        }

        static void MarkNotWalkable(GameObject go)
        {
            var mod = go.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = NotWalkableArea;
        }

        static void BuildWall(Transform parent, WallSpec w, int idx, float t, float h, Material m)
        {
            bool alongX = Mathf.Abs(w.a.y - w.b.y) < 1e-4f;
            float fixedC = alongX ? w.a.y : w.a.x;
            float s0 = (alongX ? Mathf.Min(w.a.x, w.b.x) : Mathf.Min(w.a.y, w.b.y)) - t * 0.5f;
            float s1 = (alongX ? Mathf.Max(w.a.x, w.b.x) : Mathf.Max(w.a.y, w.b.y)) + t * 0.5f;

            var doors = new List<Vector2>(w.doors);
            doors.Sort((p, q) => p.x.CompareTo(q.x));

            int k = 0;
            float cur = s0;
            foreach (var d in doors)
            {
                Segment(cur, d.x);
                cur = d.y;
            }
            Segment(cur, s1);

            void Segment(float from, float to)
            {
                if (to - from < 0.01f) return;
                float len = to - from, mid = (from + to) * 0.5f;
                var c = alongX ? new Vector3(mid, h * 0.5f, fixedC) : new Vector3(fixedC, h * 0.5f, mid);
                var size = alongX ? new Vector3(len, h, t) : new Vector3(t, h, len);
                Prims.Box(parent, $"Wall_{idx}_{k++}", c, size, m);
            }
        }
    }
}
