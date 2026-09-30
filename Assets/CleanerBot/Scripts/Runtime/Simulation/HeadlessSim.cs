using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    /// <summary>Builds an independent simulation (own grid) for a house — used for batch runs and planner comparison.</summary>
    public static class HeadlessSim
    {
        public static SimulationCore Create(HouseInstance house, string planner, int seed, RobotSpec spec, float cellSize, out string error)
        {
            error = null;
            if (house == null || house.floor == null) { error = "no HouseInstance"; return null; }
            if (!NavMesh.SamplePosition(house.robotSpawn, out var hit, 1f, NavMesh.AllAreas))
            { error = "robot spawn is not on the NavMesh"; return null; }

            var b = house.floor.GetComponent<Renderer>().bounds;
            var grid = new CoverageGrid(new Vector2(b.min.x, b.min.z), new Vector2(b.size.x, b.size.z), cellSize);
            grid.BuildCleanableMask(hit.position, spec.brushRadius);
            var robot = new RobotSim(spec, hit.position, 0f);
            return new SimulationCore(grid, robot, PlannerFactory.Create(planner, seed));
        }
    }

    public static class CameraFramer
    {
        /// <summary>Place the camera so the whole floor is visible at the given pitch.</summary>
        /// <param name="leftUiFraction">Fraction of the screen width covered by the left UI panel; the view is shifted right by half of it.</param>
        public static void Frame(Camera cam, Bounds floor, float pitch = 60f, float contentHeight = 2.0f, float leftUiFraction = 0.2f)
        {
            float fov = cam.fieldOfView;
            cam.usePhysicalProperties = leftUiFraction > 0f;
            if (cam.usePhysicalProperties)
            {
                cam.lensShift = new Vector2(-leftUiFraction * 0.5f, 0f);
                cam.fieldOfView = fov;
            }
            var rot = Quaternion.Euler(pitch, 0f, 0f);
            cam.transform.rotation = rot;

            // 8 corners of the house volume (floor + tall furniture)
            var min = new Vector3(floor.min.x - 0.1f, 0f, floor.min.z - 0.1f);
            var max = new Vector3(floor.max.x + 0.1f, contentHeight, floor.max.z + 0.1f);
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
                corners[i] = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);

            float left = leftUiFraction + 0.02f;
            float dist = Mathf.Max(floor.size.x, floor.size.z) * 0.5f;
            for (int iter = 0; iter < 200; iter++)
            {
                cam.transform.position = floor.center - rot * Vector3.forward * dist;
                bool fits = true;
                foreach (var c in corners)
                {
                    var v = cam.WorldToViewportPoint(c);
                    if (v.z <= 0f || v.x < left || v.x > 0.98f || v.y < 0.03f || v.y > 0.97f) { fits = false; break; }
                }
                if (fits) break;
                dist *= 1.03f;
            }
        }
    }
}
