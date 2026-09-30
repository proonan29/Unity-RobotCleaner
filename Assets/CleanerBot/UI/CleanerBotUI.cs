using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace CleanerBot.UI
{
    /// <summary>Binds the UI Toolkit screen (CleanerBotUI.uxml) to the AppController.</summary>
    [RequireComponent(typeof(UIDocument))]
    public class CleanerBotUI : MonoBehaviour
    {
        public AppController app;
        [Tooltip("Live stats refresh interval (real-time seconds)")] public float refreshInterval = 0.2f;
        [Tooltip("Simulated time limit for 'Compare planners' (minutes)")] public float compareMinutes = 60f;

        static readonly float[] Speeds = { 1, 2, 5, 10, 20, 50, 100 };
        public static readonly Color FrontierColor = new Color(0.25f, 0.82f, 0.78f);
        public static readonly Color RandomColor = new Color(0.96f, 0.63f, 0.27f);

        DropdownField _house, _planner;
        Button _start, _pause, _reset, _summaryBtn, _compareBtn, _close;
        SliderInt _speed;
        Label _speedValue, _status, _time, _distance, _passes, _area, _summaryTitle, _summarySubtitle;
        Toggle _remaining;
        ProgressBar _bar;
        VisualElement _summary, _summaryStats, _legend, _compareTable;
        TimelineChart _chart;
        float _nextRefresh;
        List<string> _houseKeys;

        void Start()
        {
            if (app == null) app = FindAnyObjectByType<AppController>();
            var root = GetComponent<UIDocument>().rootVisualElement;

            _house = root.Q<DropdownField>("houseDropdown");
            _planner = root.Q<DropdownField>("plannerDropdown");
            _start = root.Q<Button>("startButton");
            _pause = root.Q<Button>("pauseButton");
            _reset = root.Q<Button>("resetButton");
            _summaryBtn = root.Q<Button>("summaryButton");
            _compareBtn = root.Q<Button>("compareButton");
            _close = root.Q<Button>("closeSummaryButton");
            _speed = root.Q<SliderInt>("speedSlider");
            _speedValue = root.Q<Label>("speedValue");
            _remaining = root.Q<Toggle>("remainingToggle");
            _status = root.Q<Label>("statusLabel");
            _bar = root.Q<ProgressBar>("coverageBar");
            _time = root.Q<Label>("timeValue");
            _distance = root.Q<Label>("distanceValue");
            _passes = root.Q<Label>("passesValue");
            _area = root.Q<Label>("areaValue");
            _summary = root.Q("summaryPanel");
            _summaryTitle = root.Q<Label>("summaryTitle");
            _summarySubtitle = root.Q<Label>("summarySubtitle");
            _summaryStats = root.Q("summaryStats");
            _legend = root.Q("chartLegend");
            _compareTable = root.Q("compareTable");

            _chart = new TimelineChart();
            root.Q("chartContainer").Add(_chart);

            // House list (display names), planner list
            _houseKeys = new List<string>(HousePresets.Names);
            var houseNames = new List<string>();
            foreach (var k in _houseKeys) houseNames.Add(HousePresets.DisplayName(k));
            _house.choices = houseNames;
            int hi = Mathf.Max(0, _houseKeys.IndexOf(app.CurrentHouse));
            _house.SetValueWithoutNotify(houseNames[hi]);
            _house.RegisterValueChangedCallback(e =>
            {
                int i = houseNames.IndexOf(e.newValue);
                if (i >= 0) { app.LoadHouse(_houseKeys[i]); HideSummary(); }
            });

            _planner.choices = new List<string>(PlannerFactory.Names);
            _planner.SetValueWithoutNotify(app.runner.planner);

            _start.clicked += () => { app.StartCleaning(_planner.value); HideSummary(); };
            _pause.clicked += () => app.SetPaused(!app.IsPaused);
            _reset.clicked += () => { app.ResetRun(); HideSummary(); };
            _summaryBtn.clicked += () => { if (app.Core != null) ShowSummary(app.Core.Stats, null); };
            _compareBtn.clicked += RunComparison;
            _close.clicked += HideSummary;

            _speed.SetValueWithoutNotify(NearestSpeedIndex(app.Speed));
            _speed.RegisterValueChangedCallback(e => ApplySpeed(e.newValue));
            ApplySpeed(_speed.value);

            _remaining.SetValueWithoutNotify(app.coverage.showRemaining);
            _remaining.RegisterValueChangedCallback(e => app.coverage.SetShowRemaining(e.newValue));

            app.Finished += OnFinished;
            app.HouseChanged += () =>
            {
                int idx = _houseKeys.IndexOf(app.CurrentHouse);
                if (idx >= 0) _house.SetValueWithoutNotify(houseNames[idx]);
                RefreshLive(true);
            };
            RefreshLive(true);
        }

        void OnDestroy()
        {
            if (app != null) app.Finished -= OnFinished;
        }

        void OnFinished(SimulationCore core) => ShowSummary(core.Stats, null);

        void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshInterval;
            RefreshLive(false);
        }

        static int NearestSpeedIndex(float speed)
        {
            int best = 0;
            for (int i = 1; i < Speeds.Length; i++)
                if (Mathf.Abs(Speeds[i] - speed) < Mathf.Abs(Speeds[best] - speed)) best = i;
            return best;
        }

        void ApplySpeed(int index)
        {
            index = Mathf.Clamp(index, 0, Speeds.Length - 1);
            app.Speed = Speeds[index];
            _speedValue.text = $"x{Speeds[index]:0}";
        }

        static string Mmss(float s) => s < 0 ? "-" : $"{(int)(s / 60)}:{(int)(s % 60):00}";
        static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        void RefreshLive(bool force)
        {
            int si = NearestSpeedIndex(app.Speed);
            if (_speed.value != si) _speed.SetValueWithoutNotify(si);
            _speedValue.text = $"x{app.Speed:0.#}";

            var core = app.Core;
            if (core == null)
            {
                _status.text = $"Ready — {HousePresets.DisplayName(app.CurrentHouse)}";
                _bar.value = 0; _bar.title = "0.0 %";
                _time.text = "0:00"; _distance.text = "0.0 m"; _passes.text = "-";
                _area.text = app.coverage.Ready ? $"{F1(app.coverage.Grid.GetStats().cleanableM2)} m²" : "-";
                _pause.SetEnabled(false);
                _pause.text = "Pause";
                return;
            }

            var s = core.Stats;
            _status.text = core.State switch
            {
                SimState.Running => $"Cleaning ({s.planner})",
                SimState.Paused => "Paused",
                SimState.Done => $"Finished — {s.endReason}",
                _ => "Ready",
            };
            _bar.value = s.coveragePercent;
            _bar.title = $"{F1(s.coveragePercent)} %";
            _time.text = Mmss(s.simTime);
            _distance.text = $"{F1(s.distance)} m";
            _passes.text = s.averagePasses > 0 ? s.averagePasses.ToString("0.00", CultureInfo.InvariantCulture) : "-";
            _area.text = $"{F1(s.cleanableM2)} m²";
            _pause.SetEnabled(core.State == SimState.Running || core.State == SimState.Paused);
            _pause.text = core.State == SimState.Paused ? "Resume" : "Pause";
        }

        // ---- summary ----

        void HideSummary() => _summary.AddToClassList("hidden");

        void ShowSummary(SimStats s, List<SimStats> comparison)
        {
            _summary.RemoveFromClassList("hidden");
            _summaryTitle.text = comparison == null ? "Cleaning summary" : "Planner comparison";
            _summarySubtitle.text = comparison == null
                ? $"{HousePresets.DisplayName(app.CurrentHouse)} · planner {s.planner} · {s.endReason}"
                : $"{HousePresets.DisplayName(app.CurrentHouse)} · headless run, limit {compareMinutes:0} min";

            _summaryStats.Clear();
            _compareTable.Clear();
            _legend.Clear();

            if (comparison == null)
            {
                AddTile("Coverage", $"{F1(s.coveragePercent)} %");
                AddTile("Time (sim)", Mmss(s.simTime));
                AddTile("Time to 90 %", Mmss(s.timeTo90));
                AddTile("Time to 95 %", Mmss(s.timeTo95));
                AddTile("Distance", $"{F1(s.distance)} m");
                AddTile("Avg passes", s.averagePasses.ToString("0.00", CultureInfo.InvariantCulture));
                AddTile("Overlap", $"{F1(s.overlapPercent)} %");
                AddTile("Turning", $"{s.rotationDeg / 360f:0} rev");
                AddTile("Cleaned", $"{F1(s.coveredM2)} / {F1(s.cleanableM2)} m²");
                AddTile("Goals", $"{s.goals} ({s.failedGoals} failed)");

                _chart.SetSeries(new[] { MakeSeries(s) });
                AddLegend(s.planner);
            }
            else
            {
                var series = new List<TimelineChart.Series>();
                foreach (var c in comparison) { series.Add(MakeSeries(c)); AddLegend(c.planner); }
                _chart.SetSeries(series);
                BuildCompareTable(comparison);
            }
        }

        static TimelineChart.Series MakeSeries(SimStats s) => new TimelineChart.Series
        {
            name = s.planner,
            color = s.planner == "Random" ? RandomColor : FrontierColor,
            points = s.timeline,
        };

        void AddTile(string key, string value)
        {
            var tile = new VisualElement();
            tile.AddToClassList("stat-tile");
            var k = new Label(key); k.AddToClassList("tile-key");
            var v = new Label(value); v.AddToClassList("tile-value");
            tile.Add(k); tile.Add(v);
            _summaryStats.Add(tile);
        }

        void AddLegend(string planner)
        {
            var item = new VisualElement(); item.AddToClassList("legend-item");
            var sw = new VisualElement(); sw.AddToClassList("legend-swatch");
            sw.AddToClassList(planner == "Random" ? "legend-random" : "legend-frontier");
            var l = new Label(planner); l.AddToClassList("legend-label");
            item.Add(sw); item.Add(l);
            _legend.Add(item);
        }

        void RunComparison()
        {
            _status.text = "Comparing planners…";
            var results = app.ComparePlanners(compareMinutes);
            if (results.Count > 0) ShowSummary(results[0], results);
        }

        void BuildCompareTable(List<SimStats> rows)
        {
            string[] headers = { "PLANNER", "COVERAGE", "TIME", "TO 90 %", "DISTANCE", "PASSES" };
            _compareTable.Add(Row(headers, true, -1));

            // best (highest coverage, then shortest time)
            int best = 0;
            for (int i = 1; i < rows.Count; i++)
                if (rows[i].coveragePercent > rows[best].coveragePercent + 0.1f ||
                    (Mathf.Abs(rows[i].coveragePercent - rows[best].coveragePercent) <= 0.1f && rows[i].simTime < rows[best].simTime))
                    best = i;

            for (int i = 0; i < rows.Count; i++)
            {
                var s = rows[i];
                _compareTable.Add(Row(new[]
                {
                    s.planner, $"{F1(s.coveragePercent)} %", Mmss(s.simTime), Mmss(s.timeTo90),
                    $"{s.distance:0} m", s.averagePasses.ToString("0.00", CultureInfo.InvariantCulture),
                }, false, i == best ? 0 : -1));
            }
        }

        static VisualElement Row(string[] cells, bool header, int highlightCol)
        {
            var row = new VisualElement();
            row.AddToClassList("compare-row");
            if (header) row.AddToClassList("compare-header");
            for (int i = 0; i < cells.Length; i++)
            {
                var l = new Label(cells[i]);
                l.AddToClassList("compare-cell");
                if (i == highlightCol) l.AddToClassList("compare-best");
                row.Add(l);
            }
            return row;
        }
    }
}
