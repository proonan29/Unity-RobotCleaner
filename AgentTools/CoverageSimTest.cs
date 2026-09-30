using System.IO;
using CleanerBot;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Edit-mode verification of the coverage pipeline (no player loop needed):
/// drives a virtual robot along NavMesh paths to random goals, sweeps the CoverageGrid,
/// paints the overlay into the scene (object "__CoverageTest") and exports the map PNG.
/// Run Cleanup() afterwards to remove the test overlay.
/// </summary>
public static class CoverageSimTest
{
    public static string Run(int goals = 40, int seed = 42)
    {
        var house = Object.FindAnyObjectByType<HouseInstance>();
        var robot = Object.FindAnyObjectByType<RobotController>();
        var cm = Object.FindAnyObjectByType<CoverageManager>();
        if (house == null || robot == null || cm == null) return "missing house/robot/coverage";
        Cleanup();

        var b = house.floor.GetComponent<Renderer>().bounds;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var grid = new CoverageGrid(new Vector2(b.min.x, b.min.z), new Vector2(b.size.x, b.size.z), cm.cellSize);
        grid.BuildCleanableMask(robot.transform.position, cm.brushRadius);
        float maskMs = (float)sw.Elapsed.TotalMilliseconds;

        var rng = new System.Random(seed);
        NavMesh.SamplePosition(robot.transform.position, out var start, 1f, NavMesh.AllAreas);
        var pos = start.position;
        var path = new NavMeshPath();
        float dist = 0f;
        int reached = 0, tries = 0;
        var fb = house.floorBounds;
        grid.Stamp(pos, cm.brushRadius);

        while (reached < goals && tries < goals * 20)
        {
            tries++;
            var q = new Vector3(
                Mathf.Lerp(fb.min.x, fb.max.x, (float)rng.NextDouble()), 0f,
                Mathf.Lerp(fb.min.z, fb.max.z, (float)rng.NextDouble()));
            if (!NavMesh.SamplePosition(q, out var hit, 0.5f, NavMesh.AllAreas)) continue;
            if (!NavMesh.CalculatePath(pos, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            var c = path.corners;
            for (int i = 1; i < c.Length; i++)
            {
                grid.Sweep(c[i - 1], c[i], cm.brushRadius);
                dist += Vector3.Distance(c[i - 1], c[i]);
            }
            pos = hit.position;
            reached++;
        }
        float simMs = (float)sw.Elapsed.TotalMilliseconds - maskMs;

        var go = new GameObject("__CoverageTest");
        var painter = new CoveragePainter(grid, cm.overlayShader, go.transform, cm.overlayHeight);
        painter.Refresh(grid, true);

        var outPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots/coverage_test_map.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllBytes(outPath, painter.Texture.EncodeToPNG());

        var s = grid.GetStats();
        return $"goals={reached}/{goals} tries={tries} path_m={dist:0.0} sim_time_s={dist / robot.speed:0} " +
               $"coverage={s.coveragePercent:0.0}% covered_m2={s.coveredM2:0.00} cleanable_m2={s.cleanableM2:0.00} " +
               $"avg_passes={s.averagePasses:0.00} overlap={s.overlapPercent:0.0}% mask_ms={maskMs:0} sweep_ms={simMs:0} png={outPath}";
    }

    public static string Cleanup()
    {
        var go = GameObject.Find("__CoverageTest");
        if (go == null) return "nothing to clean";
        var r = go.GetComponentInChildren<Renderer>();
        if (r != null && r.sharedMaterial != null)
        {
            var tex = r.sharedMaterial.GetTexture("_CoverageTex");
            if (tex != null) Object.DestroyImmediate(tex);
            Object.DestroyImmediate(r.sharedMaterial);
        }
        Object.DestroyImmediate(go);
        return "cleaned";
    }
}
