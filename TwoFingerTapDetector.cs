using System;
using System.Collections.Generic;
using System.Windows;

namespace LingGuangInk
{
    /// <summary>
    /// 双指轻触检测。
    ///
    /// 判定为"轻触"需同时满足三条：恰好两指接触、两指都没有明显移动、整段接触时间很短。
    ///
    /// 时长限制是必需的 —— 手笔分离允许手掌整片贴在屏幕上，若只看"两指同时接触"，
    /// 用户抬手时就会被误判成轻敲并触发撤销。
    ///
    /// 之所以独立成一个不依赖 UI 的类：这段判定逻辑埋在触摸事件里，靠手测很难覆盖
    /// "三指"、"移动超限"、"超时"、"手势被打断"这些分支，抽出来就能直接驱动测试。
    /// </summary>
    public class TwoFingerTapDetector
    {
        private readonly double _moveTolerance;
        private readonly double _maxDurationMs;
        private readonly Func<DateTime> _clock;
        private readonly Dictionary<int, Point> _points = new Dictionary<int, Point>();

        private bool _candidate;
        private DateTime _startTime;

        /// <summary>满足全部条件时触发。</summary>
        public event Action Tapped;

        public TwoFingerTapDetector(double moveTolerance, double maxDurationMs)
            : this(moveTolerance, maxDurationMs, null)
        {
        }

        /// <param name="clock">时间源，默认取系统时间；测试可注入假时钟。</param>
        public TwoFingerTapDetector(double moveTolerance, double maxDurationMs, Func<DateTime> clock)
        {
            _moveTolerance = moveTolerance;
            _maxDurationMs = maxDurationMs;
            _clock = clock != null ? clock : (Func<DateTime>)(() => DateTime.Now);
        }

        public int TouchCount { get { return _points.Count; } }

        public void OnDown(int id, Point position)
        {
            _points[id] = position;

            if (_points.Count == 2)
            {
                _candidate = true;
                _startTime = _clock();
            }
            else if (_points.Count > 2)
            {
                // 三指及以上一律不算轻敲
                _candidate = false;
            }
        }

        public void OnMove(int id, Point position)
        {
            if (!_candidate) return;

            Point start;
            if (!_points.TryGetValue(id, out start)) return;

            if (Math.Abs(position.X - start.X) > _moveTolerance ||
                Math.Abs(position.Y - start.Y) > _moveTolerance)
            {
                // 有明显位移，说明是拖动 / 滑动而非轻敲
                _candidate = false;
            }
        }

        public void OnUp(int id)
        {
            bool candidate = _candidate && _points.Count == 2;
            bool withinTime = (_clock() - _startTime).TotalMilliseconds <= _maxDurationMs;

            _points.Remove(id);

            if (candidate && withinTime)
            {
                // 置回 false，避免第二根手指抬起时重复触发
                _candidate = false;

                if (Tapped != null) Tapped();
            }

            if (_points.Count == 0) _candidate = false;
        }

        /// <summary>触点意外离开（手势被打断）时清理状态，避免残留导致下次误判。</summary>
        public void OnLeave(int id)
        {
            _points.Remove(id);
            if (_points.Count == 0) _candidate = false;
        }

        public void Reset()
        {
            _points.Clear();
            _candidate = false;
        }
    }
}
