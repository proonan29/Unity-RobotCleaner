using System.Collections.Generic;
using UnityEngine;

namespace CleanerBot
{
    /// <summary>
    /// Procedural placeholder furniture built from primitives (swap for Asset Store models later).
    /// Local frame: footprint centred on the origin, +Z = front (the side you sit on / face).
    /// Legged pieces leave floor space underneath that a ~10 cm robot can clean.
    /// </summary>
    public static class FurnitureFactory
    {
        static readonly Color Fabric = new Color(0.36f, 0.42f, 0.52f);
        static readonly Color FabricDark = new Color(0.28f, 0.32f, 0.40f);
        static readonly Color WoodLight = new Color(0.72f, 0.56f, 0.38f);
        static readonly Color WoodDark = new Color(0.42f, 0.29f, 0.19f);
        static readonly Color White = new Color(0.92f, 0.92f, 0.92f);
        static readonly Color Metal = new Color(0.55f, 0.57f, 0.60f);
        static readonly Color Black = new Color(0.08f, 0.08f, 0.09f);
        static readonly Color Linen = new Color(0.90f, 0.87f, 0.80f);
        static readonly Color Leaf = new Color(0.25f, 0.55f, 0.28f);
        static readonly Color Pot = new Color(0.70f, 0.42f, 0.30f);

        public static GameObject Create(FurnitureSpec f, IMaterialProvider mats, FurnitureCatalog catalog = null)
        {
            var root = new GameObject(f.kind);      // built at the origin, posed at the end
            var p = root.transform;

            if (TryModel(p, f, catalog))
            {
                root.transform.localPosition = new Vector3(f.pos.x, 0f, f.pos.y);
                root.transform.localRotation = Quaternion.Euler(0f, f.rotY, 0f);
                return root;
            }

            switch (f.kind)
            {
                case "sofa": Sofa(p, mats, 2.0f); break;
                case "armchair": Sofa(p, mats, 0.9f); break;
                case "coffee_table": Table(p, mats, 1.1f, 0.6f, 0.42f, WoodLight, "WoodLight"); break;
                case "dining_table": Table(p, mats, 1.4f, 0.8f, 0.74f, WoodDark, "WoodDark"); break;
                case "desk": Table(p, mats, 1.2f, 0.6f, 0.74f, White, "White"); break;
                case "chair": Chair(p, mats); break;
                case "tv_stand": TvStand(p, mats); break;
                case "bookshelf": Solid(p, mats, 0.9f, 0.3f, 1.8f, WoodDark, "WoodDark"); break;
                case "kitchen_counter":
                    {
                        float sx = f.size.x > 0 ? f.size.x : 2.0f, sz = f.size.y > 0 ? f.size.y : 0.6f;
                        Solid(p, mats, sx, sz, 0.86f, White, "White");
                        Prims.Box(p, "Worktop", new Vector3(0, 0.88f, 0), new Vector3(sx, 0.04f, sz), mats.Get("Metal", Metal));
                        break;
                    }
                case "fridge": Solid(p, mats, 0.7f, 0.7f, 1.8f, Metal, "Metal"); break;
                case "bed": Bed(p, mats); break;
                case "nightstand": Solid(p, mats, 0.45f, 0.4f, 0.5f, WoodLight, "WoodLight"); break;
                case "wardrobe": Solid(p, mats, 1.2f, 0.6f, 2.0f, WoodLight, "WoodLight"); break;
                case "plant": Plant(p, mats); break;
                default:
                    Debug.LogWarning($"[CleanerBot] Unknown furniture kind '{f.kind}', using a placeholder box.");
                    Solid(p, mats, 0.5f, 0.5f, 0.5f, Color.magenta, "Unknown");
                    break;
            }
            root.transform.localPosition = new Vector3(f.pos.x, 0f, f.pos.y);
            root.transform.localRotation = Quaternion.Euler(0f, f.rotY, 0f);
            return root;
        }

        /// <summary>Footprint (x = width, y = depth) of the procedural version of each kind; models are aligned to it.</summary>
        public static Vector2 Footprint(FurnitureSpec f)
        {
            switch (f.kind)
            {
                case "sofa": return new Vector2(2.0f, 0.9f);
                case "armchair": return new Vector2(0.9f, 0.9f);
                case "coffee_table": return new Vector2(1.1f, 0.6f);
                case "dining_table": return new Vector2(1.4f, 0.8f);
                case "desk": return new Vector2(1.2f, 0.6f);
                case "chair": return new Vector2(0.45f, 0.45f);
                case "tv_stand": return new Vector2(1.6f, 0.4f);
                case "bookshelf": return new Vector2(0.9f, 0.3f);
                case "kitchen_counter": return new Vector2(f.size.x > 0 ? f.size.x : 2.0f, f.size.y > 0 ? f.size.y : 0.6f);
                case "fridge": return new Vector2(0.7f, 0.7f);
                case "bed": return new Vector2(1.6f, 2.0f);
                case "nightstand": return new Vector2(0.45f, 0.4f);
                case "wardrobe": return new Vector2(1.2f, 0.6f);
                case "plant": return new Vector2(0.34f, 0.34f);
                default: return new Vector2(0.5f, 0.5f);
            }
        }

        /// <summary>
        /// Place catalog model(s) under `root` (which must be at the origin with identity rotation):
        /// yaw-corrected, sitting on the floor, aligned to the kind's footprint, with mesh colliders for the NavMesh.
        /// </summary>
        static bool TryModel(Transform root, FurnitureSpec f, FurnitureCatalog catalog)
        {
            var e = catalog != null ? catalog.Find(f.kind) : null;
            if (e == null) return false;
            var fp = Footprint(f);

            var first = UnityEngine.Object.Instantiate(e.prefab, root);
            first.name = e.prefab.name;
            first.transform.localPosition = Vector3.zero;
            first.transform.localRotation = Quaternion.Euler(0f, e.yawOffset, 0f);
            var parts = new List<GameObject> { first };

            if (e.tileAlongX)
            {
                float w = RendererBounds(first).size.x;
                int n = Mathf.Max(1, Mathf.FloorToInt((fp.x + 0.05f) / Mathf.Max(0.05f, w)));
                for (int i = 1; i < n; i++)
                {
                    var c = UnityEngine.Object.Instantiate(e.prefab, root);
                    c.name = e.prefab.name + "_" + i;
                    c.transform.localRotation = first.transform.localRotation;
                    parts.Add(c);
                }
                for (int i = 0; i < parts.Count; i++)
                    parts[i].transform.localPosition = new Vector3((i - (parts.Count - 1) * 0.5f) * w, 0f, 0f);
            }

            // align combined bounds to the footprint (root is at the origin, so world == local here)
            var b = RendererBounds(root.gameObject);
            float dz = e.align == FurnitureCatalog.Align.Back ? (-fp.y * 0.5f) - b.min.z
                     : e.align == FurnitureCatalog.Align.Front ? (fp.y * 0.5f) - b.max.z
                     : -b.center.z;
            var delta = new Vector3(-b.center.x, -b.min.y, dz + e.depthOffset);
            foreach (var p in parts) p.transform.localPosition += delta;

            // colliders for the NavMesh (PhysicsColliders) and click raycasts
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            return true;
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }


        /// <summary>Box that sits on the floor (robot cannot go under).</summary>
        static void Solid(Transform p, IMaterialProvider mats, float sx, float sz, float h, Color c, string key)
        {
            Prims.Box(p, "Body", new Vector3(0, h * 0.5f, 0), new Vector3(sx, h, sz), mats.Get(key, c));
        }

        static void Legs(Transform p, IMaterialProvider mats, float sx, float sz, float h, float leg, float inset, Color c, string key)
        {
            var m = mats.Get(key, c);
            float x = sx * 0.5f - inset - leg * 0.5f, z = sz * 0.5f - inset - leg * 0.5f;
            int i = 0;
            foreach (var sxn in new[] { -1f, 1f })
                foreach (var szn in new[] { -1f, 1f })
                    Prims.Box(p, $"Leg_{i++}", new Vector3(sxn * x, h * 0.5f, szn * z), new Vector3(leg, h, leg), m);
        }

        static void Table(Transform p, IMaterialProvider mats, float sx, float sz, float h, Color c, string key)
        {
            const float top = 0.04f;
            Prims.Box(p, "Top", new Vector3(0, h - top * 0.5f, 0), new Vector3(sx, top, sz), mats.Get(key, c));
            Legs(p, mats, sx, sz, h - top, 0.05f, 0.04f, c, key);
        }

        static void Chair(Transform p, IMaterialProvider mats)
        {
            const float s = 0.45f, seatH = 0.45f;
            Prims.Box(p, "Seat", new Vector3(0, seatH - 0.02f, 0), new Vector3(s, 0.04f, s), mats.Get("WoodDark", WoodDark));
            Prims.Box(p, "Back", new Vector3(0, seatH + 0.22f, -s * 0.5f + 0.02f), new Vector3(s, 0.4f, 0.04f), mats.Get("WoodDark", WoodDark));
            Legs(p, mats, s, s, seatH - 0.04f, 0.035f, 0.01f, WoodDark, "WoodDark");
        }

        /// <summary>Skirted sofa/armchair (solid to the floor), seat facing +Z.</summary>
        static void Sofa(Transform p, IMaterialProvider mats, float width)
        {
            const float depth = 0.9f;
            var fabric = mats.Get("Fabric", Fabric);
            var dark = mats.Get("FabricDark", FabricDark);
            Prims.Box(p, "Base", new Vector3(0, 0.21f, 0), new Vector3(width, 0.42f, depth), fabric);
            Prims.Box(p, "Back", new Vector3(0, 0.62f, -depth * 0.5f + 0.1f), new Vector3(width, 0.4f, 0.2f), dark);
            Prims.Box(p, "ArmL", new Vector3(-width * 0.5f + 0.075f, 0.53f, 0), new Vector3(0.15f, 0.22f, depth), dark);
            Prims.Box(p, "ArmR", new Vector3(width * 0.5f - 0.075f, 0.53f, 0), new Vector3(0.15f, 0.22f, depth), dark);
        }

        static void TvStand(Transform p, IMaterialProvider mats)
        {
            Solid(p, mats, 1.6f, 0.4f, 0.5f, WoodDark, "WoodDark");
            Prims.Box(p, "TV", new Vector3(0, 0.5f + 0.4f, 0), new Vector3(1.3f, 0.75f, 0.05f), mats.Get("Black", Black));
        }

        /// <summary>Bed on a solid base (too low for the robot), headboard at +Z.</summary>
        static void Bed(Transform p, IMaterialProvider mats)
        {
            const float w = 1.6f, l = 2.0f;
            Prims.Box(p, "Base", new Vector3(0, 0.2f, 0), new Vector3(w, 0.4f, l), mats.Get("WoodLight", WoodLight));
            Prims.Box(p, "Mattress", new Vector3(0, 0.5f, 0.02f), new Vector3(w - 0.06f, 0.2f, l - 0.1f), mats.Get("Linen", Linen));
            Prims.Box(p, "Headboard", new Vector3(0, 0.55f, l * 0.5f - 0.04f), new Vector3(w, 1.1f, 0.08f), mats.Get("WoodDark", WoodDark));
        }

        static void Plant(Transform p, IMaterialProvider mats)
        {
            Prims.Cyl(p, "Pot", Vector3.zero, 0.17f, 0.4f, mats.Get("Pot", Pot));
            Prims.Sphere(p, "Foliage", new Vector3(0, 0.7f, 0), 0.3f, mats.Get("Leaf", Leaf));
        }
    }
}
