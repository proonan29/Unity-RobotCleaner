using System.Collections.Generic;
using UnityEngine;

namespace CleanerBot
{
    /// <summary>
    /// Maps furniture kinds (HouseLayout) to 3D model prefabs (e.g. Asset Store packs).
    /// Kinds without a prefab (or with a missing one) fall back to the procedural primitives.
    /// </summary>
    [CreateAssetMenu(menuName = "CleanerBot/Furniture Catalog", fileName = "FurnitureCatalog")]
    public class FurnitureCatalog : ScriptableObject
    {
        public enum Align
        {
            Center,     // centre the model on the footprint
            Back,       // align the model's back with the footprint's back (-Z local, i.e. against the wall)
            Front,      // align with the footprint's +Z edge (e.g. a bed's headboard side)
        }

        [System.Serializable]
        public class Entry
        {
            public string kind;
            public GameObject prefab;
            [Tooltip("Extra yaw so the model's front faces local +Z (the layout convention)")] public float yawOffset = 180f;
            public Align align = Align.Center;
            [Tooltip("Extra shift along local +Z (toward the front / away from the wall) after alignment, in metres")] public float depthOffset;
            [Tooltip("Repeat the model along local X to fill the footprint width (kitchen counters)")] public bool tileAlongX;
        }

        public List<Entry> entries = new List<Entry>();

        public Entry Find(string kind)
        {
            foreach (var e in entries)
                if (e != null && e.kind == kind && e.prefab != null) return e;
            return null;
        }
    }
}
