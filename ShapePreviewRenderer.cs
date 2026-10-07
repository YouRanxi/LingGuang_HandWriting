using System;
using System.Windows;
using System.Windows.Media;

namespace LingGuangInk
{
    public class ShapePreviewRenderer : FrameworkElement
    {
        public DrawingToolType Tool { get; set; }
        public Point? StartPoint { get; set; }
        public Point? CurrentPoint { get; set; }
        public Color ShapeColor { get; set; }
        public double Thickness { get; set; }

        private Pen _cachedPen;
        private Color _cachedPenColor;
        private double _cachedPenThickness;

        public ShapePreviewRenderer()
        {
            IsHitTestVisible = false;
            ShapeColor = Color.FromRgb(255, 77, 79);
            Thickness = 3.0;
        }

        public void SetPreview(DrawingToolType tool, Point start, Point current, Color color, double thickness)
        {
            Tool = tool;
            StartPoint = start;
            CurrentPoint = current;
            ShapeColor = color;
            Thickness = thickness;
            InvalidateVisual();
        }

        /// <summary>
        /// 只更新外观参数并立即重绘。
        /// 用于拖拽过程中改颜色 / 线宽时刷新当前预览 —— 直接赋属性不会触发重绘，
        /// 预览要等到下一次鼠标移动才更新。
        /// </summary>
        public void UpdateAppearance(Color color, double thickness)
        {
            ShapeColor = color;
            Thickness = thickness;
            InvalidateVisual();
        }

        public void Clear()
        {
            StartPoint = null;
            CurrentPoint = null;
            InvalidateVisual();
        }

        /// <summary>按需构建并缓存 Pen：OnRender 每帧都会调用，不必每次重新分配。</summary>
        private Pen GetPen()
        {
            if (_cachedPen != null && _cachedPenColor == ShapeColor && _cachedPenThickness == Thickness)
            {
                return _cachedPen;
            }

            Pen pen = new Pen(new SolidColorBrush(ShapeColor), Thickness);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.Freeze();

            _cachedPen = pen;
            _cachedPenColor = ShapeColor;
            _cachedPenThickness = Thickness;
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (!StartPoint.HasValue || !CurrentPoint.HasValue) return;

            Point start = StartPoint.Value;
            Point end = CurrentPoint.Value;

            Pen pen = GetPen();

            switch (Tool)
            {
                case DrawingToolType.ShapeLine:
                    dc.DrawLine(pen, start, end);
                    break;

                case DrawingToolType.ShapeArrow:
                    DrawArrowPreview(dc, pen, start, end);
                    break;

                case DrawingToolType.ShapeRect:
                    double minX = Math.Min(start.X, end.X);
                    double maxX = Math.Max(start.X, end.X);
                    double minY = Math.Min(start.Y, end.Y);
                    double maxY = Math.Max(start.Y, end.Y);
                    dc.DrawRectangle(null, pen, new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY)));
                    break;

                case DrawingToolType.ShapeEllipse:
                    double cx = (start.X + end.X) / 2.0;
                    double cy = (start.Y + end.Y) / 2.0;
                    double rx = Math.Abs(end.X - start.X) / 2.0;
                    double ry = Math.Abs(end.Y - start.Y) / 2.0;
                    dc.DrawEllipse(null, pen, new Point(cx, cy), Math.Max(1, rx), Math.Max(1, ry));
                    break;
            }
        }

        private void DrawArrowPreview(DrawingContext dc, Pen pen, Point start, Point end)
        {
            dc.DrawLine(pen, start, end);

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 3) return;

            double angle = Math.Atan2(dy, dx);
            double arrowLength = Math.Max(16.0, Math.Min(36.0, Thickness * 3.5 + 8.0));
            double arrowAngle = 0.50;

            Point wing1 = new Point(
                end.X - arrowLength * Math.Cos(angle - arrowAngle),
                end.Y - arrowLength * Math.Sin(angle - arrowAngle));

            Point wing2 = new Point(
                end.X - arrowLength * Math.Cos(angle + arrowAngle),
                end.Y - arrowLength * Math.Sin(angle + arrowAngle));

            dc.DrawLine(pen, end, wing1);
            dc.DrawLine(pen, end, wing2);
        }
    }
}
