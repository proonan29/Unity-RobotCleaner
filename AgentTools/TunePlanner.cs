using CleanerBot;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Headless Frontier-planner parameter sweep (no recompile needed: run with run_script).</summary>
public static class TunePlanner
{
    public static string Run(float minGain, float extendGain, float openW, float headingW, float slack)
    {
        var house = Object.FindAnyObjectByType<HouseInstance>();
        NavMesh.SamplePosition(house.robotSpawn, out var hit, 1f, NavMesh.AllAreas);
        var b = house.floor.GetComponent<Renderer>().bounds;
        var grid = new CoverageGrid(new Vector2(b.min.x, b.min.z), new Vector2(b.size.x, b.size.z), 0.05f);
        var spec = RobotSpec.Default;
        grid.BuildCleanableMask(hit.position, spec.brushRadius);
        var p = new FrontierPlanner
        {
            minGainFraction = minGain, extendGainFraction = extendGain,
            openAreaWeight = openW, headingWeight = headingW, slack = slack,
        };
        var core = new SimulationCore(grid, new RobotSim(spec, hit.position, 0f), p);
        float wall = core.RunToEnd(0.05f, 3600f);
        var s = core.Stats;
        return $"[{minGain},{extendGain},{openW},{headingW},{slack}] end={s.endReason} cov={s.coveragePercent:0.00}% " +
               $"t={s.simTime / 60f:0.0}min t95={s.timeTo95 / 60f:0.0} dist={s.distance:0}m turns={s.rotationDeg / 360f:0}rev " +
               $"goals={s.goals} failed={s.failedGoals} passes={s.averagePasses:0.00} wall={wall:0}ms";
    }
}
