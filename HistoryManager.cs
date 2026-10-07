using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Threading;

namespace LingGuangInk
{
    /// <summary>
    /// 笔迹撤销 / 重做。
    ///
    /// 核心设计：**按手势合并**。
    ///
    /// 为什么需要合并：WPF 的 InkCanvas 做像素擦除时是边拖边改笔迹的 —— 一条笔迹
    /// 被一点点切开，每次切开都会先 Remove 原笔迹、再 Add 碎片，也就是**两次独立的
    /// 集合操作、两次 StrokesChanged**。若逐次入栈，一次擦除拖动会产生几十个撤销项，
    /// 用户得按到手酸才能退回擦除前的样子。
    ///
    /// 为什么不能按"是不是纯新增"来区分落笔与擦除：像素擦除里那个 Add 碎片的事件
    /// 本身就是"纯新增"，会被误判成新落笔。所以这里改看**输入手势边界**：
    /// 每次按下（落笔 / 起擦）先落定上一组，于是"一笔 = 一步"，
    /// 而一次擦除拖动中的所有变化都留在同一组里。
    /// </summary>
    public class HistoryManager
    {
        private class HistoryItem
        {
            public StrokeCollection AddedStrokes = new StrokeCollection();
            public StrokeCollection RemovedStrokes = new StrokeCollection();
        }

        /// <summary>一组变化静默多久后落定。用于兜住手势结束之后才抛出的尾事件。</summary>
        private const int MergeWindowMs = 400;

        /// <summary>撤销步数上限，防止长时间批注无限累积。</summary>
        private const int MaxUndoSteps = 100;

        private readonly InkCanvas _inkCanvas;
        private readonly Stack<HistoryItem> _undoStack = new Stack<HistoryItem>();
        private readonly Stack<HistoryItem> _redoStack = new Stack<HistoryItem>();
        private bool _isPerformingHistoryAction = false;

        // 正在累积、尚未入栈的一组变化
        private HistoryItem _pending;
        private DispatcherTimer _mergeTimer;

        public event Action HistoryChanged;

        public bool CanUndo { get { return _pending != null || _undoStack.Count > 0; } }

        /// <summary>有未入栈的变化时不能重做 —— 那会跳过一个尚未记录的状态。</summary>
        public bool CanRedo { get { return _pending == null && _redoStack.Count > 0; } }

        public HistoryManager(InkCanvas inkCanvas)
        {
            _inkCanvas = inkCanvas;
            _inkCanvas.Strokes.StrokesChanged += OnStrokesChanged;

            // 输入手势开始的三个入口。用 Preview（隧道）阶段即可：
            // 此处只做"落定上一组"，不依赖笔迹是否已经提交。
            _inkCanvas.PreviewMouseLeftButtonDown += delegate { BeginGesture(); };
            _inkCanvas.PreviewStylusDown += delegate { BeginGesture(); };
            _inkCanvas.PreviewTouchDown += delegate { BeginGesture(); };
        }

        /// <summary>
        /// 标记一次新手势的开始：先把上一组累积的变化落定。
        /// 这样"快速连写的好几笔"不会因为间隔短而被并成一步。
        /// </summary>
        public void BeginGesture()
        {
            FlushPending();
        }

        private void OnStrokesChanged(object sender, StrokeCollectionChangedEventArgs e)
        {
            if (_isPerformingHistoryAction) return;
            if (e.Added.Count == 0 && e.Removed.Count == 0) return;

            if (_pending == null) _pending = new HistoryItem();

            // 同一组变化里既加又删的笔迹直接抵消 —— 它本来不存在、现在也不存在，
            // 若留在两边，撤销时会既删又加，结果错乱。
            foreach (Stroke s in e.Added)
            {
                if (_pending.RemovedStrokes.Contains(s)) _pending.RemovedStrokes.Remove(s);
                else _pending.AddedStrokes.Add(s);
            }

            foreach (Stroke s in e.Removed)
            {
                if (_pending.AddedStrokes.Contains(s)) _pending.AddedStrokes.Remove(s);
                else _pending.RemovedStrokes.Add(s);
            }

            ScheduleMerge();
        }

        private void ScheduleMerge()
        {
            if (_mergeTimer == null)
            {
                _mergeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MergeWindowMs) };
                _mergeTimer.Tick += delegate
                {
                    _mergeTimer.Stop();
                    FlushPending();
                };
            }

            _mergeTimer.Stop();
            _mergeTimer.Start();
        }

        /// <summary>把累积的变化整体入栈。撤销 / 重做 / 清屏前都会强制调用一次。</summary>
        private void FlushPending()
        {
            if (_mergeTimer != null) _mergeTimer.Stop();
            if (_pending == null) return;

            HistoryItem item = _pending;
            _pending = null;

            // 加减互相抵消后可能什么都不剩，这种空项不入栈
            if (item.AddedStrokes.Count == 0 && item.RemovedStrokes.Count == 0) return;

            _undoStack.Push(item);
            _redoStack.Clear();

            TrimUndoStack();
            NotifyChanged();
        }

        /// <summary>Stack 只能从顶端弹出，要丢弃最旧的必须重建。</summary>
        private void TrimUndoStack()
        {
            if (_undoStack.Count <= MaxUndoSteps) return;

            HistoryItem[] all = _undoStack.ToArray();   // 索引 0 = 最新
            _undoStack.Clear();
            for (int i = MaxUndoSteps - 1; i >= 0; i--)
            {
                _undoStack.Push(all[i]);
            }
        }

        public void Undo()
        {
            // 必须先落定未入栈的变化，否则"刚画完立刻撤销"会漏掉最近这一笔
            FlushPending();
            if (_undoStack.Count == 0) return;

            _isPerformingHistoryAction = true;
            try
            {
                HistoryItem item = _undoStack.Pop();

                if (item.AddedStrokes.Count > 0) _inkCanvas.Strokes.Remove(item.AddedStrokes);
                if (item.RemovedStrokes.Count > 0) _inkCanvas.Strokes.Add(item.RemovedStrokes);

                _redoStack.Push(item);
            }
            finally
            {
                _isPerformingHistoryAction = false;
                NotifyChanged();
            }
        }

        public void Redo()
        {
            FlushPending();
            if (_redoStack.Count == 0) return;

            _isPerformingHistoryAction = true;
            try
            {
                HistoryItem item = _redoStack.Pop();

                if (item.AddedStrokes.Count > 0) _inkCanvas.Strokes.Add(item.AddedStrokes);
                if (item.RemovedStrokes.Count > 0) _inkCanvas.Strokes.Remove(item.RemovedStrokes);

                _undoStack.Push(item);
            }
            finally
            {
                _isPerformingHistoryAction = false;
                NotifyChanged();
            }
        }

        public void Clear()
        {
            FlushPending();

            if (_inkCanvas.Strokes.Count == 0) return;

            HistoryItem item = new HistoryItem();
            foreach (Stroke s in _inkCanvas.Strokes)
            {
                item.RemovedStrokes.Add(s);
            }

            _isPerformingHistoryAction = true;
            try
            {
                _inkCanvas.Strokes.Clear();
                _undoStack.Push(item);
                _redoStack.Clear();
                TrimUndoStack();
            }
            finally
            {
                _isPerformingHistoryAction = false;
                NotifyChanged();
            }
        }

        private void NotifyChanged()
        {
            if (HistoryChanged != null) HistoryChanged();
        }
    }
}
