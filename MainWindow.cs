using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Point = System.Windows.Point;

namespace LingGuangInk
{
    public class MainWindow : Window
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_TOUCH = 0x0240;
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;

        // 注：曾有 PassthroughUnknownInput 开关尝试把"未知输入源"也放行，
        // v1.6.3 已移除。真机实测表明 HTTRANSPARENT 对跨进程窗口无效，
        // 放行未知输入只会连带破坏触控笔的书写链路。

        private readonly AppSettings _settings;
        private Grid _rootGrid;
        private Border _backdropBorder;
        private InkCanvas _inkCanvas;
        private ShapePreviewRenderer _shapePreview;
        private LaserTrailRenderer _laserRenderer;

        // 统一在同一窗口顶层的悬浮工具栏系统
        private Canvas _toolbarCanvas;
        private Border _mainPillBorder;
        private Border _miniBallBorder;
        private StackPanel _toolsPanel;
        private Border _toastBorder;
        private TextBlock _toastText;
        private DispatcherTimer _toastTimer;

        private Button _btnPen;
        private Button _btnHighlighter;
        private Button _btnLaser;
        private Button _btnShapes;
        private Button _btnEraser;
        private Button _btnColor;
        private Ellipse _colorBadge;
        private Button _btnThickness;
        private Button _btnPalmRejection;
        private Ellipse _palmBadge;
        private Button _btnPassthrough;
        private Button _btnBackdrop;
        private Button _btnMore;
        private Button _btnCollapse;
        private Button _btnBoard;
        // 自定义画板窗口（单例，关闭后置空以便重新创建）
        private BoardWindow _boardWindow;
        // 收起按钮当前是否位于最右端（工具栏向左展开时）。收起悬浮球时据此决定落点。
        private bool _collapseAtRight;
        // 工具栏按钮的正向逻辑顺序，向左展开时按此逆序重建
        private System.Collections.Generic.List<UIElement> _toolbarLogicalOrder;
        // 「更多」面板内部的正向逻辑顺序，同样需要随方向镜像
        private System.Collections.Generic.List<UIElement> _advancedLogicalOrder;
        private StackPanel _advancedPanel;
        private Button _btnUndo;
        private Button _btnRedo;

        private Popup _colorPopup;
        private Popup _shapesPopup;
        private Popup _thicknessPopup;
        private TextBlock _thicknessValueText;
        // 粗细面板里的实时尺寸预览圆点
        private Ellipse _thicknessPreviewDot;
        private Popup _eraserPopup;
        // 当前橡皮模式：笔迹擦除（整条）或像素擦除（擦接触区域）
        private DrawingToolType _eraserMode = DrawingToolType.EraserStroke;

        // ===== 自动手笔分离（Raw Input 悬停触发）=====
        // 思路：常态让窗口 WS_EX_TRANSPARENT（手指完全穿透）；靠 RIDEV_INPUTSINK 的
        // 原始输入绕过命中测试，感知到笔在感应范围内就摘掉穿透让笔能写，笔离开再挂回。
        private bool _autoSeparation;
        private bool _penInRange;
        private bool _autoTransparent;
        private bool _penRawInputRegistered;
        private DispatcherTimer _penPresenceTimer;
        private Button _btnAutoSeparation;
        private Popup _backdropPopup;

        private bool _isCollapsed = false;
        private bool _isPassthrough = false;
        private IntPtr _windowHandle = IntPtr.Zero;
        private uint _toggleMessage;
        private EscapeHatchWindow _escapeHatch;

        private DrawingToolType _currentTool = DrawingToolType.Pen;
        private BackdropType _currentBackdrop = BackdropType.Transparent;

        private Point? _shapeStartPoint = null;
        private bool _isDraggingShape = false;

        private PalmRejectionManager _palmManager;
        private SurfacePenHandler _penHandler;
        private HistoryManager _historyManager;

        private readonly Color[] _presetColors = new Color[]
        {
            Color.FromRgb(255, 77, 79),   // 珊瑚红 (默认)
            Color.FromRgb(250, 140, 22),  // 活力橙
            Color.FromRgb(250, 219, 20),  // 亮柠檬黄
            Color.FromRgb(82, 196, 26),   // 翡翠绿
            Color.FromRgb(19, 194, 194),  // 霓虹青
            Color.FromRgb(24, 144, 255),  // 极客蓝
            Color.FromRgb(114, 46, 209),  // 优雅紫
            Color.FromRgb(235, 47, 150),  // 樱花粉
            Color.FromRgb(255, 255, 255), // 纯洁白
            Color.FromRgb(38, 38, 38)     // 曜石黑
        };

        // ===== 双指轻触撤销手势 =====
        // 判定条件：同时只有两指接触画板、两指都没有明显移动、且整段接触时间很短。
        // 加时长限制是为了避免"手掌压在屏幕上再抬起"被误判成轻敲。
        private const double TwoFingerMoveTolerance = 30.0;
        private const double TwoFingerMaxDurationMs = 450.0;

        private TwoFingerTapDetector _twoFingerTap;

        public MainWindow(AppSettings settings)
        {
            _settings = settings;

            Title = "灵光画笔";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            // 使用 Alpha=1 微透像素：人眼完全不可见，但 DWM 与 Surface 数字化仪将其作为实体画板，确保 Surface Pen 4096级压感正常触发
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;

            UpdateScreenBounds();

            BuildUI();

            Loaded += OnWindowLoaded;
            Closed += (s, e) => Application.Current.Shutdown();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) =>
            {
                Dispatcher.Invoke((Action)UpdateScreenBounds);
            };
        }

        private void UpdateScreenBounds()
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            UpdateScreenBounds();

            PalmRejectionManager.RefreshTabletDeviceCache();

            // 挂接 WM_NCHITTEST 消息钩子：实现手笔分离硬件级分流及桌面穿透
            var helper = new WindowInteropHelper(this);
            _windowHandle = helper.Handle;

            // 关键：必须让触摸保持"被提升为鼠标消息"的状态。
            // WM_NCHITTEST 阶段只有 GetMessageExtraInfo() 的签名能可靠区分触摸与
            // 触控笔；一旦窗口注册了触摸（改走 WM_TOUCH），签名消失，分流判断会
            // 全部失效并回退成 HTCLIENT —— 手指就被窗口整个吃掉了。
            NativeMethods.UnregisterTouchWindow(_windowHandle);

            NativeMethods.InputDiagnostics.LogPath =
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nchittest.log");

            // 监听"再次启动"广播：把 Surface Pen 顶部按钮绑定到本程序后，
            // 按笔即可在"涂鸦模式 / 穿透模式"之间切换。
            _toggleMessage = NativeMethods.RegisterWindowMessage(NativeMethods.ToggleMessageName);

            var source = HwndSource.FromHwnd(_windowHandle);
            if (source != null)
            {
                source.AddHook(HwndMessageHook);
            }

            // 初始化工具栏位置居中靠顶
            double initLeft = Math.Max(20, (ActualWidth - 780) / 2);
            Canvas.SetLeft(_mainPillBorder, initLeft);
            Canvas.SetTop(_mainPillBorder, 26);

            UpdateActiveToolButton(_currentTool);

            // 退出时立即落盘一次，保证防抖窗口内未保存的改动不丢。
            // 先 -= 再 +=，这样即便 OnWindowLoaded 被重复调用也不会重复订阅。
            Closing -= OnWindowClosing;
            Closing += OnWindowClosing;
            UpdatePassthroughButton(_isPassthrough);
            UpdateHistoryButtons();
            UpdatePalmRejectionButton();

            NativeMethods.InputDiagnostics.Log("=== 灵光画笔启动完成，命中测试分流就绪 ===");
            ShowToast("✨ 灵光画笔已就绪：Surface Pen 随心涂鸦，手笔分离防误触已开启");
        }

        private IntPtr HwndMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_toggleMessage != 0 && msg == (int)_toggleMessage)
            {
                TogglePassthrough();
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WM_INPUT)
            {
                // 只注册了 Digitizer/Pen 一种用途，所以这里的任何 WM_INPUT 都来自笔设备。
                // RIDEV_INPUTSINK 让这条链路绕过命中测试，即使窗口处于
                // WS_EX_TRANSPARENT 全穿透状态也照样收得到 —— 这正是自动手笔分离的关键。
                if (_autoSeparation) OnPenRawInput();

                // 不置 handled，让原始输入继续正常流转
                return IntPtr.Zero;
            }

            if (msg == WM_TOUCH)
            {
                // 收到 WM_TOUCH 说明触摸窗口被重新注册，触摸不再被提升为鼠标消息，
                // GetMessageExtraInfo 的签名判定随之失效 —— 这是最需要监控的失效路径。
                NativeMethods.InputDiagnostics.Log("!! 收到 WM_TOUCH：触摸窗口已被注册，签名判定失效");
                return IntPtr.Zero;
            }

            if (msg == WM_NCHITTEST)
            {
                try
                {
                    int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                    int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                    Point screenPt = new Point(x, y);
                    Point winPt = PointFromScreen(screenPt);

                    // 1. 如果光标/触点位于悬浮工具栏、悬浮球或弹出菜单上：
                    // 无论手触摸、触控笔还是鼠标，100% 正常响应点击交互
                    if (IsOverToolbarOrPopups(winPt))
                    {
                        handled = true;
                        return new IntPtr(HTCLIENT);
                    }

                    // 2. 如果开启了【穿透桌面模式】：全屏除工具栏外 100% 穿透到底层
                    if (_isPassthrough)
                    {
                        handled = true;
                        return new IntPtr(HTTRANSPARENT);
                    }

                    // 3. 全部输入保留在画板窗口。
                    //
                    // 历史结论（v1.6.3）：HTTRANSPARENT 这条路已被彻底证伪。
                    //   - 微软文档限定它只在 "the same thread" 内生效，跨进程无效；
                    //   - Surface Pro 6 真机实测：返回 HTTRANSPARENT 后手指依旧到不了
                    //     下层，反而把触控笔的输入一起带偏，涂鸦模式下笔无法书写。
                    // 真正可靠的穿透机制只有窗口级的 WS_EX_TRANSPARENT，
                    // 由"穿透模式"经 SetClickThrough 控制。
                    // 诊断日志开销不小（两次 P/Invoke + 可能的同步写盘），
                    // 这里是逐点触发的热路径，必须先用 Enabled 短路再取值。
                    if (NativeMethods.InputDiagnostics.Enabled)
                    {
                        NativeMethods.InputDiagnostics.Log(
                            "NCHITTEST " + NativeMethods.GetCurrentInputKind() + " -> HTCLIENT");
                    }

                    handled = true;
                    return new IntPtr(HTCLIENT);
                }
                catch
                {
                    handled = true;
                    return new IntPtr(HTCLIENT);
                }
            }
            return IntPtr.Zero;
        }

        private bool IsOverToolbarOrPopups(Point winPt)
        {
            if (_isCollapsed)
            {
                double left = Canvas.GetLeft(_miniBallBorder);
                double top = Canvas.GetTop(_miniBallBorder);
                double w = _miniBallBorder.ActualWidth > 0 ? _miniBallBorder.ActualWidth : 44;
                double h = _miniBallBorder.ActualHeight > 0 ? _miniBallBorder.ActualHeight : 44;
                Rect ballRect = new Rect(left, top, w, h);
                ballRect.Inflate(8, 8);
                return ballRect.Contains(winPt);
            }
            else
            {
                double left = Canvas.GetLeft(_mainPillBorder);
                double top = Canvas.GetTop(_mainPillBorder);
                double w = _mainPillBorder.ActualWidth > 0 ? _mainPillBorder.ActualWidth : 780;
                double h = _mainPillBorder.ActualHeight > 0 ? _mainPillBorder.ActualHeight : 50;
                Rect pillRect = new Rect(left, top, w, h);
                pillRect.Inflate(8, 8);
                if (pillRect.Contains(winPt)) return true;

                if (_colorPopup != null && _colorPopup.IsOpen) return true;
                if (_shapesPopup != null && _shapesPopup.IsOpen) return true;
                if (_thicknessPopup != null && _thicknessPopup.IsOpen) return true;
                if (_backdropPopup != null && _backdropPopup.IsOpen) return true;

                return false;
            }
        }

        private void BuildUI()
        {
            _rootGrid = new Grid();

            // 1. 底衬层 (白板 / 黑板 / 透明)
            _backdropBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                IsHitTestVisible = false
            };
            _rootGrid.Children.Add(_backdropBorder);

            // 2. 核心 Windows Ink 画板 (内置硬件级手笔分离)
            _inkCanvas = new CustomInkCanvas(_settings)
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
            };
            _rootGrid.Children.Add(_inkCanvas);

            // 3. 几何图形实时预览层
            _shapePreview = new ShapePreviewRenderer();
            _rootGrid.Children.Add(_shapePreview);

            // 4. 激光笔平滑衰减光轨层
            _laserRenderer = new LaserTrailRenderer();
            _rootGrid.Children.Add(_laserRenderer);

            // 应用笔刷初始属性
            ApplyDrawingAttributes();

            // 初始化历史管理器 (撤销/重做)
            _historyManager = new HistoryManager(_inkCanvas);
            _historyManager.HistoryChanged += UpdateHistoryButtons;

            // 初始化手笔分离防误触
            _palmManager = new PalmRejectionManager(_settings);
            _palmManager.Attach(_inkCanvas);

            // 初始化 Surface Pen 1776 专用按键与笔尾检测
            _penHandler = new SurfacePenHandler(_inkCanvas, _settings);
            _penHandler.OnEraserStateChanged += isEraser =>
            {
                if (isEraser) ShowToast("Surface Pen 尾端橡皮擦已激活");
                else SetTool(_currentTool);
            };
            _penHandler.OnPenStatusTip += tip => ShowToast(tip);

            // 交互事件
            _inkCanvas.MouseDown += OnCanvasMouseDown;
            _inkCanvas.MouseMove += OnCanvasMouseMove;
            _inkCanvas.MouseUp += OnCanvasMouseUp;

            _inkCanvas.StylusDown += OnCanvasStylusDown;
            _inkCanvas.StylusMove += OnCanvasStylusMove;
            _inkCanvas.StylusUp += OnCanvasStylusUp;

            // 双指轻触撤销：判定逻辑见 TwoFingerTapDetector
            _twoFingerTap = new TwoFingerTapDetector(TwoFingerMoveTolerance, TwoFingerMaxDurationMs);
            _twoFingerTap.Tapped += delegate
            {
                if (_historyManager != null) _historyManager.Undo();
                ShowToast("双指轻触：已撤销上一步");
            };

            // 必须用 handledEventsToo 订阅 ——
            // PalmRejectionManager 在手笔分离开启时会把触摸事件标记为 Handled，
            // 普通 += 订阅根本收不到这些事件。
            _inkCanvas.AddHandler(UIElement.PreviewTouchDownEvent,
                new EventHandler<TouchEventArgs>(OnCanvasTouchDown), true);
            _inkCanvas.AddHandler(UIElement.PreviewTouchMoveEvent,
                new EventHandler<TouchEventArgs>(OnCanvasTouchMove), true);
            _inkCanvas.AddHandler(UIElement.PreviewTouchUpEvent,
                new EventHandler<TouchEventArgs>(OnCanvasTouchUp), true);
            _inkCanvas.AddHandler(UIElement.TouchLeaveEvent,
                new EventHandler<TouchEventArgs>(OnCanvasTouchLeave), true);

            // 5. 顶层悬浮工具栏容器 (同窗口顶层，绝对不会被背景遮挡)
            _toolbarCanvas = new Canvas { IsHitTestVisible = true };
            BuildToolbarUI();
            _rootGrid.Children.Add(_toolbarCanvas);

            Content = _rootGrid;

            InitPopups();
        }

        private void BuildToolbarUI()
        {
            // 主胶囊容器
            _mainPillBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(242, 24, 26, 34)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(25),
                Padding = new Thickness(8, 5, 8, 5),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Direction = 270,
                    Color = Colors.Black,
                    Opacity = 0.55
                }
            };

            _toolsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // ===== 收起按钮（默认最左，向右的尖括号；向左展开时移到最右并换成左括号）=====
            _btnCollapse = CreateIconButton(VectorIcons.CollapseRight,
                "收起为悬浮微球\n收拢至屏幕边缘，点击即刻弹回", ToggleCollapse);
            _toolsPanel.Children.Add(_btnCollapse);

            // 拖动把手
            Border gripHandle = CreateGripHandle();
            _toolsPanel.Children.Add(gripHandle);

            _toolsPanel.Children.Add(CreateDivider());

            // ===== 常用绘图工具 =====
            _btnPen = CreateIconButton(VectorIcons.Pen, "压感钢笔\n支持 Surface Pen 4096级高精度压感", () => SetTool(DrawingToolType.Pen));
            _btnHighlighter = CreateIconButton(VectorIcons.Highlighter, "半透明荧光笔\n高亮标注文字，不遮挡下层界面", () => SetTool(DrawingToolType.Highlighter));
            _btnEraser = CreateMenuButton(VectorIcons.Eraser,
                "橡皮擦\n点击选择擦除模式（笔迹擦除 / 像素擦除）",
                ToggleEraserPopup);

            _toolsPanel.Children.Add(_btnPen);
            _toolsPanel.Children.Add(_btnHighlighter);
            _toolsPanel.Children.Add(_btnEraser);

            _toolsPanel.Children.Add(CreateDivider());

            // ===== 颜色 =====
            _btnColor = CreateColorButton();
            _toolsPanel.Children.Add(_btnColor);

            _toolsPanel.Children.Add(CreateDivider());

            // ===== 撤销 / 清屏 =====
            _btnUndo = CreateIconButton(VectorIcons.Undo, "撤销上一步笔迹", () => _historyManager.Undo());
            Button btnClear = CreateIconButton(VectorIcons.Clear, "一键清屏", () => _historyManager.Clear());

            _toolsPanel.Children.Add(_btnUndo);
            _toolsPanel.Children.Add(btnClear);

            _toolsPanel.Children.Add(CreateDivider());

            // ===== 穿透桌面（高频，留在主栏）=====
            _btnPassthrough = CreateIconButton(VectorIcons.Passthrough,
                "穿透桌面模式\n开启后手指/鼠标直接操作下层应用；再点一次或点右下角圆钮退出",
                TogglePassthrough);
            _toolsPanel.Children.Add(_btnPassthrough);

            // ===== 自动手笔分离（Raw Input 悬停触发）=====
            _btnAutoSeparation = CreateIconButton(VectorIcons.AutoSeparation,
                "自动手笔分离\n开启后：笔靠近自动可书写，笔离开自动穿透给手指\n（点右下角圆钮可关闭）",
                () => SetAutoSeparation(!_autoSeparation));
            _toolsPanel.Children.Add(_btnAutoSeparation);

            // ===== 更多：附加工具折叠区 =====
            _btnMore = CreateIconButton(VectorIcons.More, "更多工具\n展开 / 收起附加功能", ToggleMorePanel);
            _toolsPanel.Children.Add(_btnMore);

            _advancedPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            _advancedPanel.Children.Add(CreateDivider());

            _btnLaser = CreateIconButton(VectorIcons.Laser, "激光教鞭\n光轨在1.2秒内平滑自动淡出", () => SetTool(DrawingToolType.Laser));
            _btnShapes = CreateMenuButton(VectorIcons.Shapes,
                "几何图形\n点击选择图形类型", ToggleShapesPopup);
            _btnThickness = CreateMenuButton(VectorIcons.Thickness,
                "笔触粗细\n点击拖动滑块调节", ToggleThicknessPopup);
            _btnPalmRejection = CreatePalmRejectionButton();
            _btnBackdrop = CreateMenuButton(VectorIcons.Blackboard,
                "背景底衬\n点击选择底衬", ToggleBackdropPopup);
            _btnBoard = CreateIconButton(VectorIcons.Board,
                "自定义画板\n打开一块可自由拖动、缩放、置顶的悬浮小黑板",
                ToggleBoardWindow);

            _advancedPanel.Children.Add(_btnLaser);
            _advancedPanel.Children.Add(_btnShapes);
            _advancedPanel.Children.Add(_btnThickness);
            _advancedPanel.Children.Add(_btnPalmRejection);
            _advancedPanel.Children.Add(_btnBackdrop);
            _advancedPanel.Children.Add(_btnBoard);

            _advancedPanel.Children.Add(CreateDivider());

            _btnRedo = CreateIconButton(VectorIcons.Redo, "重做", () => _historyManager.Redo());
            Button btnSnapshot = CreateIconButton(VectorIcons.Snapshot, "截图标注并复制\n截图合并笔迹直接存入剪贴板", () => CaptureScreenAndAnnotationsToClipboard());
            Button btnSettings = CreateIconButton(VectorIcons.Settings, "设置与触控指南", OpenSettingsDialog);
            Button btnExit = CreateIconButton(VectorIcons.Close, "退出灵光画笔", () => Application.Current.Shutdown());

            _advancedPanel.Children.Add(_btnRedo);
            _advancedPanel.Children.Add(btnSnapshot);
            _advancedPanel.Children.Add(btnSettings);
            _advancedPanel.Children.Add(btnExit);

            _toolsPanel.Children.Add(_advancedPanel);

            _mainPillBorder.Child = _toolsPanel;
            _toolbarCanvas.Children.Add(_mainPillBorder);

            // 迷你折叠球
            _miniBallBorder = CreateMiniBall();
            _toolbarCanvas.Children.Add(_miniBallBorder);

            // 浮动通知 Toast
            _toastBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 20, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 6, 14, 6),
                Opacity = 0,
                IsHitTestVisible = false
            };
            _toastText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium
            };
            _toastBorder.Child = _toastText;
            _toolbarCanvas.Children.Add(_toastBorder);
        }

        private Border CreateGripHandle()
        {
            Border grip = new Border
            {
                Width = 24,
                Height = 36,
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Cursor = Cursors.SizeAll,
                ToolTip = "按住拖动悬浮窗到屏幕任意位置",
                Margin = new Thickness(2, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            System.Windows.Shapes.Path icon = new System.Windows.Shapes.Path
            {
                Data = VectorIcons.Parse(VectorIcons.Grip),
                Fill = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                Stretch = Stretch.Uniform,
                Width = 11,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grip.Child = icon;

            Point startMouse = new Point(0, 0);
            Point startPos = new Point(0, 0);
            bool isDragging = false;

            Action<Point> handleStart = pt =>
            {
                startMouse = pt;
                startPos = new Point(Canvas.GetLeft(_mainPillBorder), Canvas.GetTop(_mainPillBorder));
                isDragging = true;
                CloseAllPopups();
            };

            Action<Point> handleMove = cur =>
            {
                if (isDragging)
                {
                    Vector delta = cur - startMouse;
                    double newLeft = Math.Max(10, Math.Min(ActualWidth - _mainPillBorder.ActualWidth - 10, startPos.X + delta.X));
                    double newTop = Math.Max(10, Math.Min(ActualHeight - _mainPillBorder.ActualHeight - 10, startPos.Y + delta.Y));
                    Canvas.SetLeft(_mainPillBorder, newLeft);
                    Canvas.SetTop(_mainPillBorder, newTop);

                    Canvas.SetLeft(_toastBorder, newLeft + (_mainPillBorder.ActualWidth - _toastBorder.ActualWidth) / 2);
                    Canvas.SetTop(_toastBorder, newTop + _mainPillBorder.ActualHeight + 8);
                }
            };

            Action handleEnd = () =>
            {
                isDragging = false;
            };

            // 拖动来源互斥标志。
            // Surface 上一次触摸会同时触发 PreviewTouchMove 与 MouseMove（触摸提升），
            // 若两者都调用 handleMove，每次移动会被处理两遍、且坐标来源不同，
            // 位置反复跳变，表现就是"拖动工具栏时位置闪烁"。
            // 用标志确保同一时刻只有一个输入来源在驱动拖动。
            bool byTouch = false;
            bool byStylus = false;

            grip.MouseLeftButtonDown += (s, e) =>
            {
                if (byTouch || byStylus) return;
                handleStart(e.GetPosition(_toolbarCanvas));
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += (s, e) =>
            {
                if (byTouch || byStylus) return;
                if (grip.IsMouseCaptured) handleMove(e.GetPosition(_toolbarCanvas));
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (byTouch || byStylus) return;
                handleEnd();
                grip.ReleaseMouseCapture();
                e.Handled = true;
            };

            grip.PreviewTouchDown += (s, e) =>
            {
                byTouch = true;
                handleStart(e.GetTouchPoint(_toolbarCanvas).Position);
                grip.CaptureTouch(e.TouchDevice);
                e.Handled = true;
            };
            grip.PreviewTouchMove += (s, e) =>
            {
                if (byTouch && grip.AreAnyTouchesCaptured)
                    handleMove(e.GetTouchPoint(_toolbarCanvas).Position);
                e.Handled = true;
            };
            grip.PreviewTouchUp += (s, e) =>
            {
                handleEnd();
                grip.ReleaseTouchCapture(e.TouchDevice);
                byTouch = false;
                e.Handled = true;
            };

            grip.PreviewStylusDown += (s, e) =>
            {
                byStylus = true;
                handleStart(e.GetPosition(_toolbarCanvas));
                grip.CaptureStylus();
                e.Handled = true;
            };
            grip.PreviewStylusMove += (s, e) =>
            {
                if (byStylus && grip.IsStylusCaptured)
                    handleMove(e.GetPosition(_toolbarCanvas));
                e.Handled = true;
            };
            grip.PreviewStylusUp += (s, e) =>
            {
                handleEnd();
                grip.ReleaseStylusCapture();
                byStylus = false;
                e.Handled = true;
            };

            return grip;
        }

        private Border CreateMiniBall()
        {
            Border ball = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Color.FromArgb(240, 24, 26, 34)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                ToolTip = "点击展开【灵光画笔】工具栏\n按住可拖动微球到任意位置",
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 3,
                    Color = Colors.Black,
                    Opacity = 0.6
                }
            };

            Grid grid = new Grid();
            System.Windows.Shapes.Path icon = new System.Windows.Shapes.Path
            {
                Data = VectorIcons.Parse(VectorIcons.Pen),
                Fill = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                Stretch = Stretch.Uniform,
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(icon);
            ball.Child = grid;

            Point startMouse = new Point(0, 0);
            Point startPos = new Point(0, 0);
            bool isMoved = false;

            Action<Point> ballStart = pt =>
            {
                startMouse = pt;
                startPos = new Point(Canvas.GetLeft(_miniBallBorder), Canvas.GetTop(_miniBallBorder));
                isMoved = false;
            };

            Action<Point> ballMove = cur =>
            {
                if ((cur - startMouse).Length > 6)
                {
                    isMoved = true;
                    Vector delta = cur - startMouse;
                    double newLeft = Math.Max(10, Math.Min(ActualWidth - 54, startPos.X + delta.X));
                    double newTop = Math.Max(10, Math.Min(ActualHeight - 54, startPos.Y + delta.Y));
                    Canvas.SetLeft(_miniBallBorder, newLeft);
                    Canvas.SetTop(_miniBallBorder, newTop);
                }
            };

            Action ballEnd = () =>
            {
                if (!isMoved)
                {
                    ToggleCollapse();
                }
            };

            ball.MouseLeftButtonDown += (s, e) => { ballStart(e.GetPosition(_toolbarCanvas)); ball.CaptureMouse(); e.Handled = true; };
            ball.MouseMove += (s, e) => { if (ball.IsMouseCaptured) ballMove(e.GetPosition(_toolbarCanvas)); };
            ball.MouseLeftButtonUp += (s, e) => { if (ball.IsMouseCaptured) { ball.ReleaseMouseCapture(); ballEnd(); e.Handled = true; } };

            ball.PreviewTouchDown += (s, e) => { ballStart(e.GetTouchPoint(_toolbarCanvas).Position); ball.CaptureTouch(e.TouchDevice); e.Handled = true; };
            ball.PreviewTouchMove += (s, e) => { if (ball.AreAnyTouchesCaptured) ballMove(e.GetTouchPoint(_toolbarCanvas).Position); e.Handled = true; };
            ball.PreviewTouchUp += (s, e) => { if (ball.AreAnyTouchesCaptured) { ball.ReleaseTouchCapture(e.TouchDevice); ballEnd(); e.Handled = true; } };

            ball.PreviewStylusDown += (s, e) => { ballStart(e.GetPosition(_toolbarCanvas)); ball.CaptureStylus(); e.Handled = true; };
            ball.PreviewStylusMove += (s, e) => { if (ball.IsStylusCaptured) ballMove(e.GetPosition(_toolbarCanvas)); e.Handled = true; };
            ball.PreviewStylusUp += (s, e) => { if (ball.IsStylusCaptured) { ball.ReleaseStylusCapture(); ballEnd(); e.Handled = true; } };

            return ball;
        }

        /// <summary>
        /// 普通动作按钮的接线：只在按下时触发一次，手感即时。
        ///
        /// 只保留 PreviewMouseLeftButtonDown 一个入口。窗口没有注册触摸窗口，
        /// 触摸与笔都会被系统提升为鼠标消息，因此这一个入口已覆盖鼠标 / 触摸 / 笔三种输入；
        /// 再挂 PreviewTouchDown / PreviewStylusDown 只会让同一手势触发两遍。
        ///
        /// 也不再挂 Click：与 Down 重复触发，还得靠时间戳去重，而那个去重是全局共享的，
        /// 会误伤弹出菜单里的菜单项 —— 点开按钮后 220ms 内点菜单项会被静默吞掉。
        /// </summary>
        private void WireButtonAction(Button btn, Action action)
        {
            btn.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                CloseAllPopups();
                if (action != null) action();
            };
        }

        // ======================= 颜色换算（色轮用） =======================

        private static Color HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360.0) + 360.0) % 360.0;
            double c = v * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = v - c;

            double r = 0.0, g = 0.0, b = 0.0;
            if (h < 60.0) { r = c; g = x; }
            else if (h < 120.0) { r = x; g = c; }
            else if (h < 180.0) { g = c; b = x; }
            else if (h < 240.0) { g = x; b = c; }
            else if (h < 300.0) { r = x; b = c; }
            else { r = c; b = x; }

            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255.0),
                (byte)Math.Round((g + m) * 255.0),
                (byte)Math.Round((b + m) * 255.0));
        }

        /// <summary>返回 { 色相(度), 饱和度, 明度 }。</summary>
        private static double[] RgbToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;

            double h = 0.0;
            if (d > 0.0)
            {
                if (max == r) h = 60.0 * (((g - b) / d) % 6.0);
                else if (max == g) h = 60.0 * ((b - r) / d + 2.0);
                else h = 60.0 * ((r - g) / d + 4.0);
            }
            if (h < 0.0) h += 360.0;

            double s = max <= 0.0 ? 0.0 : d / max;
            return new double[] { h, s, max };
        }

        private static string ToHex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        /// <summary>接受 #RGB 与 #RRGGBB（# 可省略）。</summary>
        private static bool TryParseHex(string text, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrEmpty(text)) return false;

            string s = text.Trim().TrimStart('#');
            if (s.Length == 3)
                s = new string(new char[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            if (s.Length != 6) return false;

            int v;
            if (!int.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                return false;

            color = Color.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
            return true;
        }

        /// <summary>替换按钮模板内 iconPath 的图标数据（图标 Data 是烘进 ControlTemplate 的）。</summary>
        private static void SetButtonIcon(Button btn, string pathData)
        {
            if (btn == null || btn.Template == null) return;

            var icon = btn.Template.FindName("iconPath", btn) as System.Windows.Shapes.Path;
            if (icon != null) icon.Data = VectorIcons.Parse(pathData);
        }

        // ===== 设置持久化 =====
        // 拖动粗细滑块、取色等操作会高频改设置，这里做 1.5 秒防抖后再落盘，
        // 避免每动一下都写文件；窗口关闭时另外做一次立即保存。

        private DispatcherTimer _settingsSaveTimer;

        /// <summary>安排一次防抖保存；连续调用只会写一次盘。</summary>
        private void ScheduleSettingsSave()
        {
            if (_settingsSaveTimer == null)
            {
                _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                _settingsSaveTimer.Tick += delegate
                {
                    _settingsSaveTimer.Stop();
                    _settings.Save();
                };
            }

            _settingsSaveTimer.Stop();
            _settingsSaveTimer.Start();
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 立即落盘一次，兜住还在防抖窗口里的改动
            _settings.Save();
        }

        /// <summary>
        /// 刷新粗细面板的实时尺寸预览：圆点按真实笔宽渲染（封顶 50px），
        /// 颜色跟随当前画笔色，并同步数值文本。
        /// </summary>
        private void RefreshThicknessPreview()
        {
            if (_thicknessPreviewDot == null) return;

            double d = Math.Max(2.0, Math.Min(50.0, _settings.PenWidth));
            _thicknessPreviewDot.Width = d;
            _thicknessPreviewDot.Height = d;
            _thicknessPreviewDot.Fill = new SolidColorBrush(_settings.CurrentColor);

            if (_thicknessValueText != null)
                _thicknessValueText.Text = string.Format("笔迹粗细   {0:F0} px", _settings.PenWidth);
        }

        /// <summary>
        /// 深色弹窗专用的滑块外观：半透明浅色轨道 + 蓝色已填充段 + 白色圆形滑块。
        /// WPF 默认 Slider 主题在深色底上会渲染成一整条暗色实心块，看不出刻度与当前位置。
        /// 注：Track 的 DecreaseRepeatButton/Thumb/IncreaseRepeatButton 依赖属性为 internal，
        /// 无法用 FrameworkElementFactory 赋值，因此这里直接用 XAML 解析整份样式。
        /// </summary>
        private static Style CreateDarkSliderStyle()
        {
            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='Slider'>
  <Setter Property='Height' Value='28'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='Slider'>
        <Grid VerticalAlignment='Center'>
          <Border Height='6' CornerRadius='3' Background='#38FFFFFF'/>
          <Track x:Name='PART_Track'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='Slider.DecreaseLarge'>
                <RepeatButton.Template>
                  <ControlTemplate TargetType='RepeatButton'>
                    <Border Height='6' CornerRadius='3' Background='#1890FF'/>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb Width='18' Height='18'>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Ellipse Fill='White'/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='Slider.IncreaseLarge'>
                <RepeatButton.Template>
                  <ControlTemplate TargetType='RepeatButton'>
                    <Border Background='Transparent'/>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private Button CreateIconButton(string pathData, string toolTip, Action clickAction)
        {
            Button btn = BuildIconButtonVisual(pathData, toolTip);
            WireButtonAction(btn, clickAction);
            return btn;
        }

        /// <summary>
        /// 创建"点击弹出小菜单"的按钮。
        ///
        /// 与 CreateIconButton 的唯一区别在触发时机：这里接 Button.Click（mouse-up 时触发，
        /// 且鼠标 / 触摸 / 笔每个手势都只触发一次），而不是 PreviewMouseLeftButtonDown。
        ///
        /// 原因：Popup 的 StaysOpen=false 会把"按下之后紧随的松手"判定为外部点击，
        /// 菜单刚打开就被关掉，表现出来就是"按住才显示、一松手就没了、菜单项根本选不到"。
        /// 改到 mouse-up 触发即可彻底避开这个时序冲突。
        /// </summary>
        private Button CreateMenuButton(string pathData, string toolTip, Action togglePopup)
        {
            Button btn = BuildIconButtonVisual(pathData, toolTip);

            btn.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                if (togglePopup != null) togglePopup();
            };

            return btn;
        }

        private Button BuildIconButtonVisual(string pathData, string toolTip)
        {
            Button btn = new Button
            {
                Width = 36,
                Height = 36,
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = toolTip,
                Focusable = false
            };

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "border";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

            FrameworkElementFactory path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            path.Name = "iconPath";
            path.SetValue(System.Windows.Shapes.Path.DataProperty, VectorIcons.Parse(pathData));
            path.SetValue(System.Windows.Shapes.Path.FillProperty, new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)));
            path.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);
            path.SetValue(System.Windows.Shapes.Path.WidthProperty, 17.0);
            path.SetValue(System.Windows.Shapes.Path.HeightProperty, 17.0);
            path.SetValue(System.Windows.Shapes.Path.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            path.SetValue(System.Windows.Shapes.Path.VerticalAlignmentProperty, VerticalAlignment.Center);

            border.AppendChild(path);
            template.VisualTree = border;

            Trigger hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), "border"));
            hoverTrigger.Setters.Add(new Setter(System.Windows.Shapes.Path.FillProperty, Brushes.White, "iconPath"));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
            return btn;
        }

        private Button CreateColorButton()
        {
            Button btn = new Button
            {
                Width = 36,
                Height = 36,
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = "画笔颜色 (点击切换)",
                Focusable = false
            };

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

            FrameworkElementFactory grid = new FrameworkElementFactory(typeof(Grid));

            FrameworkElementFactory outerRing = new FrameworkElementFactory(typeof(Ellipse));
            outerRing.SetValue(Ellipse.WidthProperty, 22.0);
            outerRing.SetValue(Ellipse.HeightProperty, 22.0);
            outerRing.SetValue(Ellipse.StrokeProperty, new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)));
            outerRing.SetValue(Ellipse.StrokeThicknessProperty, 1.5);

            FrameworkElementFactory badge = new FrameworkElementFactory(typeof(Ellipse));
            badge.Name = "colorDot";
            badge.SetValue(Ellipse.WidthProperty, 15.0);
            badge.SetValue(Ellipse.HeightProperty, 15.0);
            badge.SetValue(Ellipse.FillProperty, new SolidColorBrush(_settings.CurrentColor));

            grid.AppendChild(outerRing);
            grid.AppendChild(badge);
            border.AppendChild(grid);
            template.VisualTree = border;

            btn.Template = template;
            btn.Loaded += (s, e) =>
            {
                _colorBadge = btn.Template.FindName("colorDot", btn) as Ellipse;
            };

            // 与 CreateMenuButton 同理：必须走 Click（mouse-up）触发。
            // 若在 PreviewMouseLeftButtonDown 上打开 Popup，StaysOpen=false 会把
            // 紧随其后的松手当成外部点击，菜单刚开就被关掉。
            btn.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                ToggleColorPopup();
            };

            return btn;
        }

        private Button CreatePalmRejectionButton()
        {
            Button btn = new Button
            {
                Width = 36,
                Height = 36,
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = "手笔分离 (防手掌误触)\n开启：仅允许 Surface Pen 触控笔书写，手掌可全贴屏幕\n关闭：允许手指触摸直接作画",
                Focusable = false
            };

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "border";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

            FrameworkElementFactory grid = new FrameworkElementFactory(typeof(Grid));

            FrameworkElementFactory icon = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            icon.SetValue(System.Windows.Shapes.Path.DataProperty, VectorIcons.Parse(VectorIcons.PalmRejection));
            icon.SetValue(System.Windows.Shapes.Path.FillProperty, new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)));
            icon.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);
            icon.SetValue(System.Windows.Shapes.Path.WidthProperty, 17.0);
            icon.SetValue(System.Windows.Shapes.Path.HeightProperty, 17.0);

            FrameworkElementFactory badge = new FrameworkElementFactory(typeof(Ellipse));
            badge.Name = "palmDot";
            badge.SetValue(Ellipse.WidthProperty, 7.0);
            badge.SetValue(Ellipse.HeightProperty, 7.0);
            badge.SetValue(Ellipse.FillProperty, new SolidColorBrush(Color.FromRgb(82, 196, 26)));
            badge.SetValue(Ellipse.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            badge.SetValue(Ellipse.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            badge.SetValue(Ellipse.MarginProperty, new Thickness(0, 0, 5, 5));

            grid.AppendChild(icon);
            grid.AppendChild(badge);
            border.AppendChild(grid);
            template.VisualTree = border;

            btn.Template = template;
            btn.Loaded += (s, e) =>
            {
                _palmBadge = btn.Template.FindName("palmDot", btn) as Ellipse;
                UpdatePalmRejectionButton();
            };

            WireButtonAction(btn, () =>
            {
                _settings.PalmRejectionEnabled = !_settings.PalmRejectionEnabled;
                UpdatePalmRejectionButton();
                if (_settings.PalmRejectionEnabled)
                {
                    ShowToast("✓ 手笔分离已开启：触控笔流畅书写，手掌手指可穿透操作下层");
                }
                else
                {
                    ShowToast("! 手笔分离已关闭：允许手指触摸直接在屏幕上作画");
                }
            });

            return btn;
        }

        private void UpdatePalmRejectionButton()
        {
            if (_palmBadge != null)
            {
                _palmBadge.Fill = _settings.PalmRejectionEnabled
                    ? new SolidColorBrush(Color.FromRgb(82, 196, 26))
                    : new SolidColorBrush(Color.FromArgb(120, 200, 200, 200));
            }
        }

        private Border CreateDivider()
        {
            return new Border
            {
                Width = 1,
                Height = 22,
                Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // ======================= 弹出层 (调色盘、图形、粗细、底衬) =======================

        private void InitPopups()
        {
            // 1. 颜色弹窗
            _colorPopup = new Popup
            {
                PlacementTarget = _btnColor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                VerticalOffset = 8
            };

            Border colorBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 28, 30, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Color = Colors.Black, Opacity = 0.5 }
            };

            StackPanel colorStack = new StackPanel { Orientation = Orientation.Vertical };

            // 色块构造抽成委托，主色板与扩展色板共用同一套外观与行为
            Action<Color, UniformGrid> addSwatch = (clr, grid) =>
            {
                Color current = clr;
                Button clrBtn = new Button
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(4),
                    Cursor = Cursors.Hand,
                    Focusable = false
                };

                ControlTemplate t = new ControlTemplate(typeof(Button));
                FrameworkElementFactory eBorder = new FrameworkElementFactory(typeof(Border));
                eBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
                eBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(current));
                eBorder.SetValue(Border.BorderThicknessProperty, new Thickness(current == Colors.White ? 1.5 : 0));
                eBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)));
                t.VisualTree = eBorder;
                clrBtn.Template = t;

                WireButtonAction(clrBtn, () =>
                {
                    _settings.CurrentColor = current;
                    if (_colorBadge != null)
                        _colorBadge.Fill = new SolidColorBrush(current);

                    RefreshThicknessPreview();
                    ApplyDrawingAttributes();
                    ScheduleSettingsSave();
                    _colorPopup.IsOpen = false;
                    ShowToast("已切换画笔颜色");
                });

                grid.Children.Add(clrBtn);
            };

            UniformGrid colorGrid = new UniformGrid { Columns = 5, Rows = 2 };
            foreach (Color clr in _presetColors) addSwatch(clr, colorGrid);
            colorStack.Children.Add(colorGrid);

            // ===== 扩展区：HSV 色轮 + 可编辑颜色代码（默认折叠，点下方箭头展开）=====
            // WPF 没有锥形渐变，色相环用 90 个小扇环拼出来；环内嵌 SV 方块
            // （横向 白→纯色相，纵向叠加 透明→黑）。
            const double RingRadius = 74.0;
            const double RingThick = 18.0;
            const double SquareSize = 88.0;
            const double RingInner = RingRadius - RingThick;

            Canvas wheelCanvas = new Canvas
            {
                Width = RingRadius * 2,
                Height = RingRadius * 2,
                Background = Brushes.Transparent,
                Cursor = Cursors.Cross,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            Func<double, double, Point> polar = (deg, r) =>
            {
                double rad = deg * Math.PI / 180.0;
                return new Point(RingRadius + r * Math.Cos(rad), RingRadius + r * Math.Sin(rad));
            };

            int segCount = 90;
            for (int i = 0; i < segCount; i++)
            {
                double a0 = i * 360.0 / segCount;
                double a1 = (i + 1) * 360.0 / segCount;

                PathFigure fig = new PathFigure();
                fig.StartPoint = polar(a0, RingRadius);
                fig.IsClosed = true;
                fig.Segments.Add(new ArcSegment(polar(a1, RingRadius),
                    new System.Windows.Size(RingRadius, RingRadius), 0, false, SweepDirection.Clockwise, true));
                fig.Segments.Add(new LineSegment(polar(a1, RingInner), true));
                fig.Segments.Add(new ArcSegment(polar(a0, RingInner),
                    new System.Windows.Size(RingInner, RingInner), 0, false, SweepDirection.Counterclockwise, true));

                PathGeometry geo = new PathGeometry();
                geo.Figures.Add(fig);

                System.Windows.Shapes.Path seg = new System.Windows.Shapes.Path();
                seg.Data = geo;
                seg.Fill = new SolidColorBrush(HsvToRgb(a0 + 180.0 / segCount, 1.0, 1.0));
                wheelCanvas.Children.Add(seg);
            }

            Grid svGrid = new Grid { Width = SquareSize, Height = SquareSize };

            Border svBase = new Border();
            LinearGradientBrush hueBrush = new LinearGradientBrush(Colors.White, Colors.Red, 0.0);
            svBase.Background = hueBrush;
            svGrid.Children.Add(svBase);

            Border svShade = new Border();
            svShade.Background = new LinearGradientBrush(
                Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0),
                new Point(0.5, 0.0), new Point(0.5, 1.0));
            svGrid.Children.Add(svShade);

            Border svFrame = new Border
            {
                Width = SquareSize,
                Height = SquareSize,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                Child = svGrid,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(svFrame, RingRadius - SquareSize / 2);
            Canvas.SetTop(svFrame, RingRadius - SquareSize / 2);
            wheelCanvas.Children.Add(svFrame);

            Ellipse hueMarker = new Ellipse
            {
                Width = 14,
                Height = 14,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            wheelCanvas.Children.Add(hueMarker);

            Ellipse svMarker = new Ellipse
            {
                Width = 12,
                Height = 12,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            wheelCanvas.Children.Add(svMarker);

            Border liveSwatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(_settings.CurrentColor),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
            };

            TextBox hexBox = new TextBox
            {
                Width = 96,
                Height = 26,
                Text = ToHex(_settings.CurrentColor),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            double[] hsv = RgbToHsv(_settings.CurrentColor);
            double hue = hsv[0];
            double sat = hsv[1];
            double val = hsv[2];

            Action refreshWheel = delegate
            {
                hueBrush.GradientStops[1].Color = HsvToRgb(hue, 1.0, 1.0);

                Point hp = polar(hue, RingRadius - RingThick / 2.0);
                Canvas.SetLeft(hueMarker, hp.X - hueMarker.Width / 2);
                Canvas.SetTop(hueMarker, hp.Y - hueMarker.Height / 2);

                double left = RingRadius - SquareSize / 2;
                Canvas.SetLeft(svMarker, left + sat * SquareSize - svMarker.Width / 2);
                Canvas.SetTop(svMarker, left + (1.0 - val) * SquareSize - svMarker.Height / 2);
            };

            Action<Color> applyColor = delegate(Color c)
            {
                _settings.CurrentColor = c;
                if (_colorBadge != null) _colorBadge.Fill = new SolidColorBrush(c);
                liveSwatch.Background = new SolidColorBrush(c);
                RefreshThicknessPreview();
                ApplyDrawingAttributes();
                ScheduleSettingsSave();
            };

            Action<double, double> pick = delegate(double px, double py)
            {
                double dx = px - RingRadius;
                double dy = py - RingRadius;
                double r = Math.Sqrt(dx * dx + dy * dy);

                if (r >= RingInner - 8 && r <= RingRadius + 8)
                {
                    double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                    if (ang < 0) ang += 360.0;
                    hue = ang;
                }
                else
                {
                    double left = RingRadius - SquareSize / 2;
                    double sx = (px - left) / SquareSize;
                    double sy = 1.0 - (py - left) / SquareSize;
                    if (sx < 0) sx = 0;
                    if (sx > 1) sx = 1;
                    if (sy < 0) sy = 0;
                    if (sy > 1) sy = 1;
                    sat = sx;
                    val = sy;
                }

                refreshWheel();

                Color c = HsvToRgb(hue, sat, val);
                applyColor(c);
                hexBox.Text = ToHex(c);
            };

            bool wheelDragging = false;
            wheelCanvas.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                wheelDragging = true;
                wheelCanvas.CaptureMouse();
                Point p = e.GetPosition(wheelCanvas);
                pick(p.X, p.Y);
                e.Handled = true;
            };
            wheelCanvas.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!wheelDragging) return;
                Point p = e.GetPosition(wheelCanvas);
                pick(p.X, p.Y);
            };
            wheelCanvas.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                wheelDragging = false;
                wheelCanvas.ReleaseMouseCapture();
                e.Handled = true;
            };

            Action applyHex = delegate
            {
                Color parsed;
                if (TryParseHex(hexBox.Text, out parsed))
                {
                    double[] h2 = RgbToHsv(parsed);
                    hue = h2[0];
                    sat = h2[1];
                    val = h2[2];
                    refreshWheel();
                    applyColor(parsed);
                    hexBox.Text = ToHex(parsed);
                }
                else
                {
                    hexBox.Text = ToHex(_settings.CurrentColor);
                }
            };

            hexBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    applyHex();
                    e.Handled = true;
                }
            };
            hexBox.LostFocus += delegate { applyHex(); };

            refreshWheel();

            StackPanel hexRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            hexRow.Children.Add(liveSwatch);
            hexRow.Children.Add(hexBox);

            StackPanel wheelPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 6, 0, 0)
            };
            wheelPanel.Children.Add(wheelCanvas);
            wheelPanel.Children.Add(hexRow);

            colorStack.Children.Add(wheelPanel);

            Button moreToggle = BuildIconButtonVisual(VectorIcons.ChevronDown, "更多颜色（色轮）");
            moreToggle.HorizontalAlignment = HorizontalAlignment.Center;
            moreToggle.Margin = new Thickness(0, 6, 0, 0);
            moreToggle.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                bool show = wheelPanel.Visibility != Visibility.Visible;
                wheelPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                SetButtonIcon(moreToggle, show ? VectorIcons.ChevronUp : VectorIcons.ChevronDown);
            };
            colorStack.Children.Add(moreToggle);

            colorBox.Child = colorStack;
            _colorPopup.Child = colorBox;

            // 2. 图形弹窗
            _shapesPopup = new Popup
            {
                PlacementTarget = _btnShapes,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                VerticalOffset = 8
            };

            Border shapeBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 28, 30, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(8),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Color = Colors.Black, Opacity = 0.5 }
            };

            StackPanel shapeStack = new StackPanel { Orientation = Orientation.Horizontal };
            shapeStack.Children.Add(CreatePopupButton(VectorIcons.Line, "直线", () => SetTool(DrawingToolType.ShapeLine)));
            shapeStack.Children.Add(CreatePopupButton(VectorIcons.Arrow, "箭头", () => SetTool(DrawingToolType.ShapeArrow)));
            shapeStack.Children.Add(CreatePopupButton(VectorIcons.Rect, "矩形", () => SetTool(DrawingToolType.ShapeRect)));
            shapeStack.Children.Add(CreatePopupButton(VectorIcons.Ellipse, "椭圆", () => SetTool(DrawingToolType.ShapeEllipse)));

            shapeBox.Child = shapeStack;
            _shapesPopup.Child = shapeBox;

            // 3. 粗细弹窗
            _thicknessPopup = new Popup
            {
                PlacementTarget = _btnThickness,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                VerticalOffset = 8
            };

            Border thickBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 28, 30, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 8, 10, 8),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Color = Colors.Black, Opacity = 0.5 }
            };

            // 粗细：实时尺寸预览 + 可拖动滑块
            StackPanel thickStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = 236
            };

            // 顶部一行：左侧是按真实笔宽渲染的圆点，右侧是数值
            StackPanel previewRow = new StackPanel { Orientation = Orientation.Horizontal };

            Border previewPad = new Border
            {
                Width = 58,
                Height = 58,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Margin = new Thickness(0, 0, 10, 0)
            };

            Ellipse previewDot = new Ellipse
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            previewPad.Child = previewDot;
            previewRow.Children.Add(previewPad);

            TextBlock thickValueText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            previewRow.Children.Add(thickValueText);

            thickStack.Children.Add(previewRow);

            Slider thickSlider = new Slider
            {
                Minimum = 1,
                Maximum = 40,
                Value = Math.Max(1, Math.Min(40, _settings.PenWidth)),
                SmallChange = 1,
                LargeChange = 5,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 10, 0, 0),
                Style = CreateDarkSliderStyle()
            };
            thickSlider.ValueChanged += (s, e) =>
            {
                double w = Math.Round(e.NewValue);
                _settings.PenWidth = w;
                _settings.HighlighterWidth = w * 3.5;
                RefreshThicknessPreview();
                ApplyDrawingAttributes();
                ScheduleSettingsSave();
            };
            thickStack.Children.Add(thickSlider);

            TextBlock thickHint = new TextBlock
            {
                Text = "拖动滑块实时调节；钢笔、荧光笔与像素橡皮同步生效",
                Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                FontSize = 10.5,
                Margin = new Thickness(2, 8, 2, 0),
                TextWrapping = TextWrapping.Wrap
            };
            thickStack.Children.Add(thickHint);

            _thicknessValueText = thickValueText;
            _thicknessPreviewDot = previewDot;
            RefreshThicknessPreview();

            thickBox.Child = thickStack;
            _thicknessPopup.Child = thickBox;

            // 4. 底衬弹窗
            _backdropPopup = new Popup
            {
                PlacementTarget = _btnBackdrop,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                VerticalOffset = 8
            };

            Border backBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 28, 30, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Color = Colors.Black, Opacity = 0.5 }
            };

            StackPanel backStack = new StackPanel { Orientation = Orientation.Horizontal };
            backStack.Children.Add(CreateTextPopupButton("透明桌面", () => SetBackdrop(BackdropType.Transparent)));
            backStack.Children.Add(CreateTextPopupButton("纯白白板", () => SetBackdrop(BackdropType.Whiteboard)));
            backStack.Children.Add(CreateTextPopupButton("护眼黑板", () => SetBackdrop(BackdropType.Blackboard)));

            backBox.Child = backStack;
            _backdropPopup.Child = backBox;

            // 5. 橡皮模式弹窗（长按橡皮按钮打开）
            _eraserPopup = new Popup
            {
                PlacementTarget = _btnEraser,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                VerticalOffset = 8
            };

            Border eraserBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 28, 30, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Color = Colors.Black, Opacity = 0.5 }
            };

            StackPanel eraserStack = new StackPanel { Orientation = Orientation.Horizontal };
            eraserStack.Children.Add(CreateTextPopupButton("笔迹擦除", () =>
            {
                _eraserMode = DrawingToolType.EraserStroke;
                SetTool(_eraserMode);
                ShowToast("橡皮：整条笔迹擦除");
            }));
            eraserStack.Children.Add(CreateTextPopupButton("像素擦除", () =>
            {
                _eraserMode = DrawingToolType.EraserPoint;
                SetTool(_eraserMode);
                ShowToast("橡皮：像素级擦除");
            }));

            eraserBox.Child = eraserStack;
            _eraserPopup.Child = eraserBox;
        }

        /// <summary>
        /// 弹出菜单里"菜单项"的接线。
        ///
        /// 走 Click（mouse-up 合成，鼠标 / 触摸 / 笔每个手势只触发一次），并且
        /// **先执行动作、再关闭菜单**。顺序不能反：若先 CloseAllPopups()，就会在菜单项
        /// 自己的输入事件还在处理时把承载它的 Popup 销毁掉，鼠标捕获随之释放，
        /// 同一次手势的 up 便落到下层元素上，可能误触发工具栏按钮。
        /// </summary>
        private void WirePopupItem(Button btn, Action action)
        {
            btn.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                if (action != null) action();
                CloseAllPopups();
            };
        }

        private Button CreatePopupButton(string pathData, string tip, Action action)
        {
            Button btn = BuildIconButtonVisual(pathData, tip);
            WirePopupItem(btn, action);
            return btn;
        }

        private Button CreateTextPopupButton(string text, Action action)
        {
            Button btn = new Button
            {
                Content = text,
                Foreground = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(4),
                Padding = new Thickness(10, 6, 10, 6),
                Cursor = Cursors.Hand,
                Focusable = false
            };

            ControlTemplate t = new ControlTemplate(typeof(Button));
            FrameworkElementFactory b = new FrameworkElementFactory(typeof(Border));
            b.Name = "b";
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            b.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            b.AppendChild(cp);
            t.VisualTree = b;

            Trigger h = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            h.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(180, 24, 144, 255)), "b"));
            t.Triggers.Add(h);

            btn.Template = t;
            WirePopupItem(btn, action);
            return btn;
        }

        private void CloseAllPopups()
        {
            if (_colorPopup != null) _colorPopup.IsOpen = false;
            if (_shapesPopup != null) _shapesPopup.IsOpen = false;
            if (_thicknessPopup != null) _thicknessPopup.IsOpen = false;
            if (_backdropPopup != null) _backdropPopup.IsOpen = false;
            if (_eraserPopup != null) _eraserPopup.IsOpen = false;
        }

        private void ToggleEraserPopup()
        {
            if (_eraserPopup == null) return;
            bool isOpen = _eraserPopup.IsOpen;
            CloseAllPopups();
            _eraserPopup.IsOpen = !isOpen;
        }

        private void ToggleColorPopup()
        {
            bool isOpen = _colorPopup.IsOpen;
            CloseAllPopups();
            _colorPopup.IsOpen = !isOpen;
        }

        private void ToggleShapesPopup()
        {
            bool isOpen = _shapesPopup.IsOpen;
            CloseAllPopups();
            _shapesPopup.IsOpen = !isOpen;
        }

        private void ToggleThicknessPopup()
        {
            bool isOpen = _thicknessPopup.IsOpen;
            CloseAllPopups();
            _thicknessPopup.IsOpen = !isOpen;
        }

        private void ToggleBackdropPopup()
        {
            bool isOpen = _backdropPopup.IsOpen;
            CloseAllPopups();
            _backdropPopup.IsOpen = !isOpen;
        }

        // ======================= 工具状态与高亮 =======================

        private void SetButtonActiveState(Button btn, bool active)
        {
            if (btn == null) return;
            Border border = btn.Template.FindName("border", btn) as Border;
            if (border != null)
            {
                border.Background = active
                    ? new SolidColorBrush(Color.FromArgb(220, 24, 144, 255))
                    : Brushes.Transparent;
            }
        }

        private void UpdateActiveToolButton(DrawingToolType tool)
        {
            SetButtonActiveState(_btnPen, tool == DrawingToolType.Pen);
            SetButtonActiveState(_btnHighlighter, tool == DrawingToolType.Highlighter);
            SetButtonActiveState(_btnLaser, tool == DrawingToolType.Laser);
            SetButtonActiveState(_btnEraser, tool == DrawingToolType.EraserStroke || tool == DrawingToolType.EraserPoint);
            SetButtonActiveState(_btnShapes,
                tool == DrawingToolType.ShapeLine ||
                tool == DrawingToolType.ShapeArrow ||
                tool == DrawingToolType.ShapeRect ||
                tool == DrawingToolType.ShapeEllipse);
        }

        private void UpdatePassthroughButton(bool passthrough)
        {
            if (_btnPassthrough != null)
            {
                SetButtonActiveState(_btnPassthrough, passthrough);
                _btnPassthrough.ToolTip = passthrough
                    ? "当前处于【穿透桌面模式】\n可正常点击操作下层软件\n点击此按钮切回画板继续绘画"
                    : "鼠标穿透 (操作桌面)\n笔迹保留在屏幕上，可直接点击下层网页与软件";
            }
        }

        private void UpdateHistoryButtons()
        {
            if (_btnUndo != null) _btnUndo.Opacity = _historyManager.CanUndo ? 1.0 : 0.35;
            if (_btnRedo != null) _btnRedo.Opacity = _historyManager.CanRedo ? 1.0 : 0.35;
        }

        // ======================= 绘图属性与工具切换 =======================

        public void ApplyDrawingAttributes()
        {
            var da = new DrawingAttributes
            {
                Color = _settings.CurrentColor,
                IgnorePressure = false, // 启用 Surface Pen 4096级原生压感
                FitToCurve = true,      // 启用贝塞尔曲线平滑笔锋
                StylusTip = StylusTip.Ellipse
            };

            if (_currentTool == DrawingToolType.Highlighter)
            {
                da.IsHighlighter = true;
                da.Width = _settings.HighlighterWidth;
                da.Height = _settings.HighlighterWidth;
                da.StylusTip = StylusTip.Rectangle;
            }
            else
            {
                da.IsHighlighter = false;
                da.Width = _settings.PenWidth;
                da.Height = _settings.PenWidth;
            }

            if (_inkCanvas != null)
            {
                _inkCanvas.DefaultDrawingAttributes = da;

                // 像素橡皮的擦除半径跟随笔迹粗细，做到"预览显示多大、就擦多大"。
                // EraserShape 同时影响 EraseByPoint 与 EraseByStroke 的命中区域。
                double eraserSize = Math.Max(2.0, _settings.PenWidth);
                _inkCanvas.EraserShape = new EllipseStylusShape(eraserSize, eraserSize);
            }

            if (_shapePreview != null)
            {
                // 走 UpdateAppearance 而不是直接赋属性：后者不触发重绘，
                // 拖拽几何图形时中途改颜色 / 线宽会延迟到下次鼠标移动才生效。
                _shapePreview.UpdateAppearance(_settings.CurrentColor, _settings.PenWidth);
            }

            if (_laserRenderer != null)
            {
                _laserRenderer.LaserColor = _settings.CurrentColor;
            }
        }

        public void SetTool(DrawingToolType tool)
        {
            if (_isPassthrough)
            {
                _isPassthrough = false;
                _inkCanvas.IsHitTestVisible = true;
                UpdatePassthroughButton(false);
            }

            _currentTool = tool;
            _laserRenderer.SetActive(tool == DrawingToolType.Laser);
            _shapePreview.Clear();

            switch (tool)
            {
                case DrawingToolType.Pen:
                    _inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    ApplyDrawingAttributes();
                    ShowToast("已选择：压感笔 (Surface Pen 4096级高精度压感)");
                    break;

                case DrawingToolType.Highlighter:
                    _inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    ApplyDrawingAttributes();
                    ShowToast("已选择：半透明荧光笔");
                    break;

                case DrawingToolType.Laser:
                    _inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    ShowToast("已选择：激光教鞭 (光轨自动淡出)");
                    break;

                case DrawingToolType.EraserStroke:
                    _inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                    ShowToast("已选择：笔段橡皮擦");
                    break;

                case DrawingToolType.EraserPoint:
                    _inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                    ShowToast("已选择：像素点橡皮擦");
                    break;

                case DrawingToolType.ShapeLine:
                case DrawingToolType.ShapeArrow:
                case DrawingToolType.ShapeRect:
                case DrawingToolType.ShapeEllipse:
                    _inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    ShowToast(string.Format("已选择：{0}", GetToolChineseName(tool)));
                    break;
            }

            UpdateActiveToolButton(tool);
        }

        private string GetToolChineseName(DrawingToolType tool)
        {
            switch (tool)
            {
                case DrawingToolType.ShapeLine: return "直线工具";
                case DrawingToolType.ShapeArrow: return "箭头工具";
                case DrawingToolType.ShapeRect: return "矩形工具";
                case DrawingToolType.ShapeEllipse: return "椭圆工具";
                default: return tool.ToString();
            }
        }

        public void SetBackdrop(BackdropType backdrop)
        {
            _currentBackdrop = backdrop;
            switch (backdrop)
            {
                case BackdropType.Transparent:
                    _backdropBorder.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                    _inkCanvas.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                    ShowToast("底衬：透明屏幕");
                    break;
                case BackdropType.Whiteboard:
                    _backdropBorder.Background = new SolidColorBrush(Color.FromRgb(250, 250, 252));
                    _inkCanvas.Background = Brushes.Transparent;
                    ShowToast("底衬：纯白白板");
                    break;
                case BackdropType.Blackboard:
                    _backdropBorder.Background = new SolidColorBrush(Color.FromRgb(26, 32, 28));
                    _inkCanvas.Background = Brushes.Transparent;
                    ShowToast("底衬：护眼黑板");
                    break;
            }
        }

        /// <summary>
        /// 开关「自动手笔分离」。
        ///
        /// 开启后窗口常态挂 WS_EX_TRANSPARENT（手指/鼠标完全穿透到下层），
        /// 由 Raw Input 感知笔是否进入感应范围：进入则摘掉穿透让笔可写，
        /// 离开则重新挂回穿透。整个过程无需用户按键。
        /// </summary>
        public void SetAutoSeparation(bool on)
        {
            if (on == _autoSeparation) return;
            _autoSeparation = on;

            if (on)
            {
                if (!_penRawInputRegistered && _windowHandle != IntPtr.Zero)
                {
                    _penRawInputRegistered = NativeMethods.RegisterPenRawInput(_windowHandle, true);
                    NativeMethods.InputDiagnostics.Log(_penRawInputRegistered
                        ? "自动手笔分离：Raw Input（Digitizer/Pen, RIDEV_INPUTSINK）注册成功"
                        : "自动手笔分离：Raw Input 注册失败，笔悬停将无法被感知");
                }

                if (_penPresenceTimer == null)
                {
                    _penPresenceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    _penPresenceTimer.Tick += (s, e) =>
                    {
                        _penPresenceTimer.Stop();
                        _penInRange = false;
                        ApplyAutoSeparation();
                    };
                }

                _penInRange = false;
                _autoTransparent = false;   // 复位，强制首次真正写入穿透样式
                ApplyAutoSeparation();

                // 自动模式下窗口常处于穿透态，必须保证有恢复入口
                if (_escapeHatch == null) _escapeHatch = new EscapeHatchWindow(BuildEscapeMenu);
                _escapeHatch.Show();

                ShowToast("已开启【自动手笔分离】：笔靠近即书写，笔离开即穿透（点右下角圆钮关闭）");
            }
            else
            {
                if (_penPresenceTimer != null) _penPresenceTimer.Stop();

                if (_penRawInputRegistered)
                {
                    NativeMethods.RegisterPenRawInput(_windowHandle, false);
                    _penRawInputRegistered = false;
                }

                _penInRange = false;
                _autoTransparent = false;
                NativeMethods.SetClickThrough(this, false);

                // 恢复工具栏：自动模式期间它可能因穿透态被收起
                if (_mainPillBorder != null)
                    _mainPillBorder.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                if (_miniBallBorder != null)
                    _miniBallBorder.Visibility = _isCollapsed ? Visibility.Visible : Visibility.Collapsed;

                if (_escapeHatch != null) _escapeHatch.Hide();

                ShowToast("已关闭【自动手笔分离】，恢复手动模式");
            }

            UpdateAutoSeparationButton();
        }

        /// <summary>
        /// 悬浮球菜单内容按当前状态动态生成，保证菜单里显示的动作与实际可执行的操作一致。
        /// </summary>
        private System.Collections.Generic.List<EscapeMenuItem> BuildEscapeMenu()
        {
            System.Collections.Generic.List<EscapeMenuItem> list =
                new System.Collections.Generic.List<EscapeMenuItem>();

            list.Add(new EscapeMenuItem(
                _autoSeparation ? "关闭自动手笔分离" : "开启自动手笔分离",
                delegate { SetAutoSeparation(!_autoSeparation); }));

            list.Add(new EscapeMenuItem(
                _isPassthrough ? "退出穿透模式" : "进入穿透模式",
                TogglePassthrough));

            list.Add(new EscapeMenuItem(
                "退出灵光画笔",
                delegate { Application.Current.Shutdown(); }));

            return list;
        }

        /// <summary>按笔的感应状态写入穿透样式。仅在状态真正变化时才动窗口样式。</summary>
        private void ApplyAutoSeparation()
        {
            if (!_autoSeparation) return;

            bool shouldTransparent = !_penInRange;
            if (shouldTransparent == _autoTransparent) return;

            _autoTransparent = shouldTransparent;
            NativeMethods.SetClickThrough(this, shouldTransparent);

            // 穿透态下工具栏既点不了又占屏幕，一并收起；笔靠近时再显示出来。
            bool showToolbar = !shouldTransparent;
            if (_mainPillBorder != null)
            {
                _mainPillBorder.Visibility =
                    (showToolbar && !_isCollapsed) ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_miniBallBorder != null)
            {
                _miniBallBorder.Visibility =
                    (showToolbar && _isCollapsed) ? Visibility.Visible : Visibility.Collapsed;
            }

            NativeMethods.InputDiagnostics.Log(shouldTransparent
                ? "自动手笔分离：笔离开感应范围 -> 恢复穿透"
                : "自动手笔分离：感知到笔 -> 摘掉穿透，可书写");
        }

        /// <summary>收到笔的原始输入：标记在范围内，并靠 300ms 静默超时判定离开。</summary>
        private void OnPenRawInput()
        {
            bool wasInRange = _penInRange;
            _penInRange = true;

            if (_penPresenceTimer != null)
            {
                _penPresenceTimer.Stop();
                _penPresenceTimer.Start();
            }

            if (!wasInRange) ApplyAutoSeparation();
        }

        private void UpdateAutoSeparationButton()
        {
            if (_btnAutoSeparation == null || _btnAutoSeparation.Template == null) return;

            var icon = _btnAutoSeparation.Template.FindName("iconPath", _btnAutoSeparation)
                as System.Windows.Shapes.Path;
            if (icon != null)
            {
                icon.Fill = _autoSeparation
                    ? new SolidColorBrush(Color.FromRgb(24, 144, 255))
                    : new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
            }
        }

        public void TogglePassthrough()
        {
            // 自动手笔分离接管期间，这个入口改为"关闭自动模式"，
            // 保证用户在任何状态下都能退回手动控制。
            if (_autoSeparation)
            {
                SetAutoSeparation(false);
                return;
            }

            _isPassthrough = !_isPassthrough;
            UpdatePassthroughButton(_isPassthrough);

            // 关键：必须真正切换窗口的 WS_EX_TRANSPARENT 扩展样式。
            // 仅设置 InkCanvas.IsHitTestVisible 只影响 WPF 内部命中测试，
            // 窗口本身依旧参与系统级命中测试、依旧吃掉输入，无法到达下层应用。
            // WS_EX_TRANSPARENT 是窗口级的，跨进程可靠，这是唯一能真正穿透的机制。
            NativeMethods.SetClickThrough(this, _isPassthrough);

            if (_isPassthrough)
            {
                _inkCanvas.EditingMode = InkCanvasEditingMode.None;
                _inkCanvas.IsHitTestVisible = false;
                // 穿透模式下主窗口整体退出命中测试，工具栏既点不了又占着屏幕，直接收起。
                _mainPillBorder.Visibility = Visibility.Collapsed;
                _miniBallBorder.Visibility = Visibility.Collapsed;
                ShowToast("已进入【穿透桌面模式】：手指/鼠标直接操作下层；点右下角圆钮退出");
            }
            else
            {
                _inkCanvas.IsHitTestVisible = true;
                SetTool(_currentTool);
                // 按当前折叠状态恢复对应的工具栏形态
                _mainPillBorder.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                _miniBallBorder.Visibility = _isCollapsed ? Visibility.Visible : Visibility.Collapsed;
                ShowToast("已恢复【涂鸦画板模式】：可直接在屏幕上作画圈点");
            }

            // 穿透模式下主窗口连同工具栏一起退出命中测试，必须靠独立的
            // 逃生舱窗口提供恢复入口，否则用户会被困在穿透模式里（真机实测确认）。
            if (_escapeHatch == null)
            {
                _escapeHatch = new EscapeHatchWindow(BuildEscapeMenu);
            }
            if (_isPassthrough) _escapeHatch.Show();
            else _escapeHatch.Hide();

            NativeMethods.InputDiagnostics.Log(_isPassthrough
                ? "=== 模式切换：穿透桌面模式（WS_EX_TRANSPARENT 已开启）==="
                : "=== 模式切换：涂鸦画板模式（WS_EX_TRANSPARENT 已关闭）===");
        }

        /// <summary>
        /// 打开 / 聚焦自定义画板（悬浮小黑板）。
        /// 窗口是独立顶层窗口，与主画板互不干扰；关闭后置空以便下次重新创建。
        /// </summary>
        private void ToggleBoardWindow()
        {
            if (_boardWindow == null)
            {
                _boardWindow = new BoardWindow();
                _boardWindow.Closed += delegate { _boardWindow = null; };
                _boardWindow.Show();
                ShowToast("已打开【自定义画板】：标题栏拖动、右下角缩放、图钉固定在最前面");
                return;
            }

            if (!_boardWindow.IsVisible) _boardWindow.Show();
            _boardWindow.Activate();
        }

        /// <summary>
        /// 展开 / 收起附加工具区（激光、图形、粗细、手笔分离、背景、重做、截图、设置、退出）。
        /// </summary>
        public void ToggleMorePanel()
        {
            if (_advancedPanel == null) return;

            // 先记录切换前的几何，作为锚点补偿的依据
            double oldLeft = Canvas.GetLeft(_mainPillBorder);
            double oldW = _mainPillBorder.ActualWidth;
            if (double.IsNaN(oldLeft)) oldLeft = 20;
            if (oldW <= 0) oldW = 320;

            bool show = _advancedPanel.Visibility != Visibility.Visible;
            _advancedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (_btnMore != null)
            {
                _btnMore.Background = show
                    ? new SolidColorBrush(Color.FromArgb(70, 255, 255, 255))
                    : Brushes.Transparent;
            }

            _mainPillBorder.UpdateLayout();
            double newW = _mainPillBorder.ActualWidth;
            if (newW <= 0) newW = oldW;

            // 锚点补偿：宽度变化时固定"靠屏幕内侧"的那条边，
            // 让工具栏朝外侧伸缩，而不是整体漂移。
            double newLeft;
            if (_collapseAtRight)
            {
                // 收起按钮在右端 → 右边缘是锚，保持不动
                double rightEdge = oldLeft + oldW;
                newLeft = rightEdge - newW;
            }
            else
            {
                // 收起按钮在左端 → 左边缘是锚，保持不动
                newLeft = oldLeft;
            }

            newLeft = Math.Max(10, Math.Min(ActualWidth - newW - 10, newLeft));
            Canvas.SetLeft(_mainPillBorder, newLeft);

            UpdateToastPosition();
        }

        /// <summary>把浮动通知 Toast 重新对齐到工具栏正下方。</summary>
        private void UpdateToastPosition()
        {
            if (_mainPillBorder == null || _toastBorder == null) return;
            double left = Canvas.GetLeft(_mainPillBorder);
            double top = Canvas.GetTop(_mainPillBorder);
            if (double.IsNaN(left)) left = 20;
            if (double.IsNaN(top)) top = 20;

            Canvas.SetLeft(_toastBorder, Math.Max(10, left + (_mainPillBorder.ActualWidth - _toastBorder.ActualWidth) / 2));
            Canvas.SetTop(_toastBorder, top + _mainPillBorder.ActualHeight + 8);
        }

        /// <summary>
        /// 按展开方向重排工具栏。向左展开时整条菜单镜像排列，
        /// 收起按钮、笔、橡皮等全部落到靠屏幕内侧的那一端，
        /// 即"右侧展开"与"左侧展开"两套顺序刚好相反。
        /// </summary>
        private void ApplyToolbarDirection(bool expandLeft)
        {
            if (_toolsPanel == null || _btnCollapse == null) return;

            _collapseAtRight = expandLeft;

            // 图标 Data 是烘进 ControlTemplate 的，需通过模板查找实际 Path 才能替换
            var iconPath = _btnCollapse.Template == null
                ? null
                : _btnCollapse.Template.FindName("iconPath", _btnCollapse) as System.Windows.Shapes.Path;
            if (iconPath != null)
            {
                iconPath.Data = VectorIcons.Parse(
                    expandLeft ? VectorIcons.CollapseLeft : VectorIcons.CollapseRight);
            }

            // 首次调用时记录正向逻辑顺序，之后每次按方向重建
            if (_toolbarLogicalOrder == null)
            {
                _toolbarLogicalOrder = new System.Collections.Generic.List<UIElement>();
                foreach (UIElement child in _toolsPanel.Children)
                    _toolbarLogicalOrder.Add(child);
            }

            _toolsPanel.Children.Clear();
            if (expandLeft)
            {
                for (int i = _toolbarLogicalOrder.Count - 1; i >= 0; i--)
                    _toolsPanel.Children.Add(_toolbarLogicalOrder[i]);
            }
            else
            {
                for (int i = 0; i < _toolbarLogicalOrder.Count; i++)
                    _toolsPanel.Children.Add(_toolbarLogicalOrder[i]);
            }

            // 「更多」面板内部同样镜像，否则退出按钮会停在靠近收起按钮的一端，
            // 而不是落在整条菜单的最外端。
            if (_advancedPanel != null)
            {
                if (_advancedLogicalOrder == null)
                {
                    _advancedLogicalOrder = new System.Collections.Generic.List<UIElement>();
                    foreach (UIElement child in _advancedPanel.Children)
                        _advancedLogicalOrder.Add(child);
                }

                _advancedPanel.Children.Clear();
                if (expandLeft)
                {
                    for (int i = _advancedLogicalOrder.Count - 1; i >= 0; i--)
                        _advancedPanel.Children.Add(_advancedLogicalOrder[i]);
                }
                else
                {
                    for (int i = 0; i < _advancedLogicalOrder.Count; i++)
                        _advancedPanel.Children.Add(_advancedLogicalOrder[i]);
                }
            }
        }

        public void ToggleCollapse()
        {
            CloseAllPopups();

            if (!_isCollapsed)
            {
                // 折叠：悬浮球落在收起按钮所在的那一端，视觉上像被按钮收进去。
                double pillLeft = Canvas.GetLeft(_mainPillBorder);
                double pillTop = Canvas.GetTop(_mainPillBorder);
                if (double.IsNaN(pillLeft)) pillLeft = 20;
                if (double.IsNaN(pillTop)) pillTop = 20;

                double pillW = _mainPillBorder.ActualWidth;
                if (pillW <= 0) pillW = 320;
                double ballW = _miniBallBorder.Width > 0 ? _miniBallBorder.Width : 44;

                // 按钮在最右端就贴右端，否则贴左端
                double ballLeft = _collapseAtRight
                    ? pillLeft + pillW - ballW
                    : pillLeft;
                double ballTop = pillTop;

                ballLeft = Math.Max(10, Math.Min(ActualWidth - ballW - 10, ballLeft));
                ballTop = Math.Max(10, Math.Min(ActualHeight - ballW - 10, ballTop));

                Canvas.SetLeft(_miniBallBorder, ballLeft);
                Canvas.SetTop(_miniBallBorder, ballTop);

                _mainPillBorder.Visibility = Visibility.Collapsed;
                _miniBallBorder.Visibility = Visibility.Visible;
                _isCollapsed = true;
            }
            else
            {
                // 展开
                double ballLeft = Canvas.GetLeft(_miniBallBorder);
                double ballTop = Canvas.GetTop(_miniBallBorder);
                if (double.IsNaN(ballLeft)) ballLeft = 20;
                if (double.IsNaN(ballTop)) ballTop = 20;

                // 关键：Collapsed 状态下 ActualWidth 为 0，用它算右边界等于没算，
                // 工具栏会从球的位置一路向右铺到屏幕外。先置为可见并强制测量，
                // 拿到真实期望尺寸后再决定展开方向。
                _mainPillBorder.Visibility = Visibility.Visible;
                _mainPillBorder.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double pillW = _mainPillBorder.DesiredSize.Width;
                double pillH = _mainPillBorder.DesiredSize.Height;
                if (pillW <= 0) pillW = 320;
                if (pillH <= 0) pillH = 44;

                // 默认从球的左边缘向右展开；右侧放不下时改为向左展开，
                // 让工具栏右边缘贴住球的右边缘。
                bool expandLeft = (ballLeft + pillW > ActualWidth - 10);

                // 整条菜单跟着镜像：收起按钮、笔、橡皮都落到靠屏幕内侧的那一端
                ApplyToolbarDirection(expandLeft);

                // 重排后重新测量，避免沿用镜像前的尺寸
                _mainPillBorder.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                pillW = _mainPillBorder.DesiredSize.Width;
                pillH = _mainPillBorder.DesiredSize.Height;
                if (pillW <= 0) pillW = 320;
                if (pillH <= 0) pillH = 44;

                double targetLeft = expandLeft ? (ActualWidth - pillW - 10) : ballLeft;
                targetLeft = Math.Max(10, targetLeft);

                double targetTop = Math.Max(10, Math.Min(ActualHeight - pillH - 10, ballTop));

                Canvas.SetLeft(_mainPillBorder, targetLeft);
                Canvas.SetTop(_mainPillBorder, targetTop);

                _miniBallBorder.Visibility = Visibility.Collapsed;
                _isCollapsed = false;
            }
        }

        public void ShowToast(string message)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                _toastText.Text = message;
                _toastBorder.UpdateLayout();

                double pillLeft = Canvas.GetLeft(_mainPillBorder);
                double pillTop = Canvas.GetTop(_mainPillBorder);

                Canvas.SetLeft(_toastBorder, Math.Max(10, pillLeft + (_mainPillBorder.ActualWidth - _toastBorder.ActualWidth) / 2));
                Canvas.SetTop(_toastBorder, pillTop + _mainPillBorder.ActualHeight + 8);

                _toastBorder.BeginAnimation(UIElement.OpacityProperty, null);
                _toastBorder.Opacity = 1.0;

                if (_toastTimer != null) _toastTimer.Stop();
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
                _toastTimer.Tick += (s, e) =>
                {
                    _toastTimer.Stop();
                    DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
                    _toastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };
                _toastTimer.Start();
            }));
        }

        // ======================= 几何图形与激光指针处理 =======================

        private bool IsShapeTool(DrawingToolType tool)
        {
            return tool == DrawingToolType.ShapeLine ||
                   tool == DrawingToolType.ShapeArrow ||
                   tool == DrawingToolType.ShapeRect ||
                   tool == DrawingToolType.ShapeEllipse;
        }

        private void HandlePointerDown(Point pt)
        {
            if (_currentTool == DrawingToolType.Laser)
            {
                _laserRenderer.AddPoint(pt);
            }
            else if (IsShapeTool(_currentTool))
            {
                _shapeStartPoint = pt;
                _isDraggingShape = true;
                _shapePreview.SetPreview(_currentTool, pt, pt, _settings.CurrentColor, _settings.PenWidth);
            }
        }

        private void HandlePointerMove(Point pt)
        {
            if (_currentTool == DrawingToolType.Laser)
            {
                _laserRenderer.AddPoint(pt);
            }
            else if (_isDraggingShape && _shapeStartPoint.HasValue)
            {
                _shapePreview.SetPreview(_currentTool, _shapeStartPoint.Value, pt, _settings.CurrentColor, _settings.PenWidth);
            }
        }

        private void HandlePointerUp(Point pt)
        {
            if (_currentTool == DrawingToolType.Laser)
            {
                _laserRenderer.EndStroke();
            }
            else if (_isDraggingShape && _shapeStartPoint.HasValue)
            {
                _isDraggingShape = false;
                _shapePreview.Clear();

                Point start = _shapeStartPoint.Value;
                _shapeStartPoint = null;

                double dist = (pt - start).Length;
                if (dist > 4)
                {
                    Stroke shapeStroke = ShapeGenerator.CreateShapeStroke(
                        _currentTool, start, pt, _inkCanvas.DefaultDrawingAttributes);

                    _inkCanvas.Strokes.Add(shapeStroke);
                }
            }
        }

        private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null)
            {
                if (_settings.PalmRejectionEnabled && !PalmRejectionManager.IsPenDevice(e.StylusDevice)) return;
            }
            else
            {
                if (!_settings.AllowMouseDrawing && !IsShapeTool(_currentTool) && _currentTool != DrawingToolType.Laser) return;
            }

            Point pt = e.GetPosition(_inkCanvas);
            HandlePointerDown(pt);
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (e.StylusDevice != null)
            {
                if (_settings.PalmRejectionEnabled && !PalmRejectionManager.IsPenDevice(e.StylusDevice)) return;
            }
            Point pt = e.GetPosition(_inkCanvas);
            HandlePointerMove(pt);
        }

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null)
            {
                if (_settings.PalmRejectionEnabled && !PalmRejectionManager.IsPenDevice(e.StylusDevice)) return;
            }
            Point pt = e.GetPosition(_inkCanvas);
            HandlePointerUp(pt);
        }

        private void OnCanvasStylusDown(object sender, StylusDownEventArgs e)
        {
            if (_settings.PalmRejectionEnabled && !PalmRejectionManager.IsPenDevice(e.StylusDevice))
            {
                return;
            }

            Point pt = e.GetPosition(_inkCanvas);
            HandlePointerDown(pt);
        }

        private void OnCanvasStylusMove(object sender, StylusEventArgs e)
        {
            if (_settings.PalmRejectionEnabled && !PalmRejectionManager.IsPenDevice(e.StylusDevice))
            {
                return;
            }

            Point pt = e.GetPosition(_inkCanvas);
            HandlePointerMove(pt);
        }

        private void OnCanvasStylusUp(object sender, StylusEventArgs e)
        {
            if (_settings.PalmRejectionEnabled && !PalmRejectionManager.IsPenDevice(e.StylusDevice))
            {
                return;
            }

            Point pt = e.GetPosition(_inkCanvas);
            HandlePointerUp(pt);
        }

        // ======================= 双指轻触撤销手势 =======================
        //
        // 判定逻辑放在 TwoFingerTapDetector 里（独立成类，便于脱离 UI 直接测试），
        // 这里只负责把触摸事件转发过去。

        private void OnCanvasTouchDown(object sender, TouchEventArgs e)
        {
            if (!_settings.TwoFingerTapUndoEnabled || _twoFingerTap == null) return;
            _twoFingerTap.OnDown(e.TouchDevice.Id, e.GetTouchPoint(_inkCanvas).Position);
        }

        private void OnCanvasTouchMove(object sender, TouchEventArgs e)
        {
            if (!_settings.TwoFingerTapUndoEnabled || _twoFingerTap == null) return;
            _twoFingerTap.OnMove(e.TouchDevice.Id, e.GetTouchPoint(_inkCanvas).Position);
        }

        private void OnCanvasTouchUp(object sender, TouchEventArgs e)
        {
            if (!_settings.TwoFingerTapUndoEnabled || _twoFingerTap == null) return;
            _twoFingerTap.OnUp(e.TouchDevice.Id);
        }

        private void OnCanvasTouchLeave(object sender, TouchEventArgs e)
        {
            if (_twoFingerTap == null) return;
            _twoFingerTap.OnLeave(e.TouchDevice.Id);
        }

        // ======================= 截图标注并复制到剪贴板 =======================

        public bool CaptureScreenAndAnnotationsToClipboard()
        {
            try
            {
                int vx = (int)SystemParameters.VirtualScreenLeft;
                int vy = (int)SystemParameters.VirtualScreenTop;
                int vw = (int)SystemParameters.VirtualScreenWidth;
                int vh = (int)SystemParameters.VirtualScreenHeight;

                using (Bitmap screenBmp = new Bitmap(vw, vh, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(screenBmp))
                    {
                        g.CopyFromScreen(vx, vy, 0, 0, new System.Drawing.Size(vw, vh), CopyPixelOperation.SourceCopy);
                    }

                    // 临时设置完全透明背景以渲染纯墨迹
                    var oldCanvasBg = _inkCanvas.Background;
                    _inkCanvas.Background = Brushes.Transparent;

                    System.Windows.Media.Imaging.RenderTargetBitmap inkBmp = new System.Windows.Media.Imaging.RenderTargetBitmap(vw, vh, 96, 96, PixelFormats.Pbgra32);
                    try
                    {
                        inkBmp.Render(_inkCanvas);
                    }
                    finally
                    {
                        _inkCanvas.Background = oldCanvasBg;
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(inkBmp));
                        encoder.Save(ms);
                        ms.Position = 0;

                        using (Bitmap inkGdiBmp = new Bitmap(ms))
                        {
                            using (Graphics g = Graphics.FromImage(screenBmp))
                            {
                                g.DrawImage(inkGdiBmp, 0, 0, vw, vh);
                            }
                        }
                    }

                    System.Windows.Forms.Clipboard.SetImage(screenBmp);
                }

                ShowToast("✓ 已成功截图标注并复制到剪贴板！可直接粘贴到微信/文档中");
                return true;
            }
            catch (Exception ex)
            {
                ShowToast("截图失败: " + ex.Message);
                return false;
            }
        }

        private void OpenSettingsDialog()
        {
            CloseAllPopups();
            SettingsWindow win = new SettingsWindow(_settings);
            win.Owner = this;
            win.ShowDialog();
            UpdatePalmRejectionButton();
            ApplyDrawingAttributes();
        }
    }

    /// <summary>悬浮球菜单的一项。</summary>
    public class EscapeMenuItem
    {
        public string Text;
        public Action Invoke;

        public EscapeMenuItem(string text, Action invoke)
        {
            Text = text;
            Invoke = invoke;
        }
    }

    /// <summary>
    /// 穿透状态下的常驻悬浮球。
    ///
    /// 穿透模式会把主窗口设为 WS_EX_TRANSPARENT，此后 WM_NCHITTEST 根本不会被触发，
    /// 主窗口连同工具栏一起退出命中测试，用户将无法再通过界面退出穿透模式
    /// （真机实测已确认会被困住）。因此必须有这样一个独立的、永不穿透的小窗口，
    /// 作为始终可用的恢复入口。
    ///
    /// 它本身不承载具体动作，而是点击后弹出一份由外部按当前状态动态生成的菜单。
    /// </summary>
    public class EscapeHatchWindow : Window
    {
        private const double BallSize = 44.0;
        private const double MenuWidth = 152.0;
        private const double MenuGap = 8.0;

        private readonly Func<System.Collections.Generic.List<EscapeMenuItem>> _buildMenu;
        private readonly Canvas _canvas;
        private readonly Border _ball;
        private Border _menuBox;
        private double _ballLeft;
        private double _ballTop;
        private bool _menuOpen;

        public EscapeHatchWindow(Func<System.Collections.Generic.List<EscapeMenuItem>> buildMenu)
        {
            _buildMenu = buildMenu;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Manual;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = false;

            _canvas = new Canvas();
            _canvas.Background = null;
            Content = _canvas;

            _ball = BuildBall();
            _canvas.Children.Add(_ball);

            // 菜单直接画在本窗口内，不再用 Popup。
            // Popup 的 HorizontalOffset 在跨 DPI 缩放下不可靠：100% 缩放正常，
            // 200% 缩放下偏移被忽略，菜单整块偏出约 (菜单宽 - 球宽) 的距离。
            // 同一个窗口内部的 WPF 布局天然 DPI 无关，不存在这个问题。
            PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                HandlePress(e.GetPosition(_canvas));
            };
            PreviewTouchDown += delegate(object s, TouchEventArgs e)
            {
                HandlePress(e.GetTouchPoint(_canvas).Position);
            };

            Loaded += delegate
            {
                Rect work = SystemParameters.WorkArea;

                // 自动隐藏的任务栏不计入 WorkArea，滑出时会盖住球。
                // 这里按任务栏所在边额外让出一段空间，并留 8 的间隙。
                double reserveX = 0;
                double reserveY = 0;

                double scale = 1.0;
                PresentationSource ps = PresentationSource.FromVisual(this);
                if (ps != null && ps.CompositionTarget != null)
                    scale = ps.CompositionTarget.TransformToDevice.M11;

                uint edge;
                double thickness;
                if (NativeMethods.GetTaskbarInfo(scale, out edge, out thickness))
                {
                    // 只有任务栏确实"没被算进 WorkArea"时才需要额外让位；
                    // 若 WorkArea 已经排除了它，再让就会空出一大截。
                    bool bottomAlreadyReserved =
                        (SystemParameters.PrimaryScreenHeight - work.Bottom) > 8;
                    bool rightAlreadyReserved =
                        (SystemParameters.PrimaryScreenWidth - work.Right) > 8;

                    if (edge == NativeMethods.ABE_BOTTOM && !bottomAlreadyReserved)
                        reserveY = thickness + 8;
                    else if (edge == NativeMethods.ABE_RIGHT && !rightAlreadyReserved)
                        reserveX = thickness + 8;
                }

                _ballLeft = work.Right - BallSize - 24 - reserveX;
                _ballTop = work.Bottom - BallSize - 24 - reserveY;

                ApplyLayout(false, 0);
            };
        }

        private Border BuildBall()
        {
            Border shell = new Border();
            shell.Width = BallSize;
            shell.Height = BallSize;
            shell.CornerRadius = new CornerRadius(BallSize / 2);
            shell.Background = new SolidColorBrush(Color.FromArgb(240, 24, 26, 34));
            shell.BorderBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
            shell.BorderThickness = new Thickness(1.5);
            shell.Cursor = Cursors.Hand;
            shell.ToolTip = "点击打开菜单";

            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 18;
            shadow.ShadowDepth = 3;
            shadow.Opacity = 0.6;
            shadow.Color = Colors.Black;
            shell.Effect = shadow;

            System.Windows.Shapes.Path icon = new System.Windows.Shapes.Path();
            icon.Data = VectorIcons.Parse(VectorIcons.Pen);
            icon.Fill = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
            icon.Stretch = Stretch.Uniform;
            icon.Width = 18;
            icon.Height = 18;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            shell.Child = icon;

            return shell;
        }

        /// <summary>
        /// 重新排布窗口与内部元素。窗口只在菜单打开时扩张到容纳菜单，平时严格等于球的大小
        /// —— 透明区域越少，误挡下层点击的可能越小。
        /// 无论开合，球在屏幕上的位置都保持不变（窗口向左上扩张）。
        /// </summary>
        private void ApplyLayout(bool menuOpen, double menuH)
        {
            double w = BallSize;
            double h = BallSize;
            if (menuOpen)
            {
                w = BallSize + MenuGap + MenuWidth;
                h = BallSize + MenuGap + menuH;
            }

            Width = w;
            Height = h;
            Left = _ballLeft - (w - BallSize);
            Top = _ballTop - (h - BallSize);

            // 球始终贴在本窗口右下角
            Canvas.SetLeft(_ball, w - BallSize);
            Canvas.SetTop(_ball, h - BallSize);

            if (_menuBox != null)
            {
                Canvas.SetLeft(_menuBox, w - BallSize - MenuWidth);
                Canvas.SetTop(_menuBox, h - BallSize - MenuGap - menuH);
            }
        }

        private void HandlePress(Point p)
        {
            if (IsInside(_ball, p))
            {
                ToggleMenu();
                return;
            }

            // 落在菜单里就交给菜单行自己处理
            if (_menuOpen && IsInside(_menuBox, p)) return;
            if (_menuOpen) HideMenu();
        }

        private static bool IsInside(FrameworkElement el, Point p)
        {
            if (el == null) return false;

            double l = Canvas.GetLeft(el);
            double t = Canvas.GetTop(el);
            if (double.IsNaN(l)) l = 0;
            if (double.IsNaN(t)) t = 0;

            double w = el.ActualWidth > 0 ? el.ActualWidth : el.Width;
            double h = el.ActualHeight > 0 ? el.ActualHeight : el.Height;
            if (double.IsNaN(w) || double.IsNaN(h)) return false;

            return p.X >= l && p.X <= l + w && p.Y >= t && p.Y <= t + h;
        }

        private void ToggleMenu()
        {
            if (_menuOpen) { HideMenu(); return; }
            ShowMenu();
        }

        private void HideMenu()
        {
            _menuOpen = false;

            if (_menuBox != null)
            {
                _canvas.Children.Remove(_menuBox);
                _menuBox = null;
            }

            ApplyLayout(false, 0);
        }

        private void ShowMenu()
        {
            System.Collections.Generic.List<EscapeMenuItem> items =
                _buildMenu == null ? null : _buildMenu();
            if (items == null || items.Count == 0) return;

            if (_menuBox != null)
            {
                _canvas.Children.Remove(_menuBox);
                _menuBox = null;
            }

            StackPanel stack = new StackPanel { Orientation = Orientation.Vertical };

            foreach (EscapeMenuItem it in items)
            {
                Border row = new Border
                {
                    Padding = new Thickness(14, 9, 14, 9),
                    CornerRadius = new CornerRadius(8),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand
                };

                TextBlock txt = new TextBlock
                {
                    Text = it.Text,
                    Foreground = Brushes.White,
                    FontSize = 12.5,
                    FontWeight = FontWeights.Medium,
                    TextWrapping = TextWrapping.NoWrap
                };
                row.Child = txt;

                EscapeMenuItem captured = it;
                Action fire = delegate
                {
                    HideMenu();
                    if (captured.Invoke != null) captured.Invoke();
                };

                row.MouseLeftButtonUp += delegate { fire(); };
                row.PreviewTouchUp += delegate { fire(); };
                row.MouseEnter += delegate
                {
                    row.Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
                };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };

                stack.Children.Add(row);
            }

            Border box = new Border
            {
                Width = MenuWidth,
                Background = new SolidColorBrush(Color.FromArgb(245, 28, 30, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6)
            };

            DropShadowEffect boxShadow = new DropShadowEffect();
            boxShadow.BlurRadius = 16;
            boxShadow.ShadowDepth = 3;
            boxShadow.Opacity = 0.6;
            boxShadow.Color = Colors.Black;
            box.Effect = boxShadow;
            box.Child = stack;

            box.Measure(new System.Windows.Size(MenuWidth, double.PositiveInfinity));
            double menuH = box.DesiredSize.Height;

            _menuBox = box;
            _canvas.Children.Add(_menuBox);

            _menuOpen = true;
            ApplyLayout(true, menuH);
        }
    }
}
