using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CleanerBot.UI
{
    /// <summary>Coverage-over-time line chart drawn with Painter2D. Supports several series (planner comparison).</summary>
    public class TimelineChart : VisualElement
    {
        public struct Series
        {
            public string name;
            public Color color;
            public List<Vector2> points;   // (seconds, percent)
        }

        const float PadLeft = 40f, PadRight = 12f, PadTop = 10f, PadBottom = 22f;
        static readonly Color GridColor = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color AxisColor = new Color(1f, 1f, 1f, 0.25f);
        static readonly Color TextColor = new Color(0.6f, 0.62f, 0.66f);

        readonly List<Series> _series = new List<Series>();
        float _maxTime = 60f;

        public TimelineChart()
        {
            AddToClassList("timeline-chart");
            generateVisualContent += Draw;
        }

        public void SetSeries(IEnumerable<Series> series)
        {
            _series.Clear();
            _series.AddRange(series);
            _maxTime = 60f;
            foreach (var s in _series)
                if (s.points != null && s.points.Count > 0)
                    _maxTime = Mathf.Max(_maxTime, s.points[s.points.Count - 1].x);
            MarkDirtyRepaint();
        }

        static float NiceStep(float range)
        {
            float[] steps = { 60, 120, 300, 600, 900, 1200, 1800, 3600 };
            foreach (var s in steps) if (range / s <= 6) return s;
            return 3600;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 40 || r.height < 40) return;
            float x0 = PadLeft, x1 = r.width - PadRight, y0 = PadTop, y1 = r.height - PadBottom;
            float w = x1 - x0, h = y1 - y0;
            var p = ctx.painter2D;

            // horizontal grid + % labels
            p.lineWidth = 1f;
            for (int pct = 0; pct <= 100; pct += 25)
            {
                float y = y1 - h * pct / 100f;
                p.strokeColor = pct == 0 ? AxisColor : GridColor;
                p.BeginPath(); p.MoveTo(new Vector2(x0, y)); p.LineTo(new Vector2(x1, y)); p.Stroke();
                ctx.DrawText($"{pct}%", new Vector2(4f, y - 7f), 10f, TextColor);
            }

            // vertical grid + minute labels
            float step = NiceStep(_maxTime);
            for (float t = 0; t <= _maxTime + 0.01f; t += step)
            {
                float x = x0 + w * t / _maxTime;
                p.strokeColor = t == 0 ? AxisColor : GridColor;
                p.BeginPath(); p.MoveTo(new Vector2(x, y0)); p.LineTo(new Vector2(x, y1)); p.Stroke();
                ctx.DrawText($"{t / 60f:0}m", new Vector2(Mathf.Min(x - 8f, x1 - 22f), y1 + 4f), 10f, TextColor);
            }

            // series
            foreach (var s in _series)
            {
                if (s.points == null || s.points.Count < 2) continue;
                p.strokeColor = s.color;
                p.lineWidth = 2.5f;
                p.lineJoin = LineJoin.Round;
                p.BeginPath();
                for (int i = 0; i < s.points.Count; i++)
                {
                    var pt = s.points[i];
                    var v = new Vector2(x0 + w * Mathf.Clamp01(pt.x / _maxTime), y1 - h * Mathf.Clamp01(pt.y / 100f));
                    if (i == 0) p.MoveTo(v); else p.LineTo(v);
                }
                p.Stroke();
            }
        }
    }
}
