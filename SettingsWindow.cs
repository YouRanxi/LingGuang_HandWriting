using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LingGuangInk
{
    /// <summary>
    /// 设置与触控指南窗口。
    ///
    /// 全部控件（含 CheckBox / ScrollBar）都使用自绘模板，保持与工具栏、弹出菜单
    /// 同一套深色视觉；若沿用系统原生模板，深色底上会突兀地出现浅色复选框与滚动条。
    /// </summary>
    public class SettingsWindow : Window
    {
        private static readonly Color Accent = Color.FromRgb(24, 144, 255);

        private readonly AppSettings _settings;

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;

            Title = "灵光画笔 - 悬浮窗与硬件适配指南";
            Width = 600;
            Height = 660;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;

            BuildUI();
        }

        private void BuildUI()
        {
            Border mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(252, 26, 28, 36)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(22, 18, 22, 18),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 34,
                    ShadowDepth = 8,
                    Color = Colors.Black,
                    Opacity = 0.7
                }
            };

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            layout.Children.Add(BuildHeader());
            Grid.SetRow(layout.Children[0], 0);

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 6, 0)
            };
            // 深色滚动条，避免系统浅色滚动条在深色卡片上割裂
            scroll.Resources.Add(typeof(ScrollBar), CreateDarkScrollBarStyle());

            StackPanel contentStack = new StackPanel();
            BuildGuideSection(contentStack);
            BuildPenSection(contentStack);
            BuildTogglesSection(contentStack);
            scroll.Content = contentStack;

            Grid.SetRow(scroll, 1);
            layout.Children.Add(scroll);

            UIElement footer = BuildFooter();
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);

            mainBorder.Child = layout;
            Content = mainBorder;

            MouseLeftButtonDown += OnWindowMouseDown;
        }

        // ======================= 各区块 =======================

        private UIElement BuildHeader()
        {
            Grid header = new Grid { Margin = new Thickness(2, 0, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            titleStack.Children.Add(new TextBlock
            {
                Text = "灵光画笔 · 悬浮窗与触控指南",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });

            titleStack.Children.Add(new TextBlock
            {
                Text = "Surface Pen 手笔分离、悬浮窗操作与防误触设置",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            Grid.SetColumn(titleStack, 0);
            header.Children.Add(titleStack);

            Button btnClose = new Button
            {
                Width = 30,
                Height = 30,
                Cursor = Cursors.Hand,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "关闭"
            };
            btnClose.Template = CreateCloseButtonTemplate();
            btnClose.Click += delegate { Close(); };
            Grid.SetColumn(btnClose, 1);
            header.Children.Add(btnClose);

            return header;
        }

        private void BuildGuideSection(StackPanel host)
        {
            host.Children.Add(CreateSectionTitle("纯悬浮窗便捷操作"));

            StackPanel stack = new StackPanel();
            stack.Children.Add(CreateBulletText("画板 / 穿透桌面切换", "点击工具栏穿透按钮，笔迹保留在屏幕上，可直接点击下层网页与软件；再次点击立刻恢复画笔。"));
            stack.Children.Add(CreateBulletText("自动手笔分离", "开启后笔靠近屏幕即自动可写、笔离开即自动穿透给手指，全程无需按键。"));
            stack.Children.Add(CreateBulletText("收起为迷你悬浮小球", "点击折叠按钮，工具栏收缩为小巧微球，可任意拖动停靠在屏幕边缘，点击即刻弹回展开。"));
            stack.Children.Add(CreateBulletText("双指轻敲屏幕撤销", "无需键盘！任意两指同时轻点屏幕，即可撤销上一步笔迹。"));
            stack.Children.Add(CreateBulletText("一键截图标注并复制", "点击相机图标，即刻将当前屏幕与涂鸦合并复制到剪贴板，直接粘贴到聊天或文档中。"));

            host.Children.Add(CreateCard(stack));
        }

        private void BuildPenSection(StackPanel host)
        {
            host.Children.Add(CreateSectionTitle("Surface Pen 1776 硬件特性"));

            StackPanel stack = new StackPanel();
            stack.Children.Add(CreateBulletText("4096 级高精度压感", "原生接入 Windows Ink 硬件引擎，轻重笔触极为自然细腻。"));
            stack.Children.Add(CreateBulletText("尾端橡皮擦自动感应", "翻转笔尾接触屏幕，自动无缝触发擦除；转回笔尖自动恢复画笔。"));
            stack.Children.Add(CreateBulletText("笔身侧键 (Barrel Button)", "按住笔身侧键接触屏幕可快速擦除，松开立即还原。"));

            host.Children.Add(CreateCard(stack));
        }

        private void BuildTogglesSection(StackPanel host)
        {
            host.Children.Add(CreateSectionTitle("手笔分离与触控防误触设置"));

            StackPanel stack = new StackPanel();

            stack.Children.Add(CreateCheckBox("开启严格手笔分离（防手掌误触）",
                _settings.PalmRejectionEnabled, delegate(bool v) { _settings.PalmRejectionEnabled = v; }));
            stack.Children.Add(CreateTipText("开启后完全过滤手掌与手指接触画板的信号，手掌可全贴屏幕安心书写；仅允许触控笔绘画。"));

            stack.Children.Add(CreateCheckBox("启用双指轻触撤销手势（Two-Finger Tap Undo）",
                _settings.TwoFingerTapUndoEnabled, delegate(bool v) { _settings.TwoFingerTapUndoEnabled = v; }));
            stack.Children.Add(CreateTipText("双指同时轻点屏幕任意位置即可撤销上一步笔迹，如 iPad / Procreate 般顺手自然。"));

            stack.Children.Add(CreateCheckBox("启用 Surface Pen 侧键长按快速擦除",
                _settings.BarrelButtonHoldToErase, delegate(bool v) { _settings.BarrelButtonHoldToErase = v; }));

            stack.Children.Add(CreateCheckBox("允许鼠标绘画（未握笔时支持鼠标随时涂鸦）",
                _settings.AllowMouseDrawing, delegate(bool v) { _settings.AllowMouseDrawing = v; }));

            host.Children.Add(CreateCard(stack));
        }

        private UIElement BuildFooter()
        {
            StackPanel footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            footer.Children.Add(new TextBlock
            {
                Text = "设置即时生效，无需重启",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            });

            Button btnSave = new Button
            {
                Content = "完成",
                Width = 96,
                Height = 34,
                FontSize = 13,
                Foreground = Brushes.White,
                Cursor = Cursors.Hand,
                Focusable = false
            };
            btnSave.Template = CreatePrimaryButtonTemplate();
            btnSave.Click += delegate { Close(); };
            footer.Children.Add(btnSave);

            return footer;
        }

        // ======================= 模板 =======================

        /// <summary>深色圆角复选框：选中时填充强调色并显示对勾。</summary>
        private static ControlTemplate CreateCheckBoxTemplate()
        {
            ControlTemplate t = new ControlTemplate(typeof(CheckBox));

            FrameworkElementFactory root = new FrameworkElementFactory(typeof(StackPanel));
            root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            FrameworkElementFactory box = new FrameworkElementFactory(typeof(Border));
            box.Name = "box";
            box.SetValue(FrameworkElement.WidthProperty, 18.0);
            box.SetValue(FrameworkElement.HeightProperty, 18.0);
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            box.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)));
            box.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory tick = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            tick.Name = "tick";
            tick.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M4,9.4 L7.2,12.6 L14,5.4"));
            tick.SetValue(System.Windows.Shapes.Path.StrokeProperty, Brushes.White);
            tick.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 2.2);
            tick.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
            tick.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
            tick.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            tick.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tick.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(tick);

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));

            root.AppendChild(box);
            root.AppendChild(cp);
            t.VisualTree = root;

            Trigger hover = new Trigger { Property = CheckBox.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Accent), "box"));
            t.Triggers.Add(hover);

            Trigger checkedOn = new Trigger { Property = CheckBox.IsCheckedProperty, Value = true };
            checkedOn.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Accent), "box"));
            checkedOn.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Accent), "box"));
            checkedOn.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "tick"));
            t.Triggers.Add(checkedOn);

            return t;
        }

        private static ControlTemplate CreatePrimaryButtonTemplate()
        {
            ControlTemplate t = new ControlTemplate(typeof(Button));

            FrameworkElementFactory b = new FrameworkElementFactory(typeof(Border));
            b.Name = "bg";
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(17));
            b.SetValue(Border.BackgroundProperty, new SolidColorBrush(Accent));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            b.AppendChild(cp);
            t.VisualTree = b;

            Trigger hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(64, 169, 255)), "bg"));
            t.Triggers.Add(hover);

            Trigger pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(9, 109, 217)), "bg"));
            t.Triggers.Add(pressed);

            return t;
        }

        private static ControlTemplate CreateCloseButtonTemplate()
        {
            ControlTemplate t = new ControlTemplate(typeof(Button));

            FrameworkElementFactory b = new FrameworkElementFactory(typeof(Border));
            b.Name = "bg";
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
            b.SetValue(Border.BackgroundProperty, Brushes.Transparent);

            FrameworkElementFactory icon = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            icon.SetValue(System.Windows.Shapes.Path.DataProperty, VectorIcons.Parse(VectorIcons.Close));
            icon.SetValue(System.Windows.Shapes.Path.FillProperty, new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)));
            icon.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);
            icon.SetValue(FrameworkElement.WidthProperty, 12.0);
            icon.SetValue(FrameworkElement.HeightProperty, 12.0);
            icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            b.AppendChild(icon);
            t.VisualTree = b;

            Trigger hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(60, 255, 90, 90)), "bg"));
            t.Triggers.Add(hover);

            return t;
        }

        /// <summary>
        /// 深色滚动条。系统原生滚动条是浅色的，在深色卡片上非常割裂。
        /// 与主窗口滑块同理：Track 的依赖属性为 internal，只能用 XAML 解析。
        /// </summary>
        private static Style CreateDarkScrollBarStyle()
        {
            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='ScrollBar'>
  <Setter Property='Width' Value='8'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' IsDirectionReversed='true'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border CornerRadius='4' Background='#55FFFFFF'/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        // ======================= 小部件 =======================

        private TextBlock CreateSectionTitle(string title)
        {
            return new TextBlock
            {
                Text = title,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Accent),
                Margin = new Thickness(2, 12, 0, 8)
            };
        }

        private Border CreateCard(UIElement child)
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 34, 36, 46)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 4)
            };
            card.Child = child;
            return card;
        }

        private Grid CreateBulletText(string boldTitle, string desc)
        {
            Grid g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top
            };
            titleRow.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Accent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = boldTitle,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Medium,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            });

            TextBlock t2 = new TextBlock
            {
                Text = desc,
                Foreground = new SolidColorBrush(Color.FromArgb(168, 255, 255, 255)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            };

            Grid.SetColumn(titleRow, 0);
            Grid.SetColumn(t2, 1);
            g.Children.Add(titleRow);
            g.Children.Add(t2);
            return g;
        }

        private TextBlock CreateTipText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromArgb(132, 255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(28, 0, 0, 10)
            };
        }

        private CheckBox CreateCheckBox(string label, bool initialVal, Action<bool> onChange)
        {
            CheckBox cb = new CheckBox
            {
                Content = label,
                IsChecked = initialVal,
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            cb.Template = CreateCheckBoxTemplate();
            cb.Checked += delegate { onChange(true); };
            cb.Unchecked += delegate { onChange(false); };
            return cb;
        }

        /// <summary>
        /// 空白处拖动窗口。必须排除交互控件：否则在复选框上按下会被 DragMove 抢走，
        /// 开关可能点不动。
        /// </summary>
        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (IsInteractive(e.OriginalSource as DependencyObject)) return;

            try { DragMove(); }
            catch { /* 拖动被系统取消时忽略 */ }
        }

        private static bool IsInteractive(DependencyObject d)
        {
            while (d != null)
            {
                if (d is ButtonBase || d is TextBoxBase || d is Slider) return true;

                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // 这里的开关都是即时生效的，关闭时统一落盘到 settings.ini
            _settings.Save();
        }
    }
}
