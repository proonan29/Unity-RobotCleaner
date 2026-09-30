using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CleanerBot
{
    /// <summary>Built-in ("prefilled") house layouts. Add new presets to the table below.</summary>
    public static class HousePresets
    {
        static readonly Dictionary<string, Func<HouseLayout>> Table = new Dictionary<string, Func<HouseLayout>>
        {
            { "Apartment_A", ApartmentA },
            { "Studio_B", StudioB },
            { "House_C", HouseC },
        };

        public static IReadOnlyList<string> Names => Table.Keys.ToList();

        public static HouseLayout Get(string name)
        {
            if (string.IsNullOrEmpty(name) || !Table.TryGetValue(name, out var f))
                throw new ArgumentException($"Unknown house preset '{name}'. Available: {string.Join(", ", Table.Keys)}");
            return f();
        }

        public static string DisplayName(string name)
        {
            try { return Get(name).displayName; } catch { return name; }
        }

        static Vector2 Door(float from, float to) => new Vector2(from, to);

        static void OuterWalls(HouseLayout L)
        {
            L.walls.Add(new WallSpec(0, 0, L.width, 0));
            L.walls.Add(new WallSpec(0, L.depth, L.width, L.depth));
            L.walls.Add(new WallSpec(0, 0, 0, L.depth));
            L.walls.Add(new WallSpec(L.width, 0, L.width, L.depth));
        }

        /// <summary>
        /// 10m x 8m apartment, 4 rooms:
        ///   Living (x0-6, z0-5) | Kitchen/Dining (x6-10, z0-4)
        ///   Study  (x0-6, z5-8) | Bedroom        (x6-10, z4-8)
        /// </summary>
        static HouseLayout ApartmentA()
        {
            var L = new HouseLayout
            {
                name = "Apartment_A",
                displayName = "Apartment A (4 rooms, 80 m²)",
                width = 10f,
                depth = 8f,
                robotSpawn = new Vector2(4.6f, 4.65f),     // dock on the living-room wall, visible from the camera
                robotSpawnYaw = 180f,
            };

            OuterWalls(L);
            // Interior walls with door openings (1.0 m)
            L.walls.Add(new WallSpec(6, 0, 6, 8, Door(1.6f, 2.6f), Door(6.2f, 7.2f))); // living|kitchen, study|bedroom
            L.walls.Add(new WallSpec(0, 5, 6, 5, Door(2.0f, 3.0f)));                    // living|study
            L.walls.Add(new WallSpec(6, 4, 10, 4, Door(7.6f, 8.6f)));                   // kitchen|bedroom

            var F = L.furniture;
            // Living room
            F.Add(new FurnitureSpec("sofa", 2.5f, 0.55f, 0f));
            F.Add(new FurnitureSpec("coffee_table", 2.5f, 1.75f));
            F.Add(new FurnitureSpec("tv_stand", 2.5f, 4.73f, 180f));
            F.Add(new FurnitureSpec("armchair", 0.55f, 2.2f, 90f));
            F.Add(new FurnitureSpec("bookshelf", 0.22f, 3.9f, 90f));
            F.Add(new FurnitureSpec("plant", 5.6f, 0.4f));
            // Kitchen / dining
            F.Add(new FurnitureSpec("kitchen_counter", 8.4f, 0.36f, 0f, 3.0f, 0.6f));
            F.Add(new FurnitureSpec("fridge", 9.58f, 1.25f, -90f));
            F.Add(new FurnitureSpec("dining_table", 8.0f, 2.6f));
            F.Add(new FurnitureSpec("chair", 7.6f, 1.95f, 0f));
            F.Add(new FurnitureSpec("chair", 8.4f, 1.95f, 0f));
            F.Add(new FurnitureSpec("chair", 7.6f, 3.25f, 180f));
            F.Add(new FurnitureSpec("chair", 8.4f, 3.25f, 180f));
            // Bedroom
            F.Add(new FurnitureSpec("bed", 8.6f, 6.93f, 0f));
            F.Add(new FurnitureSpec("nightstand", 7.5f, 7.7f));
            F.Add(new FurnitureSpec("nightstand", 9.68f, 7.7f));
            F.Add(new FurnitureSpec("wardrobe", 6.36f, 4.75f, 90f));
            F.Add(new FurnitureSpec("plant", 9.6f, 4.45f));
            // Study
            F.Add(new FurnitureSpec("desk", 1.2f, 7.6f));
            F.Add(new FurnitureSpec("chair", 1.2f, 6.95f, 0f));
            F.Add(new FurnitureSpec("bookshelf", 4.0f, 7.8f, 180f));
            F.Add(new FurnitureSpec("armchair", 5.35f, 5.8f, -90f));
            F.Add(new FurnitureSpec("plant", 0.4f, 5.45f));
            return L;
        }

        /// <summary>
        /// 7m x 5m studio: one open room with sleeping / living / kitchen corners and a bathroom (x5-7, z3-5).
        /// </summary>
        static HouseLayout StudioB()
        {
            var L = new HouseLayout
            {
                name = "Studio_B",
                displayName = "Studio B (open room + bath, 35 m²)",
                width = 7f,
                depth = 5f,
                robotSpawn = new Vector2(0.35f, 2.3f),
                robotSpawnYaw = 90f,
            };

            OuterWalls(L);
            L.walls.Add(new WallSpec(5, 3, 5, 5, Door(3.3f, 4.2f)));   // bathroom door (0.9 m)
            L.walls.Add(new WallSpec(5, 3, 7, 3));

            var F = L.furniture;
            F.Add(new FurnitureSpec("bed", 1.1f, 3.95f, 0f));
            F.Add(new FurnitureSpec("wardrobe", 3.0f, 4.65f, 180f));
            F.Add(new FurnitureSpec("sofa", 3.3f, 0.55f, 0f));
            F.Add(new FurnitureSpec("coffee_table", 3.3f, 1.75f));
            F.Add(new FurnitureSpec("desk", 0.36f, 1.1f, 90f));
            F.Add(new FurnitureSpec("chair", 0.95f, 1.1f, -90f));
            F.Add(new FurnitureSpec("kitchen_counter", 5.9f, 0.36f, 0f, 2.0f, 0.6f));
            F.Add(new FurnitureSpec("fridge", 6.58f, 1.35f, -90f));
            F.Add(new FurnitureSpec("plant", 4.7f, 0.4f));
            F.Add(new FurnitureSpec("plant", 6.65f, 4.65f));
            return L;
        }

        /// <summary>
        /// 12m x 9m family house, 5 rooms:
        ///   Living (x0-7, z0-5)            | Kitchen/Dining (x7-12, z0-4)
        ///   Bedroom 2 (x0-4, z5-9) | Bath (x4-7, z5-9) | Master bedroom (x7-12, z4-9)
        /// </summary>
        static HouseLayout HouseC()
        {
            var L = new HouseLayout
            {
                name = "House_C",
                displayName = "House C (5 rooms, 108 m²)",
                width = 12f,
                depth = 9f,
                robotSpawn = new Vector2(6.5f, 4.65f),
                robotSpawnYaw = 180f,
            };

            OuterWalls(L);
            L.walls.Add(new WallSpec(7, 0, 7, 9, Door(1.5f, 2.5f), Door(5.8f, 6.8f)));   // living|kitchen, bath|master
            L.walls.Add(new WallSpec(0, 5, 7, 5, Door(1.5f, 2.5f), Door(5.0f, 6.0f)));   // living|bed2, living|bath
            L.walls.Add(new WallSpec(4, 5, 4, 9));                                       // bed2|bath
            L.walls.Add(new WallSpec(7, 4, 12, 4, Door(9.0f, 10.0f)));                   // kitchen|master

            var F = L.furniture;
            // Living
            F.Add(new FurnitureSpec("sofa", 3.0f, 0.55f, 0f));
            F.Add(new FurnitureSpec("coffee_table", 3.0f, 1.75f));
            F.Add(new FurnitureSpec("tv_stand", 3.6f, 4.73f, 180f));
            F.Add(new FurnitureSpec("armchair", 0.55f, 2.2f, 90f));
            F.Add(new FurnitureSpec("armchair", 5.6f, 2.0f, -90f));
            F.Add(new FurnitureSpec("bookshelf", 0.22f, 3.9f, 90f));
            F.Add(new FurnitureSpec("plant", 6.6f, 0.4f));
            // Kitchen / dining
            F.Add(new FurnitureSpec("kitchen_counter", 9.5f, 0.36f, 0f, 3.6f, 0.6f));
            F.Add(new FurnitureSpec("fridge", 11.58f, 1.3f, -90f));
            F.Add(new FurnitureSpec("dining_table", 9.5f, 2.4f));
            F.Add(new FurnitureSpec("chair", 9.1f, 1.75f, 0f));
            F.Add(new FurnitureSpec("chair", 9.9f, 1.75f, 0f));
            F.Add(new FurnitureSpec("chair", 9.1f, 3.05f, 180f));
            F.Add(new FurnitureSpec("chair", 9.9f, 3.05f, 180f));
            // Master bedroom
            F.Add(new FurnitureSpec("bed", 10.5f, 7.93f, 0f));
            F.Add(new FurnitureSpec("nightstand", 9.4f, 8.7f));
            F.Add(new FurnitureSpec("nightstand", 11.6f, 8.7f));
            F.Add(new FurnitureSpec("wardrobe", 7.36f, 5.0f, 90f));
            F.Add(new FurnitureSpec("desk", 8.2f, 8.6f));
            F.Add(new FurnitureSpec("chair", 8.2f, 7.95f, 0f));
            F.Add(new FurnitureSpec("plant", 11.6f, 4.4f));
            // Bedroom 2
            F.Add(new FurnitureSpec("bed", 1.0f, 7.93f, 0f));
            F.Add(new FurnitureSpec("nightstand", 2.1f, 8.7f));
            F.Add(new FurnitureSpec("wardrobe", 3.64f, 6.3f, -90f));
            F.Add(new FurnitureSpec("plant", 0.4f, 5.4f));
            // Bath
            F.Add(new FurnitureSpec("nightstand", 4.4f, 8.7f));
            F.Add(new FurnitureSpec("plant", 6.6f, 8.6f));
            return L;
        }
    }
}
