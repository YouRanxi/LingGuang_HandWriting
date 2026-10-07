using System;
using System.Globalization;
using System.Windows.Media;

namespace LingGuangInk
{
    public enum DrawingToolType
    {
        Pen,           // 压感钢笔 / 圆珠笔
        Highlighter,   // 半透明荧光笔
        Laser,         // 激光教鞭 (自动渐隐光轨)
        EraserStroke,  // 笔段橡皮擦
        EraserPoint,   // 像素点橡皮擦
        ShapeLine,     // 直线
        ShapeArrow,    // 箭头
        ShapeRect,     // 矩形
        ShapeEllipse   // 椭圆
    }

    public enum BackdropType
    {
        Transparent,   // 透明桌面 (默认涂鸦)
        Whiteboard,    // 纯白电子白板
        Blackboard     // 护眼磨砂黑板
    }

    public class AppSettings
    {
        public bool PalmRejectionEnabled { get; set; }
        public bool AllowMouseDrawing { get; set; }
        public bool TwoFingerTapUndoEnabled { get; set; }
        public bool BarrelButtonHoldToErase { get; set; }
        public double PenWidth { get; set; }
        public double HighlighterWidth { get; set; }
        public Color CurrentColor { get; set; }

        public AppSettings()
        {
            PalmRejectionEnabled = true;
            AllowMouseDrawing = true;
            TwoFingerTapUndoEnabled = true;
            BarrelButtonHoldToErase = true;
            PenWidth = 4.0;
            HighlighterWidth = 24.0;
            CurrentColor = Color.FromRgb(255, 77, 79); // 珊瑚红
        }

        // ======================= 持久化 =======================
        //
        // 用最朴素的 key=value 文本而不是 JSON：本项目由内置 csc.exe 直接编译，
        // 引序列化框架要额外加程序集引用；配置项就这么几个，纯文本更稳、也更好排查。
        // 任何读写异常都被吞掉并回退默认值 —— 配置问题绝不该阻塞启动。

        private const string FileName = "settings.ini";

        /// <summary>%LOCALAPPDATA%\LingGuangInk\settings.ini</summary>
        public static string StorePath
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LingGuangInk");
                return System.IO.Path.Combine(dir, FileName);
            }
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();

            try
            {
                string path = StorePath;
                if (!System.IO.File.Exists(path)) return s;

                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "PalmRejectionEnabled":
                            s.PalmRejectionEnabled = ParseBool(val, s.PalmRejectionEnabled);
                            break;
                        case "AllowMouseDrawing":
                            s.AllowMouseDrawing = ParseBool(val, s.AllowMouseDrawing);
                            break;
                        case "TwoFingerTapUndoEnabled":
                            s.TwoFingerTapUndoEnabled = ParseBool(val, s.TwoFingerTapUndoEnabled);
                            break;
                        case "BarrelButtonHoldToErase":
                            s.BarrelButtonHoldToErase = ParseBool(val, s.BarrelButtonHoldToErase);
                            break;
                        case "PenWidth":
                            s.PenWidth = ParseDouble(val, s.PenWidth);
                            break;
                        case "HighlighterWidth":
                            s.HighlighterWidth = ParseDouble(val, s.HighlighterWidth);
                            break;
                        case "CurrentColor":
                            Color c;
                            if (TryParseColor(val, out c)) s.CurrentColor = c;
                            break;
                    }
                }
            }
            catch
            {
                // 配置损坏不应阻塞启动，直接回退默认值
            }

            return s;
        }

        public void Save()
        {
            try
            {
                string path = StorePath;
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("# 灵光画笔配置（自动生成，可手工编辑）");
                sb.AppendLine("PalmRejectionEnabled=" + (PalmRejectionEnabled ? "1" : "0"));
                sb.AppendLine("AllowMouseDrawing=" + (AllowMouseDrawing ? "1" : "0"));
                sb.AppendLine("TwoFingerTapUndoEnabled=" + (TwoFingerTapUndoEnabled ? "1" : "0"));
                sb.AppendLine("BarrelButtonHoldToErase=" + (BarrelButtonHoldToErase ? "1" : "0"));
                sb.AppendLine("PenWidth=" + PenWidth.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("HighlighterWidth=" + HighlighterWidth.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("CurrentColor=" + ToHex(CurrentColor));

                System.IO.File.WriteAllText(path, sb.ToString());
            }
            catch
            {
                // 写入失败不影响本次会话
            }
        }

        private static bool ParseBool(string v, bool fallback)
        {
            if (v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (v == "0" || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static double ParseDouble(string v, double fallback)
        {
            double d;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0.0)
                return d;

            return fallback;
        }

        private static string ToHex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        /// <summary>接受 #RGB 与 #RRGGBB（# 可省略）。</summary>
        private static bool TryParseColor(string text, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrEmpty(text)) return false;

            string s = text.Trim().TrimStart('#');
            if (s.Length == 3)
                s = new string(new char[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            if (s.Length != 6) return false;

            int v;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                return false;

            color = Color.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
            return true;
        }
    }
}
