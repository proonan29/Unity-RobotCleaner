using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CleanerBot
{
    /// <summary>
    /// Application facade used by the UI (and CLI): switch houses at runtime, start/pause/reset cleaning,
    /// change speed and compare planners.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public class AppController : MonoBehaviour
    {
        public HouseInstance house;
        public RobotController robot;
        public CoverageManager coverage;
        public SimulationRunner runner;
        public Camera cam;
        public float cameraPitch = 60f;

        public event Action HouseChanged;
        public event Action<SimulationCore> Finished;

        IMaterialProvider _mats;

        public string CurrentHouse => house != null ? house.layoutName : "";
        public SimulationCore Core => runner != null ? runner.Core : null;

        void Awake()
        {
            if (house == null) house = FindAnyObjectByType<HouseInstance>();
            if (robot == null) robot = FindAnyObjectByType<RobotController>();
            if (coverage == null) coverage = FindAnyObjectByType<CoverageManager>();
            if (runner == null) runner = FindAnyObjectByType<SimulationRunner>();
            if (cam == null) cam = Camera.main;
        }

        void Start()
        {
            if (cam != null && house != null) CameraFramer.Frame(cam, house.floorBounds, cameraPitch);
        }

        void OnEnable() { if (runner != null) runner.Finished += OnRunnerFinished; }
        void OnDisable() { if (runner != null) runner.Finished -= OnRunnerFinished; }
        void OnRunnerFinished(SimulationCore core) => Finished?.Invoke(core);

        /// <summary>Replace the current house with a preset, rebuild the NavMesh and reset the robot/coverage.</summary>
        public void LoadHouse(string preset)
        {
            var layout = HousePresets.Get(preset);
            _mats ??= new RuntimeMaterialProvider();

            if (house != null)
            {
                house.gameObject.SetActive(false);   // removes its NavMesh data immediately
                Destroy(house.gameObject);
            }

            house = HouseGenerator.Build(layout, _mats);
            house.RebuildNavMesh();

            var spawn = NavMesh.SamplePosition(house.robotSpawn, out var hit, 1f, NavMesh.AllAreas) ? hit.position : house.robotSpawn;
            robot.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, house.robotSpawnYaw, 0f));

            coverage.house = house;
            coverage.Initialize();
            runner.SetHome(spawn, house.robotSpawnYaw);
            runner.ResetToDock();

            if (cam != null) CameraFramer.Frame(cam, house.floorBounds, cameraPitch);
            HouseChanged?.Invoke();
        }

        public void StartCleaning(string planner) => runner.StartCleaning(planner);

        public void SetPaused(bool paused) => runner.SetPaused(paused);

        public bool IsPaused => Core != null && Core.State == SimState.Paused;

        public void ResetRun() => runner.ResetToDock();

        public BatterySpec Battery
        {
            get => runner.battery;
            set => runner.battery = value;
        }

        public float Speed
        {
            get => runner.speedMultiplier;
            set => runner.speedMultiplier = Mathf.Clamp(value, 0.1f, 100f);
        }

        /// <summary>Run every planner headless on the current house (same start, same limits).</summary>
        public List<SimStats> ComparePlanners(float maxMinutes = 60f, int seed = 1)
        {
            var results = new List<SimStats>();
            foreach (var name in PlannerFactory.Names)
            {
                var core = HeadlessSim.Create(house, name, seed, runner.spec, runner.battery, coverage.cellSize, out var err);
                if (core == null) { Debug.LogWarning($"[CleanerBot] compare: {err}"); continue; }
                core.RunToEnd(runner.fixedStep, maxMinutes * 60f);
                results.Add(core.Stats);
            }
            return results;
        }
    }
}
