using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    [System.Serializable]
    public struct RobotSpec
    {
        public float speed;              // m/s
        public float angularSpeed;       // deg/s
        public float rotateInPlaceAngle; // turn on the spot when heading error exceeds this (deg)
        public float brushRadius;        // m

        public static RobotSpec Default => new RobotSpec
        {
            speed = 0.3f, angularSpeed = 120f, rotateInPlaceAngle = 15f, brushRadius = 0.16f,
        };
    }

    /// <summary>
    /// Deterministic differential-drive kinematics that follow a NavMesh path.
    /// Independent of the player loop: advance it with Step(dt).
    /// </summary>
    public class RobotSim
    {
        public RobotSpec Spec;
        public Vector3 Position;
        public float Heading;               // yaw in degrees (Unity convention: 0 = +Z, 90 = +X)

        public float Distance { get; private set; }
        public float RotationDeg { get; private set; }
        public int PathsFollowed { get; private set; }
        public Vector3? Goal => HasPath ? _path[_path.Count - 1] : (Vector3?)null;
        public bool HasPath => _idx < _path.Count;
        public IReadOnlyList<Vector3> Path => _path;
        public int PathIndex => _idx;

        readonly List<Vector3> _path = new List<Vector3>();
        readonly NavMeshPath _navPath = new NavMeshPath();
        int _idx;

        public RobotSim(RobotSpec spec, Vector3 position, float heading)
        {
            Spec = spec;
            Position = position;
            Heading = heading;
        }

        public bool SetPathTo(Vector3 goal)
        {
            if (!NavMesh.CalculatePath(Position, goal, NavMesh.AllAreas, _navPath) ||
                _navPath.status != NavMeshPathStatus.PathComplete)
                return false;
            _path.Clear();
            _path.AddRange(_navPath.corners);
            _idx = 1;                                   // corner 0 is the current position
            if (_path.Count <= 1) { _path.Clear(); _idx = 0; return false; }
            PathsFollowed++;
            return true;
        }

        /// <summary>Path through several waypoints (NavMesh path per leg). False if any leg is unreachable.</summary>
        public bool SetPathThrough(IList<Vector3> waypoints)
        {
            var from = Position;
            var tmp = new List<Vector3> { Position };
            foreach (var wp in waypoints)
            {
                // snap onto the NavMesh (cell centres can sit a few cm off the mesh edge)
                var w = NavMesh.SamplePosition(wp, out var snap, 0.15f, NavMesh.AllAreas) ? snap.position : wp;
                if (!NavMesh.CalculatePath(from, w, NavMesh.AllAreas, _navPath) ||
                    _navPath.status != NavMeshPathStatus.PathComplete)
                    return false;
                var c = _navPath.corners;
                for (int i = 1; i < c.Length; i++) tmp.Add(c[i]);
                from = c[c.Length - 1];
            }
            if (tmp.Count <= 1) return false;
            _path.Clear();
            _path.AddRange(tmp);
            _idx = 1;
            PathsFollowed++;
            return true;
        }

        public void ClearPath()
        {
            _path.Clear();
            _idx = 0;
        }

        /// <summary>Advance by dt seconds (rotate in place for big heading errors, otherwise drive).</summary>
        public void Step(float dt)
        {
            int guard = 64;
            while (dt > 1e-6f && HasPath && guard-- > 0)
            {
                var target = _path[_idx];
                var to = target - Position;
                to.y = 0f;
                float d = to.magnitude;
                if (d < 1e-4f) { _idx++; continue; }

                float desired = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float diff = Mathf.DeltaAngle(Heading, desired);

                if (Mathf.Abs(diff) > Spec.rotateInPlaceAngle)
                {
                    float maxRot = Spec.angularSpeed * dt;
                    float rot = Mathf.Clamp(diff, -maxRot, maxRot);
                    Heading = Mathf.Repeat(Heading + rot, 360f);
                    RotationDeg += Mathf.Abs(rot);
                    dt -= Mathf.Abs(rot) / Spec.angularSpeed;
                    continue;
                }

                // small correction while driving
                Heading = Mathf.Repeat(desired, 360f);
                RotationDeg += Mathf.Abs(diff);

                float step = Spec.speed * dt;
                if (step >= d)
                {
                    Position = target;
                    Distance += d;
                    dt -= d / Spec.speed;
                    _idx++;
                }
                else
                {
                    Position += to / d * step;
                    Distance += step;
                    dt = 0f;
                }
            }
        }
    }
}
