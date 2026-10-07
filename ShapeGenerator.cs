using System;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace LingGuangInk
{
    public static class ShapeGenerator
    {
        public static Stroke CreateShapeStroke(DrawingToolType toolType, Point start, Point end, DrawingAttributes attributes)
        {
            StylusPointCollection points = new StylusPointCollection();

            switch (toolType)
            {
                case DrawingToolType.ShapeLine:
                    points.Add(new StylusPoint(start.X, start.Y));
                    points.Add(new StylusPoint(end.X, end.Y));
                    break;

                case DrawingToolType.ShapeArrow:
                    points = GenerateArrowPoints(start, end, attributes.Width);
                    break;

                case DrawingToolType.ShapeRect:
                    points = GenerateRectPoints(start, end);
                    break;

                case DrawingToolType.ShapeEllipse:
                    points = GenerateEllipsePoints(start, end);
                    break;

                default:
                    points.Add(new StylusPoint(start.X, start.Y));
                    points.Add(new StylusPoint(end.X, end.Y));
                    break;
            }

            DrawingAttributes shapeAttr = attributes.Clone();
            // 几何图形不根据压感缩放粗细，保持规整漂亮
            shapeAttr.IgnorePressure = true;
            shapeAttr.FitToCurve = false;

            return new Stroke(points, shapeAttr);
        }

        private static StylusPointCollection GenerateArrowPoints(Point start, Point end, double strokeWidth)
        {
            var points = new StylusPointCollection();

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < 3)
            {
                points.Add(new StylusPoint(start.X, start.Y));
                points.Add(new StylusPoint(end.X, end.Y));
                return points;
            }

            double angle = Math.Atan2(dy, dx);
            double arrowLength = Math.Max(16.0, Math.Min(36.0, strokeWidth * 3.5 + 8.0));
            double arrowAngle = 0.50; // 约28度，符合黄金箭头比例

            Point wing1 = new Point(
                end.X - arrowLength * Math.Cos(angle - arrowAngle),
                end.Y - arrowLength * Math.Sin(angle - arrowAngle));

            Point wing2 = new Point(
                end.X - arrowLength * Math.Cos(angle + arrowAngle),
                end.Y - arrowLength * Math.Sin(angle + arrowAngle));

            // 主干线: start -> end
            points.Add(new StylusPoint(start.X, start.Y));
            points.Add(new StylusPoint(end.X, end.Y));
            // 翼1: end -> wing1
            points.Add(new StylusPoint(wing1.X, wing1.Y));
            // 回到顶点: wing1 -> end
            points.Add(new StylusPoint(end.X, end.Y));
            // 翼2: end -> wing2
            points.Add(new StylusPoint(wing2.X, wing2.Y));

            return points;
        }

        private static StylusPointCollection GenerateRectPoints(Point start, Point end)
        {
            var points = new StylusPointCollection();

            double minX = Math.Min(start.X, end.X);
            double maxX = Math.Max(start.X, end.X);
            double minY = Math.Min(start.Y, end.Y);
            double maxY = Math.Max(start.Y, end.Y);

            points.Add(new StylusPoint(minX, minY));
            points.Add(new StylusPoint(maxX, minY));
            points.Add(new StylusPoint(maxX, maxY));
            points.Add(new StylusPoint(minX, maxY));
            points.Add(new StylusPoint(minX, minY)); // 闭合

            return points;
        }

        private static StylusPointCollection GenerateEllipsePoints(Point start, Point end)
        {
            var points = new StylusPointCollection();

            double cx = (start.X + end.X) / 2.0;
            double cy = (start.Y + end.Y) / 2.0;
            double rx = Math.Abs(end.X - start.X) / 2.0;
            double ry = Math.Abs(end.Y - start.Y) / 2.0;

            if (rx < 1) rx = 1;
            if (ry < 1) ry = 1;

            int segments = 48; // 48个采样点保证椭圆极为圆润光滑
            for (int i = 0; i <= segments; i++)
            {
                double theta = (2.0 * Math.PI * i) / segments;
                double x = cx + rx * Math.Cos(theta);
                double y = cy + ry * Math.Sin(theta);
                points.Add(new StylusPoint(x, y));
            }

            return points;
        }
    }
}
