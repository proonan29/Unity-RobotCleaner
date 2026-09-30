using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    /// <summary>Builds the robot vacuum GameObject (visuals without colliders + NavMeshAgent + controller).</summary>
    public static class RobotFactory
    {
        public static RobotController Create(Vector3 position, IMaterialProvider mats)
        {
            var root = new GameObject("CleanerBot");
            root.transform.position = position;

            // Agent first so RobotController's RequireComponent is satisfied with our settings.
            var agent = root.AddComponent<NavMeshAgent>();
            agent.agentTypeID = 0;
            agent.baseOffset = 0f;
            agent.radius = 0.17f;
            agent.height = 0.1f;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            NoCollider(Prims.Cyl(visual, "Body", Vector3.zero, 0.17f, 0.08f, mats.Get("RobotBody", new Color(0.15f, 0.15f, 0.17f))));
            NoCollider(Prims.Cyl(visual, "TopPlate", new Vector3(0, 0.08f, 0), 0.15f, 0.01f, mats.Get("RobotTop", new Color(0.75f, 0.77f, 0.80f))));
            NoCollider(Prims.Box(visual, "Bumper", new Vector3(0, 0.045f, 0.13f), new Vector3(0.22f, 0.05f, 0.08f), mats.Get("RobotAccent", new Color(0.10f, 0.65f, 0.95f))));
            NoCollider(Prims.Cyl(visual, "Lidar", new Vector3(0, 0.09f, -0.03f), 0.04f, 0.025f, mats.Get("RobotBody", new Color(0.15f, 0.15f, 0.17f))));

            // Path preview line
            var lineGo = new GameObject("PathLine");
            lineGo.transform.SetParent(root.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = 0.03f;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = mats.Get("PathLine", new Color(1f, 0.55f, 0.1f));

            return root.AddComponent<RobotController>();
        }

        static void NoCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
        }
    }
}
