using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CleanerBot
{
    /// <summary>
    /// Robot vacuum driven by a NavMeshAgent.
    /// Phase 1 modes: Idle (external MoveTo calls), Wander (random reachable goals), ClickToMove.
    /// A cleaning planner (phase 3) will call MoveTo() instead of Wander.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class RobotController : MonoBehaviour
    {
        public enum Mode { Idle, Wander, ClickToMove }

        [Header("Behaviour")]
        public Mode mode = Mode.Wander;

        [Header("Robot spec (typical robot vacuum)")]
        public float radius = 0.17f;
        public float height = 0.1f;
        public float speed = 0.3f;          // m/s
        public float angularSpeed = 180f;   // deg/s
        public float acceleration = 1.5f;

        [Header("Debug")]
        public bool drawPath = true;

        public float DistanceTraveled { get; private set; }
        public int GoalsReached { get; private set; }
        public Vector3? CurrentGoal { get; private set; }
        public NavMeshAgent Agent => _agent;

        NavMeshAgent _agent;
        LineRenderer _line;
        Vector3 _lastPos;
        Bounds _area;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.radius = radius;
            _agent.height = height;
            _agent.speed = speed;
            _agent.angularSpeed = angularSpeed;
            _agent.acceleration = acceleration;
            _agent.stoppingDistance = 0.03f;
            _agent.autoBraking = true;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            _line = GetComponentInChildren<LineRenderer>();
        }

        void Start()
        {
            var house = FindAnyObjectByType<HouseInstance>();
            _area = house != null ? house.floorBounds : new Bounds(transform.position, new Vector3(10, 0, 10));

            if (!_agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 1f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
            _lastPos = transform.position;
        }

        /// <summary>Plan a path to a world position. Returns false if unreachable.</summary>
        public bool MoveTo(Vector3 worldPos)
        {
            if (_agent == null || !_agent.isOnNavMesh) return false;
            if (!NavMesh.SamplePosition(worldPos, out var hit, 0.5f, NavMesh.AllAreas)) return false;

            var path = new NavMeshPath();
            if (!_agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                return false;

            _agent.SetPath(path);
            CurrentGoal = hit.position;
            return true;
        }

        public void Stop()
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();
            CurrentGoal = null;
        }

        public bool HasArrived =>
            _agent != null && _agent.isOnNavMesh && !_agent.pathPending &&
            (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.02f);

        void Update()
        {
            // Odometry
            var p = transform.position;
            DistanceTraveled += Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(_lastPos.x, 0, _lastPos.z));
            _lastPos = p;

            if (CurrentGoal.HasValue && HasArrived)
            {
                GoalsReached++;
                CurrentGoal = null;
            }

            switch (mode)
            {
                case Mode.Wander:
                    if (!CurrentGoal.HasValue) PickRandomGoal();
                    break;
                case Mode.ClickToMove:
                    HandleClick();
                    break;
            }

            if (drawPath) UpdatePathLine();
        }

        void PickRandomGoal()
        {
            for (int i = 0; i < 30; i++)
            {
                var q = new Vector3(
                    UnityEngine.Random.Range(_area.min.x, _area.max.x), 0f,
                    UnityEngine.Random.Range(_area.min.z, _area.max.z));
                if (MoveTo(q)) return;
            }
        }

        void HandleClick()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null || !mouse.leftButton.wasPressedThisFrame) return;
            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, 100f)) MoveTo(hit.point);
#endif
        }

        void UpdatePathLine()
        {
            if (_line == null) return;
            if (!_agent.hasPath) { _line.positionCount = 0; return; }
            var corners = _agent.path.corners;
            _line.positionCount = corners.Length;
            for (int i = 0; i < corners.Length; i++)
                _line.SetPosition(i, corners[i] + Vector3.up * 0.02f);
        }
    }
}
