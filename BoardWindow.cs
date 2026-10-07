using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LingGuangInk
{
    /// <summary>
    /// 自定义画板：一块可自由拖动、缩放、置顶的悬浮小黑板。
    ///
    /// 为什么始终 Topmost：主画板窗口是全屏且 Topmost 的覆盖层，
    /// 任何非置顶窗口都会被它整个挡住、根本看不见。
    /// 因此"固定到屏幕顶部"是在置顶之外，再把黑板吸附到屏幕顶端并锁定拖动。
    /// </summary>
    public class BoardWindow : Window
    {
        private const double MinBoardWidth = 320.0;
        private const double MinBoardHeight = 220.0;

        private readonly InkCanvas _board;
        private readonly Border _header;
        private readonly Button _btnPin;
        private readonly Button _btnChalk;
        private readonly Button _btnBoardEraser;

        // 默认置顶：主画板窗口是全屏 Topmost 覆盖层，不置顶就会被整个挡住。
        private bool _pinned = true;
        private bool _chalkMode = true;
        private bool _resizing;
        private Point _resizeStartMouse;
        private double _resizeStartW;
        private double _resizeStartH;

        public BoardWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 520;
            Height = 380;

            // ===== 外框 =====
            Border frame = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 22, 26, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 28,
                    ShadowDepth = 6,
                    Color = Colors.Black,
                    Opacity = 0.62
                }
            };

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ===== 标题栏（可拖动）=====
            _header = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 32, 38, 34)),
                CornerRadius = new CornerRadius(11, 11, 0, 0),
                Padding = new Thickness(12, 5, 8, 5),
                Cursor = Cursors.SizeAll,
                ToolTip = "按住拖动小黑板"
            };

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromRgb(24, 144, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "小黑板",
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(titleRow, 0);
            headerGrid.Children.Add(titleRow);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _btnChalk = CreateHeaderButton(VectorIcons.Chalk, "粉笔：在黑板书写", delegate { SetBoardMode(true); });
            buttons.Children.Add(_btnChalk);

            _btnBoardEraser = CreateHeaderButton(VectorIcons.BoardEraser, "黑板擦：抹除笔迹", delegate { SetBoardMode(false); });
            buttons.Children.Add(_btnBoardEraser);

            buttons.Children.Add(CreateHeaderButton(VectorIcons.Clear, "擦净整块黑板", ClearBoard));

            _btnPin = CreateHeaderButton(VectorIcons.Pin, "固定在最前面 / 取消固定", TogglePin);
            buttons.Children.Add(_btnPin);

            buttons.Children.Add(CreateHeaderButton(VectorIcons.Close, "关闭小黑板", delegate { Close(); }));

            Grid.SetColumn(buttons, 1);
            headerGrid.Children.Add(buttons);

            _header.Child = headerGrid;
            Grid.SetRow(_header, 0);
            root.Children.Add(_header);

            // ===== 黑板本体 =====
            Border boardFrame = new Border
            {
                Margin = new Thickness(10, 8, 10, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                ClipToBounds = true
            };

            RadialGradientBrush boardBg = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.38),
                Center = new Point(0.5, 0.38),
                RadiusX = 0.9,
                RadiusY = 0.9
            };
            boardBg.GradientStops.Add(new GradientStop(Color.FromRgb(48, 58, 52), 0.0));
            boardBg.GradientStops.Add(new GradientStop(Color.FromRgb(21, 26, 23), 1.0));
            boardFrame.Background = boardBg;

            _board = new InkCanvas
            {
                // 背景留空，让黑板的底色透出来
                Background = null,
                EditingMode = InkCanvasEditingMode.Ink,
                DefaultDrawingAttributes = new DrawingAttributes
                {
                    // 略带透明的白，接近粉笔质感
                    Color = Color.FromArgb(238, 255, 255, 255),
                    Width = 3.2,
                    Height = 3.2,
                    FitToCurve = true,
                    IgnorePressure = false,
                    StylusTip = StylusTip.Ellipse
                }
            };
            boardFrame.Child = _board;

            Grid.SetRow(boardFrame, 1);
            root.Children.Add(boardFrame);

            // ===== 右下角缩放手柄 =====
            Border grip = new Border
            {
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 3),
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeNWSE,
                ToolTip = "拖动调整画板大小"
            };

            System.Windows.Shapes.Path gripIcon = new System.Windows.Shapes.Path();
            gripIcon.Data = Geometry.Parse("M17,5 L17,17 L5,17 Z");
            gripIcon.Fill = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
            gripIcon.Stretch = Stretch.Uniform;
            gripIcon.IsHitTestVisible = false;
            grip.Child = gripIcon;

            Grid.SetRow(grip, 0);
            Grid.SetRowSpan(grip, 2);
            root.Children.Add(grip);

            frame.Child = root;
            Content = frame;

            // ===== 交互 =====
            _header.MouseLeftButtonDown += OnHeaderMouseDown;

            grip.MouseLeftButtonDown += OnGripDown;
            grip.MouseMove += OnGripMove;
            grip.MouseLeftButtonUp += OnGripUp;

            grip.PreviewTouchDown += OnGripTouchDown;
            grip.PreviewTouchMove += OnGripTouchMove;
            grip.PreviewTouchUp += OnGripTouchUp;

            // 初始为粉笔模式。此处模板尚未应用，图标配色在 Loaded 里再补一次。
            SetBoardMode(true);

            Loaded += delegate
            {
                Rect work = SystemParameters.WorkArea;
                Left = work.Left + 140;
                Top = work.Top + 140;

                UpdateModeVisuals();
            };
        }

        // ======================= 顶部固定 =======================

        /// <summary>
        /// 固定在最前面 = 置顶开关。
        ///
        /// 置顶时黑板浮在所有窗口之上、可直接书写；取消后会被主画板的全屏覆盖层挡住，
        /// 只能当背景参考。位置不受影响 —— 固定与否都可以自由拖动。
        /// </summary>
        private void TogglePin()
        {
            _pinned = !_pinned;
            Topmost = _pinned;
            UpdateModeVisuals();
        }

        /// <summary>
        /// 切换粉笔 / 黑板擦。
        /// 黑板擦用 EraseByPoint 配合大号矩形擦除形状，手感接近真黑板擦"抹掉一片"。
        /// </summary>
        private void SetBoardMode(bool chalk)
        {
            _chalkMode = chalk;

            if (chalk)
            {
                _board.EditingMode = InkCanvasEditingMode.Ink;
            }
            else
            {
                _board.EditingMode = InkCanvasEditingMode.EraseByPoint;
                _board.EraserShape = new RectangleStylusShape(56.0, 32.0);
            }

            UpdateModeVisuals();
        }

        private void ClearBoard()
        {
            _board.Strokes.Clear();
        }

        /// <summary>把当前粉笔 / 黑板擦 / 置顶状态反映到图标颜色上。</summary>
        private void UpdateModeVisuals()
        {
            SetIconAccent(_btnChalk, _chalkMode);
            SetIconAccent(_btnBoardEraser, !_chalkMode);
            SetIconAccent(_btnPin, _pinned);
        }

        private static void SetIconAccent(Button btn, bool accent)
        {
            if (btn == null || btn.Template == null) return;

            var icon = btn.Template.FindName("icon", btn) as System.Windows.Shapes.Path;
            if (icon == null) return;

            icon.Fill = accent
                ? new SolidColorBrush(Color.FromRgb(24, 144, 255))
                : new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        }

        // ======================= 拖动 =======================

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;

            try { DragMove(); }
            catch { /* 拖动被系统取消时忽略 */ }
        }

        // ======================= 缩放 =======================

        private void OnGripDown(object sender, MouseButtonEventArgs e)
        {
            BeginResize(e.GetPosition(this));
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void OnGripMove(object sender, MouseEventArgs e)
        {
            if (!_resizing) return;
            ApplyResize(e.GetPosition(this));
        }

        private void OnGripUp(object sender, MouseButtonEventArgs e)
        {
            EndResize((UIElement)sender);
            e.Handled = true;
        }

        private void OnGripTouchDown(object sender, TouchEventArgs e)
        {
            BeginResize(e.GetTouchPoint(this).Position);
            ((UIElement)sender).CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void OnGripTouchMove(object sender, TouchEventArgs e)
        {
            if (!_resizing) return;
            ApplyResize(e.GetTouchPoint(this).Position);
            e.Handled = true;
        }

        private void OnGripTouchUp(object sender, TouchEventArgs e)
        {
            EndResize((UIElement)sender);
            e.Handled = true;
        }

        private void BeginResize(Point pt)
        {
            _resizing = true;
            _resizeStartMouse = pt;
            _resizeStartW = ActualWidth;
            _resizeStartH = ActualHeight;
        }

        private void ApplyResize(Point pt)
        {
            double w = _resizeStartW + (pt.X - _resizeStartMouse.X);
            double h = _resizeStartH + (pt.Y - _resizeStartMouse.Y);

            Width = Math.Max(MinBoardWidth, w);
            Height = Math.Max(MinBoardHeight, h);
        }

        private void EndResize(UIElement grip)
        {
            _resizing = false;

            Mouse.Capture(null);
            grip.ReleaseMouseCapture();
            grip.ReleaseAllTouchCaptures();
        }

        // ======================= 小部件 =======================

        private static Button CreateHeaderButton(string pathData, string tip, Action onClick)
        {
            Button b = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                Focusable = false,
                ToolTip = tip,
                VerticalAlignment = VerticalAlignment.Center
            };

            ControlTemplate t = new ControlTemplate(typeof(Button));

            FrameworkElementFactory bg = new FrameworkElementFactory(typeof(Border));
            bg.Name = "bg";
            bg.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
            bg.SetValue(Border.BackgroundProperty, Brushes.Transparent);

            FrameworkElementFactory icon = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            icon.Name = "icon";
            icon.SetValue(System.Windows.Shapes.Path.DataProperty, VectorIcons.Parse(pathData));
            icon.SetValue(System.Windows.Shapes.Path.FillProperty, new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)));
            icon.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);
            icon.SetValue(FrameworkElement.WidthProperty, 14.0);
            icon.SetValue(FrameworkElement.HeightProperty, 14.0);
            icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            bg.AppendChild(icon);
            t.VisualTree = bg;

            Trigger hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), "bg"));
            t.Triggers.Add(hover);

            b.Template = t;
            b.Click += delegate { if (onClick != null) onClick(); };
            return b;
        }
    }
}
