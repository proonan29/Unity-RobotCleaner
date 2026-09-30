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
        };

        public static IReadOnlyList<string> Names => Table.Keys.ToList();

        public static HouseLayout Get(string name)
        {
            if (string.IsNullOrEmpty(name) || !Table.TryGetValue(name, out var f))
                throw new ArgumentException($"Unknown house preset '{name}'. Available: {string.Join(", ", Table.Keys)}");
            return f();
        }

        static Vector2 Door(float from, float to) => new Vector2(from, to);

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
                robotSpawn = new Vector2(4.6f, 0.5f),
            };

            // Outer walls
            L.walls.Add(new WallSpec(0, 0, 10, 0));
            L.walls.Add(new WallSpec(0, 8, 10, 8));
            L.walls.Add(new WallSpec(0, 0, 0, 8));
            L.walls.Add(new WallSpec(10, 0, 10, 8));
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
    }
}
