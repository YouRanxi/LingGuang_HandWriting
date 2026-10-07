using System;
using System.Threading;
using System.Windows;

namespace LingGuangInk
{
    public class Program
    {
        private static Mutex _appMutex;

        [STAThread]
        public static void Main()
        {
            try
            {
                // 单实例互斥保证 (全新命名，避免旧僵尸进程干扰)
                bool createdNew;
                _appMutex = new Mutex(true, "LingGuangInk_App_SingleWindow_v6_Final", out createdNew);
                if (!createdNew)
                {
                    // 已有实例在运行：广播自定义消息，让它切换"涂鸦 / 穿透"模式。
                    // 把 Surface Pen 顶部按钮绑定到本程序后，按一下笔即可切换——
                    // 这是 WS_EX_TRANSPARENT 全穿透状态下唯一可靠的唤醒入口，
                    // 因为笔按钮是蓝牙硬件级事件，不经过命中测试、不受窗口样式影响。
                    try
                    {
                        uint toggleMsg = NativeMethods.RegisterWindowMessage(NativeMethods.ToggleMessageName);
                        if (toggleMsg != 0)
                        {
                            // 用 FindWindow 定向投递。实测 PostMessage(HWND_BROADCAST, ...)
                            // 返回 True 但 WPF 顶层窗口收不到，定向投递确定可达。
                            IntPtr target = NativeMethods.FindWindow(null, NativeMethods.MainWindowTitle);
                            if (target != IntPtr.Zero)
                            {
                                NativeMethods.PostMessage(target, toggleMsg, IntPtr.Zero, IntPtr.Zero);
                            }
                        }
                    }
                    catch
                    {
                        // 通知失败不应阻塞退出。
                    }
                    return;
                }

                // Surface Pro 高分屏高 DPI 精准点位适配
                NativeMethods.EnableHighDpi();

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                app.DispatcherUnhandledException += (s, e) =>
                {
                    MessageBox.Show("运行异常: " + e.Exception.Message + "\n" + e.Exception.StackTrace,
                        "灵光画笔错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };

                // 从 %LOCALAPPDATA%\LingGuangInk\settings.ini 读取用户配置，
                // 文件缺失或损坏时自动回退默认值。
                AppSettings settings = AppSettings.Load();

                MainWindow mainWindow = new MainWindow(settings);
                mainWindow.Show();
                mainWindow.Activate();

                app.Exit += (s, e) =>
                {
                    if (_appMutex != null)
                    {
                        try { _appMutex.ReleaseMutex(); } catch { }
                        _appMutex.Close();
                    }
                };

                app.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败: " + ex.Message + "\n\n" + ex.StackTrace,
                    "灵光画笔启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
