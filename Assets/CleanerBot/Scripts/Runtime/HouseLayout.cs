using System;
using System.Collections.Generic;
using UnityEngine;

namespace CleanerBot
{
    /// <summary>
    /// Axis-aligned wall segment from a to b (XZ plane, metres).
    /// doors: openings as (start, end) along the wall's axis, in world coordinates.
    /// </summary>
    [Serializable]
    public class WallSpec
    {
        public Vector2 a;
        public Vector2 b;
        public List<Vector2> doors = new List<Vector2>();

        public WallSpec() { }
        public WallSpec(float ax, float az, float bx, float bz, params Vector2[] doorSpans)
        {
            a = new Vector2(ax, az);
            b = new Vector2(bx, bz);
            doors.AddRange(doorSpans);
        }
    }

    /// <summary>Furniture placement. pos = footprint centre (x, z). size is optional (kind-specific).</summary>
    [Serializable]
    public class FurnitureSpec
    {
        public string kind;
        public Vector2 pos;
        public float rotY;
        public Vector2 size;

        public FurnitureSpec() { }
        public FurnitureSpec(string kind, float x, float z, float rotY = 0f, float sx = 0f, float sz = 0f)
        {
            this.kind = kind;
            pos = new Vector2(x, z);
            this.rotY = rotY;
            size = new Vector2(sx, sz);
        }
    }

    /// <summary>Data description of a house. Serializable so presets can later come from JSON / floor plans.</summary>
    [Serializable]
    public class HouseLayout
    {
        public string name;
        public string displayName;
        public float width;          // X extent (m)
        public float depth;          // Z extent (m)
        public float wallHeight = 1.2f;    // cut-away height so the interior stays visible
        public float wallThickness = 0.1f;
        public Vector2 robotSpawn;
        public List<WallSpec> walls = new List<WallSpec>();
        public List<FurnitureSpec> furniture = new List<FurnitureSpec>();
    }
}
