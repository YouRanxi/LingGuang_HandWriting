using System;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;

namespace LingGuangInk
{
    public class SurfacePenHandler
    {
        private readonly InkCanvas _inkCanvas;
        private readonly AppSettings _settings;
        private InkCanvasEditingMode _previousMode = InkCanvasEditingMode.Ink;
        private bool _isBarrelButtonPressed = false;

        public event Action<bool> OnEraserStateChanged;
        public event Action<string> OnPenStatusTip;

        public SurfacePenHandler(InkCanvas inkCanvas, AppSettings settings)
        {
            _inkCanvas = inkCanvas;
            _settings = settings;

            // 适配 Surface Pen 1776 的笔尾橡皮擦反转
            _inkCanvas.EditingModeInverted = InkCanvasEditingMode.EraseByStroke;

            _inkCanvas.StylusButtonDown += OnStylusButtonDown;
            _inkCanvas.StylusButtonUp += OnStylusButtonUp;
            _inkCanvas.StylusDown += OnStylusDown;
            _inkCanvas.StylusUp += OnStylusUp;
        }

        private void OnStylusDown(object sender, StylusDownEventArgs e)
        {
            if (e.StylusDevice != null)
            {
                // 检测是否使用了 Surface Pen 1776 的尾端橡皮擦
                if (e.StylusDevice.Inverted)
                {
                    if (OnEraserStateChanged != null)
                        OnEraserStateChanged(true);
                }
            }
        }

        private void OnStylusUp(object sender, StylusEventArgs e)
        {
            if (e.StylusDevice != null && e.StylusDevice.Inverted)
            {
                if (OnEraserStateChanged != null)
                    OnEraserStateChanged(false);
            }
        }

        private void OnStylusButtonDown(object sender, StylusButtonEventArgs e)
        {
            // 检测 Surface Pen 1776 笔身侧键 (Barrel Button)
            if (e.StylusButton != null && e.StylusButton.Guid == StylusPointProperties.BarrelButton.Id)
            {
                if (_settings.BarrelButtonHoldToErase)
                {
                    _isBarrelButtonPressed = true;
                    _previousMode = _inkCanvas.EditingMode;
                    _inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;

                    if (OnPenStatusTip != null)
                        OnPenStatusTip("Surface Pen 侧键：快速擦除中");
                    if (OnEraserStateChanged != null)
                        OnEraserStateChanged(true);
                }
            }
        }

        private void OnStylusButtonUp(object sender, StylusButtonEventArgs e)
        {
            if (e.StylusButton != null && e.StylusButton.Guid == StylusPointProperties.BarrelButton.Id)
            {
                if (_isBarrelButtonPressed)
                {
                    _isBarrelButtonPressed = false;

                    // 只在擦除期间用户没有另选工具时才恢复原模式。
                    // 否则会把用户刚从工具栏选的工具静默覆盖掉。
                    bool untouched = _inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke;
                    if (untouched) _inkCanvas.EditingMode = _previousMode;

                    if (OnPenStatusTip != null)
                        OnPenStatusTip(untouched ? "恢复画笔" : "已保留新选择的工具");
                    if (OnEraserStateChanged != null)
                        OnEraserStateChanged(false);
                }
            }
        }
    }
}
