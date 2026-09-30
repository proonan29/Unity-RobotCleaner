using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    /// <summary>
    /// Floor coverage map. Source of truth for "where has the robot cleaned".
    /// - Cells are square (default 5 cm) over the floor's XZ extent.
    /// - Cleanable = cells the robot's brush can physically reach:
    ///   NavMesh cells connected to the robot's start, dilated by the brush radius.
    /// - Passes = how many separate times the brush went over a cell
    ///   (staying under the brush across consecutive stamps counts as one pass).
    /// </summary>
    public class CoverageGrid
    {
        public readonly int Width, Height;
        public readonly float CellSize;
        public readonly Vector2 Origin;     // world XZ of cell (0,0) min corner

        public readonly bool[] Cleanable;
        public readonly ushort[] Passes;
        readonly int[] _lastStep;
        int _step = 1;

        public int CleanableCount { get; private set; }
        public int CoveredCount { get; private set; }
        public int OverlapCount { get; private set; }    // cells with >= 2 passes
        public long TotalPasses { get; private set; }
        public bool Dirty { get; set; } = true;

        public int CellCount => Width * Height;
        public float CellArea => CellSize * CellSize;

        public CoverageGrid(Vector2 origin, Vector2 size, float cellSize)
        {
            CellSize = cellSize;
            Origin = origin;
            Width = Mathf.Max(1, Mathf.CeilToInt(size.x / cellSize));
            Height = Mathf.Max(1, Mathf.CeilToInt(size.y / cellSize));
            Cleanable = new bool[Width * Height];
            Passes = new ushort[Width * Height];
            _lastStep = new int[Width * Height];
        }

        public int Index(int x, int y) => y * Width + x;

        public Vector3 CellCenter(int x, int y) =>
            new Vector3(Origin.x + (x + 0.5f) * CellSize, 0f, Origin.y + (y + 0.5f) * CellSize);

        public bool WorldToCell(Vector3 p, out int x, out int y)
        {
            x = Mathf.FloorToInt((p.x - Origin.x) / CellSize);
            y = Mathf.FloorToInt((p.z - Origin.y) / CellSize);
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        /// <summary>Compute the cleanable mask. Call once after the NavMesh is available.</summary>
        public void BuildCleanableMask(Vector3 seedWorld, float brushRadius)
        {
            int n = CellCount;
            var onMesh = new bool[n];
            float half = CellSize * 0.5f;

            // 1) Cell centres that lie on the NavMesh at floor level
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    var c = CellCenter(x, y);
                    if (NavMesh.SamplePosition(c, out var hit, 0.12f, NavMesh.AllAreas))
                    {
                        float dx = hit.position.x - c.x, dz = hit.position.z - c.z;
                        if (dx * dx + dz * dz <= half * half && Mathf.Abs(hit.position.y) < 0.1f)
                            onMesh[Index(x, y)] = true;
                    }
                }

            // 2) Flood fill from the robot's start (drops islands the robot can't reach, e.g. under a bed)
            var reach = new bool[n];
            if (FindSeed(seedWorld, onMesh, out int seed))
            {
                var q = new Queue<int>();
                q.Enqueue(seed);
                reach[seed] = true;
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    int x = i % Width, y = i / Width;
                    TryPush(x + 1, y); TryPush(x - 1, y); TryPush(x, y + 1); TryPush(x, y - 1);
                }

                void TryPush(int x, int y)
                {
                    if (x < 0 || y < 0 || x >= Width || y >= Height) return;
                    int j = Index(x, y);
                    if (reach[j] || !onMesh[j]) return;
                    reach[j] = true;
                    q.Enqueue(j);
                }
            }

            // 3) Dilate by the brush radius: the brush reaches beyond the robot centre
            Array.Clear(Cleanable, 0, n);
            int r = Mathf.CeilToInt(brushRadius / CellSize);
            float r2 = (brushRadius / CellSize) * (brushRadius / CellSize);
            var offsets = new List<Vector2Int>();
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= r2) offsets.Add(new Vector2Int(dx, dy));

            for (int i = 0; i < n; i++)
            {
                if (!reach[i]) continue;
                int x = i % Width, y = i / Width;
                foreach (var o in offsets)
                {
                    int xx = x + o.x, yy = y + o.y;
                    if (xx < 0 || yy < 0 || xx >= Width || yy >= Height) continue;
                    Cleanable[Index(xx, yy)] = true;
                }
            }

            CleanableCount = 0;
            for (int i = 0; i < n; i++) if (Cleanable[i]) CleanableCount++;
            Clear();
        }

        bool FindSeed(Vector3 p, bool[] onMesh, out int seed)
        {
            seed = -1;
            if (!WorldToCell(p, out int cx, out int cy)) return false;
            for (int rad = 0; rad < 20; rad++)
                for (int dy = -rad; dy <= rad; dy++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                        if (onMesh[Index(x, y)]) { seed = Index(x, y); return true; }
                    }
            return false;
        }

        /// <summary>Reset cleaning progress (keeps the cleanable mask).</summary>
        public void Clear()
        {
            Array.Clear(Passes, 0, Passes.Length);
            Array.Clear(_lastStep, 0, _lastStep.Length);
            _step = 1;
            CoveredCount = OverlapCount = 0;
            TotalPasses = 0;
            Dirty = true;
        }

        /// <summary>Stamp the brush along a straight segment (spacing = half a cell, so no gaps at any speed).</summary>
        public void Sweep(Vector3 from, Vector3 to, float radius)
        {
            float d = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
            int steps = Mathf.Max(1, Mathf.CeilToInt(d / (CellSize * 0.5f)));
            for (int s = 1; s <= steps; s++)
                Stamp(Vector3.Lerp(from, to, (float)s / steps), radius);
        }

        /// <summary>Apply the circular brush once at p.</summary>
        public void Stamp(Vector3 p, float radius)
        {
            _step++;
            int x0 = Mathf.FloorToInt((p.x - radius - Origin.x) / CellSize);
            int x1 = Mathf.FloorToInt((p.x + radius - Origin.x) / CellSize);
            int y0 = Mathf.FloorToInt((p.z - radius - Origin.y) / CellSize);
            int y1 = Mathf.FloorToInt((p.z + radius - Origin.y) / CellSize);
            float r2 = radius * radius;

            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(Height - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Width - 1, x1); x++)
                {
                    var c = CellCenter(x, y);
                    float dx = c.x - p.x, dz = c.z - p.z;
                    if (dx * dx + dz * dz > r2) continue;
                    int i = Index(x, y);
                    if (!Cleanable[i]) continue;

                    if (_lastStep[i] < _step - 1)      // wasn't under the brush on the previous stamp -> new pass
                    {
                        if (Passes[i] == 0) CoveredCount++;
                        else if (Passes[i] == 1) OverlapCount++;
                        if (Passes[i] < ushort.MaxValue) Passes[i]++;
                        TotalPasses++;
                        Dirty = true;
                    }
                    _lastStep[i] = _step;
                }
        }

        public CoverageStats GetStats()
        {
            return new CoverageStats
            {
                cleanableM2 = CleanableCount * CellArea,
                coveredM2 = CoveredCount * CellArea,
                coveragePercent = CleanableCount > 0 ? 100f * CoveredCount / CleanableCount : 0f,
                averagePasses = CoveredCount > 0 ? (float)TotalPasses / CoveredCount : 0f,
                overlapPercent = CoveredCount > 0 ? 100f * OverlapCount / CoveredCount : 0f,
            };
        }
    }

    [Serializable]
    public struct CoverageStats
    {
        public float cleanableM2;
        public float coveredM2;
        public float coveragePercent;
        public float averagePasses;     // over covered cells
        public float overlapPercent;    // % of covered cells cleaned 2+ times
    }
}
