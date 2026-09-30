using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    public static class PlannerFactory
    {
        public static readonly string[] Names = { "Frontier", "Random" };

        public static ICleaningPlanner Create(string name, int seed = 1)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "random": return new RandomPlanner(seed);
                case "frontier":
                default: return new FrontierPlanner();
            }
        }
    }

    /// <summary>Baseline: drive to random reachable points (like early bump-and-go robots, but with paths).</summary>
    public class RandomPlanner : ICleaningPlanner
    {
        public string Name => "Random";
        readonly System.Random _rng;
        List<int> _cells;
        CoverageGrid _grid;

        public RandomPlanner(int seed) { _rng = new System.Random(seed); }

        public bool NextGoals(SimulationCore sim, List<Vector3> waypoints)
        {
            var g = sim.Grid;
            if (_grid != g)
            {
                _grid = g;
                _cells = new List<int>();
                for (int i = 0; i < g.CellCount; i++) if (g.Reachable[i]) _cells.Add(i);
            }
            if (_cells.Count == 0) return false;
            int c = _cells[_rng.Next(_cells.Count)];
            waypoints.Add(g.CellCenter(c % g.Width, c / g.Width));
            return true;
        }

        public void OnGoalFailed(Vector3 goal) { }
    }

    /// <summary>
    /// "Nearest uncleaned area" planner.
    /// 1) BFS over reachable robot-centre cells from the robot (≈ path distance).
    /// 2) Candidates = cells where the brush would clean >= minGainFraction of its footprint;
    ///    among those within `slack` of the nearest, prefer high gain and alignment with the current heading.
    /// 3) Extend in a straight line (heading, reverse, towards target or along an axis) through dirty floor,
    ///    which naturally produces back-and-forth strips instead of tiny hops.
    /// </summary>
    public class FrontierPlanner : ICleaningPlanner
    {
        public string Name => "Frontier";

        public float minGainFraction = 0.3f;
        public float extendGainFraction = 0.5f;
        [Tooltip("Weight of candidates whose brush footprint touches nothing cleaned yet (discourages leaving slivers between lanes)")]
        public float openAreaWeight = 0.6f;
        public float slack = 0.3f;           // m
        public float headingWeight = 0.6f;
        public float maxExtend = 4f;         // m

        CoverageGrid _g;
        List<Vector2Int> _brush;
        int[] _dist, _queue, _visit;
        int _stamp;
        readonly HashSet<int> _blacklist = new HashSet<int>();
        readonly List<Vector3> _dirs = new List<Vector3>(8);

        void Ensure(CoverageGrid g, float brushRadius)
        {
            if (_g == g) return;
            _g = g;
            _brush = g.BrushOffsets(brushRadius);
            _dist = new int[g.CellCount];
            _queue = new int[g.CellCount];
            _visit = new int[g.CellCount];
            _blacklist.Clear();
        }

        public bool NextGoals(SimulationCore sim, List<Vector3> waypoints)
        {
            var g = sim.Grid;
            Ensure(g, sim.Robot.Spec.brushRadius);
            int start = g.NearestReachable(sim.Robot.Position);
            if (start < 0) return false;

            int full = _brush.Count;
            int best = Search(sim, start, Mathf.Max(1, Mathf.RoundToInt(full * minGainFraction)));
            if (best < 0) best = Search(sim, start, 1);          // mop up small leftovers
            if (best < 0) return false;
            Extend(sim, best, waypoints);
            return true;
        }

        public void OnGoalFailed(Vector3 goal)
        {
            if (_g != null && _g.WorldToCell(goal, out int x, out int y)) _blacklist.Add(_g.Index(x, y));
        }

        int Search(SimulationCore sim, int start, int minGain)
        {
            var g = _g;
            int w = g.Width, h = g.Height;
            var reach = g.Reachable;
            _stamp++;
            int head = 0, tail = 0;
            _queue[tail++] = start;
            _visit[start] = _stamp;
            _dist[start] = 0;

            float hr = sim.Robot.Heading * Mathf.Deg2Rad;
            float hx = Mathf.Sin(hr), hz = Mathf.Cos(hr);
            int rx = start % w, ry = start / w;
            int slackCells = Mathf.CeilToInt(slack / g.CellSize);
            int limit = int.MaxValue;
            int bestIdx = -1;
            float bestScore = float.MinValue;
            float full = _brush.Count;

            while (head < tail)
            {
                int i = _queue[head++];
                int d = _dist[i];
                if (d > limit) break;
                int x = i % w, y = i / w;

                if (!_blacklist.Contains(i))
                {
                    int gain = g.Gain(x, y, _brush);
                    if (gain >= minGain)
                    {
                        if (limit == int.MaxValue) limit = d + slackCells;
                        float vx = x - rx, vy = y - ry;
                        float len = Mathf.Sqrt(vx * vx + vy * vy);
                        float align = len > 0.5f ? (vx * hx + vy * hz) / len : 1f;
                        // prefer lanes that touch already-cleaned floor/obstacles: max gain but not fully open
                        float eff = gain >= full ? full * openAreaWeight : gain;
                        float score = eff / full * (1f + headingWeight * align) - 0.5f * d * g.CellSize;
                        if (score > bestScore) { bestScore = score; bestIdx = i; }
                    }
                }

                if (x + 1 < w) Push(i + 1, d);
                if (x - 1 >= 0) Push(i - 1, d);
                if (y + 1 < h) Push(i + w, d);
                if (y - 1 >= 0) Push(i - w, d);
            }
            return bestIdx;

            void Push(int j, int d)
            {
                if (_visit[j] == _stamp || !reach[j]) return;
                _visit[j] = _stamp;
                _dist[j] = d + 1;
                _queue[tail++] = j;
            }
        }

        void Extend(SimulationCore sim, int cell, List<Vector3> waypoints)
        {
            var g = _g;
            var target = g.CellCenter(cell % g.Width, cell / g.Width);
            target.y = sim.Robot.Position.y;

            float hr = sim.Robot.Heading * Mathf.Deg2Rad;
            var heading = new Vector3(Mathf.Sin(hr), 0f, Mathf.Cos(hr));
            var toTarget = target - sim.Robot.Position; toTarget.y = 0f;

            _dirs.Clear();
            _dirs.Add(heading);
            _dirs.Add(-heading);
            if (toTarget.sqrMagnitude > 1e-6f) _dirs.Add(toTarget.normalized);
            // axis-aligned options keep strips parallel to walls
            _dirs.Add(Vector3.right); _dirs.Add(Vector3.left); _dirs.Add(Vector3.forward); _dirs.Add(Vector3.back);

            int minGain = Mathf.Max(1, Mathf.RoundToInt(_brush.Count * extendGainFraction));
            Vector3 bestEnd = target;
            float bestSum = 0f;
            int maxSteps = Mathf.FloorToInt(maxExtend / g.CellSize);

            for (int di = 0; di < _dirs.Count; di++)
            {
                var u = _dirs[di];
                float sum = 0f;
                Vector3 end = target;
                for (int k = 1; k <= maxSteps; k++)
                {
                    var p = target + u * (k * g.CellSize);
                    if (!g.WorldToCell(p, out int x, out int y)) break;
                    int i = g.Index(x, y);
                    if (!g.Reachable[i]) break;
                    int gain = g.Gain(x, y, _brush);
                    if (gain < minGain) break;
                    sum += gain;
                    end = p;
                }
                if (di == 0) sum *= 1.15f;               // slight preference for keeping the heading
                if (sum > bestSum) { bestSum = sum; bestEnd = end; }
            }

            if (bestSum > 0f && NavMesh.Raycast(target, bestEnd, out var hit, NavMesh.AllAreas))
            {
                var back = (bestEnd - target).normalized;
                bestEnd = hit.position - back * 0.03f;
            }

            waypoints.Add(target);
            if (bestSum > 0f && (bestEnd - target).sqrMagnitude > g.CellSize * g.CellSize)
                waypoints.Add(bestEnd);
        }
    }
}
