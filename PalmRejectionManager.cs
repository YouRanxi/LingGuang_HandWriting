using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;

namespace LingGuangInk
{
    /// <summary>
    /// 专用实时笔触渲染器：在 RealTimeStylus 硬件渲染线程上直接拦截手掌/手指信号，
    /// 开启手笔分离时，手指与手掌完全无法在屏幕上绘制任何墨迹。
    /// </summary>
    public class PenOnlyDynamicRenderer : DynamicRenderer
    {
        private readonly AppSettings _settings;

        public PenOnlyDynamicRenderer(AppSettings settings)
        {
            _settings = settings;
        }

        private bool IsTouchInput(RawStylusInput raw)
        {
            if (!_settings.PalmRejectionEnabled) return false;
            return PalmRejectionManager.IsTouchTabletDevice(raw.TabletDeviceId);
        }

        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            if (IsTouchInput(rawStylusInput)) return;
            base.OnStylusDown(rawStylusInput);
        }

        protected override void OnStylusMove(RawStylusInput rawStylusInput)
        {
            if (IsTouchInput(rawStylusInput)) return;
            base.OnStylusMove(rawStylusInput);
        }

        protected override void OnStylusUp(RawStylusInput rawStylusInput)
        {
            if (IsTouchInput(rawStylusInput)) return;
            base.OnStylusUp(rawStylusInput);
        }
    }

    /// <summary>
    /// 定制 InkCanvas 画板：内置 PenOnlyDynamicRenderer，实现硬件级手笔分离
    /// </summary>
    public class CustomInkCanvas : InkCanvas
    {
        public CustomInkCanvas(AppSettings settings)
        {
            PalmRejectionManager.RefreshTabletDeviceCache();
            DynamicRenderer = new PenOnlyDynamicRenderer(settings);
        }
    }

    /// <summary>
    /// 手笔分离与防手掌误触管理器
    /// </summary>
    public class PalmRejectionManager
    {
        private readonly AppSettings _settings;
        private sealed class TabletDeviceSnapshot
        {
            public static readonly TabletDeviceSnapshot Empty = new TabletDeviceSnapshot(
                new System.Collections.Generic.HashSet<int>(),
                new System.Collections.Generic.HashSet<int>());

            public readonly System.Collections.Generic.HashSet<int> TouchIds;
            public readonly System.Collections.Generic.HashSet<int> PenIds;

            public TabletDeviceSnapshot(
                System.Collections.Generic.HashSet<int> touchIds,
                System.Collections.Generic.HashSet<int> penIds)
            {
                TouchIds = touchIds;
                PenIds = penIds;
            }
        }

        // Replace the complete snapshot atomically; readers run on the RTS thread.
        private static volatile TabletDeviceSnapshot _tabletSnapshot = TabletDeviceSnapshot.Empty;

        public static void RefreshTabletDeviceCache()
        {
            try
            {
                var touchIds = new System.Collections.Generic.HashSet<int>();
                var penIds = new System.Collections.Generic.HashSet<int>();

                foreach (TabletDevice td in Tablet.TabletDevices)
                {
                    if (td.Type == TabletDeviceType.Touch)
                        touchIds.Add(td.Id);
                    else if (td.Type == TabletDeviceType.Stylus)
                        penIds.Add(td.Id);
                }

                _tabletSnapshot = new TabletDeviceSnapshot(touchIds, penIds);
            }
            catch
            {
                // Keep the last valid snapshot when WPF device enumeration fails.
            }
        }

        public static bool IsTouchTabletDevice(int tabletDeviceId)
        {
            TabletDeviceSnapshot snapshot = _tabletSnapshot;
            if (snapshot.TouchIds.Contains(tabletDeviceId)) return true;
            if (snapshot.PenIds.Contains(tabletDeviceId)) return false;

            // Unknown IDs are not enough evidence to discard a real pen.
            return false;
        }

        public PalmRejectionManager(AppSettings settings)
        {
            _settings = settings;
            RefreshTabletDeviceCache();
        }

        /// <summary>
        /// 判定当前设备是否为 Surface Pen 真实硬件触控笔
        /// </summary>
        public static bool IsPenDevice(StylusDevice device)
        {
            if (device == null) return false;

            // 1. Surface Pen 1776 笔尾橡皮擦反转状态 -> 必为触控笔
            if (device.Inverted) return true;

            if (device.TabletDevice != null)
            {
                // 2. 硬件明确声明为 Touch（电容触摸屏/手指/手掌） -> 100% 绝非触控笔！
                if (device.TabletDevice.Type == TabletDeviceType.Touch)
                    return false;

                // 3. 硬件明确声明为 Stylus（电磁/主动式 Surface Pen 触控笔） -> 100% 为笔！
                if (device.TabletDevice.Type == TabletDeviceType.Stylus)
                    return true;

                // 4. Check the lock-free snapshot if WPF reports an unknown device type.
                TabletDeviceSnapshot snapshot = _tabletSnapshot;
                if (snapshot.TouchIds.Contains(device.TabletDevice.Id))
                    return false;
                if (snapshot.PenIds.Contains(device.TabletDevice.Id))
                    return true;
            }

            return false;
        }

        public void Attach(InkCanvas target)
        {
            RefreshTabletDeviceCache();
            target.PreviewStylusDown += OnPreviewStylusDown;
            target.PreviewStylusMove += OnPreviewStylusMove;
            target.PreviewStylusUp += OnPreviewStylusUp;

            target.PreviewTouchDown += OnPreviewTouchDown;
            target.PreviewTouchMove += OnPreviewTouchMove;
            target.PreviewTouchUp += OnPreviewTouchUp;

            target.PreviewMouseDown += OnPreviewMouseDown;
            target.PreviewMouseMove += OnPreviewMouseMove;
            target.PreviewMouseUp += OnPreviewMouseUp;
        }

        private void OnPreviewStylusDown(object sender, StylusDownEventArgs e)
        {
            bool isPen = IsPenDevice(e.StylusDevice);

            // 若判定为 Surface Pen 触控笔，完全放行 Windows Ink 4096级高精度压感书写
            if (isPen)
            {
                return;
            }

            // 非触控笔（手指或手掌贴屏）：开启手笔分离时严格阻断，画板不接收信号
            if (_settings.PalmRejectionEnabled)
            {
                e.Handled = true;
            }
        }

        private void OnPreviewStylusMove(object sender, StylusEventArgs e)
        {
            if (IsPenDevice(e.StylusDevice))
            {
                return;
            }

            if (_settings.PalmRejectionEnabled)
            {
                e.Handled = true;
            }
        }

        private void OnPreviewStylusUp(object sender, StylusEventArgs e)
        {
            if (IsPenDevice(e.StylusDevice))
            {
                return;
            }

            if (_settings.PalmRejectionEnabled)
            {
                e.Handled = true;
            }
        }

        private void OnPreviewTouchDown(object sender, TouchEventArgs e)
        {
            // 手掌与手指接触：若开启手笔分离，直接阻断，防止在画板上产生杂点
            if (_settings.PalmRejectionEnabled)
            {
                e.Handled = true;
            }
        }

        private void OnPreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (_settings.PalmRejectionEnabled)
            {
                e.Handled = true;
            }
        }

        private void OnPreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (_settings.PalmRejectionEnabled)
            {
                e.Handled = true;
            }
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 阻断来自 Touch 模拟的鼠标绘制
            if (e.StylusDevice != null && !IsPenDevice(e.StylusDevice))
            {
                if (_settings.PalmRejectionEnabled)
                {
                    e.Handled = true;
                }
            }
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.StylusDevice != null && !IsPenDevice(e.StylusDevice))
            {
                if (_settings.PalmRejectionEnabled)
                {
                    e.Handled = true;
                }
            }
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null && !IsPenDevice(e.StylusDevice))
            {
                if (_settings.PalmRejectionEnabled)
                {
                    e.Handled = true;
                }
            }
        }

    }
}
