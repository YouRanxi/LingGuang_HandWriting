using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace LingGuangInk
{
    public class LaserTrailRenderer : FrameworkElement
    {
        private class LaserPoint
        {
            public Point Position;
            public DateTime Time;
        }

        private readonly List<LaserPoint> _points = new List<LaserPoint>();
        private readonly DispatcherTimer _fadeTimer;
        private Point? _currentTip = null;
        private bool _isActive = false;

        public Color LaserColor { get; set; }
        public double LifeTimeMs { get; set; }
        public double MaxThickness { get; set; }

        public LaserTrailRenderer()
        {
            LaserColor = Color.FromRgb(255, 50, 70); // 激光红
            LifeTimeMs = 1200; // 1.2秒平滑消逝
            MaxThickness = 7.0;

            IsHitTestVisible = false; // 不阻挡鼠标事件

            _fadeTimer = new DispatcherTimer();
            _fadeTimer.Interval = TimeSpan.FromMilliseconds(25); // ~40 FPS 刷新
            _fadeTimer.Tick += OnFadeTick;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (!active)
            {
                _currentTip = null;
                _points.Clear();
                _fadeTimer.Stop();
                InvalidateVisual();
            }
        }

        public void AddPoint(Point pt)
        {
            if (!_isActive) return;

            _currentTip = pt;
            _points.Add(new LaserPoint { Position = pt, Time = DateTime.UtcNow });

            if (!_fadeTimer.IsEnabled)
            {
                _fadeTimer.Start();
            }
            InvalidateVisual();
        }

        public void EndStroke()
        {
            _currentTip = null;
            InvalidateVisual();
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            if (_points.Count == 0 && !_currentTip.HasValue)
            {
                _fadeTimer.Stop();
                InvalidateVisual();
                return;
            }

            DateTime now = DateTime.UtcNow;
            _points.RemoveAll(p => (now - p.Time).TotalMilliseconds > LifeTimeMs);

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            DateTime now = DateTime.UtcNow;

            // 1. 绘制消逝中的激光光轨
            if (_points.Count > 1)
            {
                for (int i = 0; i < _points.Count - 1; i++)
                {
                    var p1 = _points[i];
                    var p2 = _points[i + 1];

                    double age1 = (now - p1.Time).TotalMilliseconds;
                    double lifeRatio = Math.Max(0.0, Math.Min(1.0, 1.0 - (age1 / LifeTimeMs)));

                    if (lifeRatio <= 0.01) continue;

                    byte alpha = (byte)(lifeRatio * 220);
                    Color trailColor = Color.FromArgb(alpha, LaserColor.R, LaserColor.G, LaserColor.B);
                    double thickness = Math.Max(1.5, MaxThickness * lifeRatio);

                    // 外层微光光晕
                    Color glowColor = Color.FromArgb((byte)(alpha * 0.35), LaserColor.R, LaserColor.G, LaserColor.B);
                    Pen glowPen = new Pen(new SolidColorBrush(glowColor), thickness * 2.2);
                    glowPen.StartLineCap = PenLineCap.Round;
                    glowPen.EndLineCap = PenLineCap.Round;
                    glowPen.Freeze();
                    dc.DrawLine(glowPen, p1.Position, p2.Position);

                    // 内层高亮核心光束
                    Pen corePen = new Pen(new SolidColorBrush(trailColor), thickness);
                    corePen.StartLineCap = PenLineCap.Round;
                    corePen.EndLineCap = PenLineCap.Round;
                    corePen.Freeze();
                    dc.DrawLine(corePen, p1.Position, p2.Position);
                }
            }

            // 2. 绘制当前激光笔尖亮点光芒
            if (_currentTip.HasValue)
            {
                Point tip = _currentTip.Value;

                // 激光扩散光晕
                Brush haloBrush = new SolidColorBrush(Color.FromArgb(90, LaserColor.R, LaserColor.G, LaserColor.B));
                haloBrush.Freeze();
                dc.DrawEllipse(haloBrush, null, tip, 12, 12);

                // 激光中层亮核
                Brush midBrush = new SolidColorBrush(LaserColor);
                midBrush.Freeze();
                dc.DrawEllipse(midBrush, null, tip, 5.5, 5.5);

                // 激光中心白热核
                Brush whiteCore = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
                whiteCore.Freeze();
                dc.DrawEllipse(whiteCore, null, tip, 2.5, 2.5);
            }
        }
    }
}
