using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LingGuangInk
{
    public static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            else
                return GetWindowLongPtr32(hWnd, nIndex);
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        public static void SetClickThrough(Window window, bool clickThrough)
        {
            var helper = new WindowInteropHelper(window);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if (clickThrough)
            {
                style |= WS_EX_TRANSPARENT;
                style |= WS_EX_LAYERED;
            }
            else
            {
                style &= ~WS_EX_TRANSPARENT;
            }
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
        }

        // ===== Raw Input：在窗口已 WS_EX_TRANSPARENT 时仍能感知笔的存在 =====
        //
        // 为什么必须用原始输入：WS_EX_TRANSPARENT 会让窗口在命中测试中被整个跳过，
        // 因此收不到 WM_POINTERUPDATE 之类的悬停消息，形成"穿透了就感知不到笔"的死锁。
        // RIDEV_INPUTSINK 正是为此而生——它让窗口即使不是命中目标、不在前台，
        // 也照样收到 WM_INPUT，因为原始输入走设备级链路，完全绕过命中测试。

        public const int WM_INPUT = 0x00FF;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDEV_REMOVE = 0x00000001;
        public const ushort HID_USAGE_PAGE_DIGITIZER = 0x0D;
        public const ushort HID_USAGE_PEN = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(
            RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        /// <summary>
        /// 只注册 Digitizer/Pen 一种用途，并带上 RIDEV_INPUTSINK。
        /// 这样收到的任何 WM_INPUT 都必定来自笔设备，于是"有没有 WM_INPUT"
        /// 本身就可以直接当作"笔是否在感应范围内"的信号，无需解析 HID 报文。
        /// </summary>
        public static bool RegisterPenRawInput(IntPtr hwnd, bool enable)
        {
            RAWINPUTDEVICE[] devices = new RAWINPUTDEVICE[1];
            devices[0].usUsagePage = HID_USAGE_PAGE_DIGITIZER;
            devices[0].usUsage = HID_USAGE_PEN;
            devices[0].dwFlags = enable ? RIDEV_INPUTSINK : RIDEV_REMOVE;
            devices[0].hwndTarget = enable ? hwnd : IntPtr.Zero;

            return RegisterRawInputDevices(
                devices, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        // ===== 任务栏位置 =====
        //
        // 自动隐藏的任务栏**不计入** SystemParameters.WorkArea，此时 work.Bottom 等于整屏高度，
        // 悬浮球会正好落进任务栏滑出的区域、被盖住。要避开就必须单独查任务栏的真实位置。

        public const uint ABM_GETTASKBARPOS = 0x00000005;
        public const uint ABE_LEFT = 0;
        public const uint ABE_TOP = 1;
        public const uint ABE_RIGHT = 2;
        public const uint ABE_BOTTOM = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        /// <summary>
        /// 返回任务栏贴在哪条边，以及其厚度（DIP）。
        /// 自动隐藏时任务栏矩形可能只剩一条极窄的边，因此厚度做了下限保护。
        /// </summary>
        public static bool GetTaskbarInfo(double dpiScale, out uint edge, out double thicknessDip)
        {
            edge = ABE_BOTTOM;
            thicknessDip = 0;

            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA));

            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) == 0) return false;
            if (dpiScale <= 0) dpiScale = 1.0;

            edge = abd.uEdge;

            double thickPx = (edge == ABE_LEFT || edge == ABE_RIGHT)
                ? (abd.rc.right - abd.rc.left)
                : (abd.rc.bottom - abd.rc.top);

            thicknessDip = thickPx / dpiScale;

            // 自动隐藏时矩形可能只剩 2px 左右，用常见任务栏厚度兜底
            if (thicknessDip < 40.0) thicknessDip = 48.0;

            return true;
        }

        [DllImport("shcore.dll")]
        public static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public static void EnableHighDpi()
        {
            try
            {
                // PROCESS_PER_MONITOR_DPI_AWARE = 2
                SetProcessDpiAwareness(2);
            }
            catch
            {
                try
                {
                    SetProcessDPIAware();
                }
                catch { }
            }
        }

        // ======================= 硬件输入源识别 (触控笔 vs 手指触摸) =======================

        public enum INPUT_MESSAGE_DEVICE_TYPE
        {
            IMDT_UNAVAILABLE = 0x00000000,
            IMDT_KEYBOARD    = 0x00000001,
            IMDT_MOUSE       = 0x00000002,
            IMDT_TOUCH       = 0x00000004,
            IMDT_PEN         = 0x00000008,
            IMDT_TOUCHPAD    = 0x00000010
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT_MESSAGE_SOURCE
        {
            public INPUT_MESSAGE_DEVICE_TYPE deviceType;
            public int originId;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCurrentInputMessageSource(out INPUT_MESSAGE_SOURCE inputMessageSource);

        [DllImport("user32.dll")]
        public static extern IntPtr GetMessageExtraInfo();

        public const uint MI_WP_SIGNATURE = 0xFF515700;
        public const uint SIGNATURE_MASK = 0xFFFFFF00;

        public const int WM_POINTERDOWN = 0x0246;
        public const int WM_POINTERUPDATE = 0x0245;
        public const int WM_POINTERUP = 0x0247;
        public const int WM_TOUCH = 0x0240;
        public const uint PT_TOUCH = 0x00000002;
        public const uint GW_HWNDNEXT = 2;
        public const uint TOUCHEVENTF_MOVE = 0x0001;
        public const uint TOUCHEVENTF_DOWN = 0x0002;
        public const uint TOUCHEVENTF_UP = 0x0004;
        public const uint WM_MOUSEMOVE = 0x0200;
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint MK_LBUTTON = 0x0001;
        public const uint TWF_WANTPALM = 0x00000002;
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOUCHINPUT
        {
            public int x;
            public int y;
            public IntPtr hSource;
            public uint dwID;
            public uint dwFlags;
            public uint dwMask;
            public uint dwTime;
            public IntPtr dwExtraInfo;
            public uint cxContact;
            public uint cyContact;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll")]
        public static extern bool GetPointerType(uint pointerId, out uint pointerType);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr RealChildWindowFromPoint(IntPtr hWnd, POINT point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        /// <summary>
        /// 主窗口标题，用于 FindWindow 定位已在运行的实例。
        /// 与 MainWindow 构造函数中设置的 Title 必须保持一致。
        /// </summary>
        public const string MainWindowTitle = "灵光画笔";

        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        /// <summary>
        /// 单实例互斥体被占用时，用这条自定义消息通知已在运行的实例切换模式。
        /// 配合把 Surface Pen 顶部按钮绑定到本程序，即可实现"按笔切换涂鸦/穿透"——
        /// 笔按钮是蓝牙硬件级事件，不经过命中测试，不受窗口样式影响，
        /// 因此成为 WS_EX_TRANSPARENT 全穿透之后唯一可靠的唤醒入口。
        /// </summary>
        public const string ToggleMessageName = "LingGuangInk_TogglePassthrough_v1";

        [DllImport("user32.dll")]
        public static extern bool RegisterTouchWindow(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        public static extern bool UnregisterTouchWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetTouchInputInfo(IntPtr hTouchInput, uint cInputs, [In, Out] TOUCHINPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern bool CloseTouchInputHandle(IntPtr hTouchInput);

        public enum CurrentInputKind
        {
            Unknown,
            Mouse,
            Touch,
            Pen
        }

        /// <summary>
        /// 命中测试阶段的输入源诊断。
        ///
        /// 注意性能：WM_NCHITTEST 是逐点触发的热路径，而 Pen / Unknown 两种判定会交替
        /// 出现，使"只压连续重复"的去重完全失效 —— 几乎每个事件都会真的写盘。
        /// File.AppendAllText 是同步的 open+write+close，跑在消息循环里会直接拖慢笔迹，
        /// 因此这里额外做了限流与体积上限。
        /// </summary>
        public static class InputDiagnostics
        {
            private static readonly object _lock = new object();
            private static string _lastLine = null;
            private static DateTime _lastHitTestLog = DateTime.MinValue;
            private static int _writesSinceTrimCheck = 0;

            public static volatile bool Enabled = true;

            /// <summary>命中测试行的最小记录间隔（毫秒），避免逐点写盘。</summary>
            public static int HitTestThrottleMs = 400;

            private const long MaxLogBytes = 512 * 1024;
            private const int TrimCheckEvery = 200;

            public static string LogPath;

            public static void Log(string line)
            {
                if (!Enabled || LogPath == null || line == null) return;

                lock (_lock)
                {
                    if (line == _lastLine) return;

                    if (line.StartsWith("NCHITTEST", StringComparison.Ordinal))
                    {
                        DateTime now = DateTime.Now;
                        if ((now - _lastHitTestLog).TotalMilliseconds < HitTestThrottleMs) return;
                        _lastHitTestLog = now;
                    }

                    _lastLine = line;

                    try
                    {
                        System.IO.File.AppendAllText(LogPath,
                            DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + Environment.NewLine);

                        if (++_writesSinceTrimCheck >= TrimCheckEvery)
                        {
                            _writesSinceTrimCheck = 0;
                            TrimIfTooLarge();
                        }
                    }
                    catch { }
                }
            }

            /// <summary>日志超过上限就清空，避免长期使用把磁盘写满。</summary>
            private static void TrimIfTooLarge()
            {
                try
                {
                    System.IO.FileInfo fi = new System.IO.FileInfo(LogPath);
                    if (fi.Exists && fi.Length > MaxLogBytes)
                    {
                        System.IO.File.WriteAllText(LogPath, string.Empty);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Read the source once while processing the current native input message.
        /// Returning Unknown is intentional: callers can choose their own fallback.
        /// </summary>
        public static CurrentInputKind GetCurrentInputKind()
        {
            try
            {
                INPUT_MESSAGE_SOURCE ims;
                if (GetCurrentInputMessageSource(out ims))
                {
                    uint type = (uint)ims.deviceType;
                    if ((type & (uint)INPUT_MESSAGE_DEVICE_TYPE.IMDT_PEN) != 0)
                        return CurrentInputKind.Pen;
                    if ((type & (uint)INPUT_MESSAGE_DEVICE_TYPE.IMDT_TOUCH) != 0 ||
                        (type & (uint)INPUT_MESSAGE_DEVICE_TYPE.IMDT_TOUCHPAD) != 0)
                        return CurrentInputKind.Touch;
                    if ((type & (uint)INPUT_MESSAGE_DEVICE_TYPE.IMDT_MOUSE) != 0)
                        return CurrentInputKind.Mouse;
                }

                // Windows 约定：0xFF515700 = 触摸提升出的鼠标消息；
                // 0xFF515780（即 0x80 位置位）= 触控笔提升出的鼠标消息。
                IntPtr extra = GetMessageExtraInfo();
                long val = extra.ToInt64() & 0xFFFFFFFFL;
                if ((val & SIGNATURE_MASK) == MI_WP_SIGNATURE)
                    return (val & 0x80) != 0 ? CurrentInputKind.Pen : CurrentInputKind.Touch;
            }
            catch
            {
                // Native input metadata is optional on older or unusual drivers.
            }

            return CurrentInputKind.Unknown;
        }

        public static bool IsCurrentInputTouch()
        {
            return GetCurrentInputKind() == CurrentInputKind.Touch;
        }

        public static bool IsCurrentInputPen()
        {
            return GetCurrentInputKind() == CurrentInputKind.Pen;
        }
    }
}
