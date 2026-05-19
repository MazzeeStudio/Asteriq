using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public sealed partial class SettingsTabController
{
    private const float RightPanelCollapsedH = 40f;

    private void DrawRightPanel(SKCanvas canvas, SKRect bounds)
    {
        bool networkEnabled  = _ctx.AppSettings.NetworkEnabled;
        bool hidHideInstalled = _ctx.HidHide?.IsAvailable() == true;
        const float gap = 8f;

        // If HidHide was uninstalled while the panel was active, fall back to visual
        if (!hidHideInstalled && _settingsRightPanelActive == PanelHidHide)
        {
            _settingsRightPanelActive = PanelVisual;
            _hidHidePanelHeaderBounds = SKRect.Empty;
        }

        // Load HidHide state once on first panel open. Each CLI call spawns HidHideCLI.exe
        // and blocks for ~1-2s, so fan them out in parallel on worker threads and marshal
        // the result back to the UI thread (struct fields below are not atomic).
        if (_settingsRightPanelActive == PanelHidHide && !_hidHideStateLoaded && !_hidHideStateLoading && _ctx.HidHide is not null)
        {
            _hidHideStateLoading = true;
            var hidhide = _ctx.HidHide;
            var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            var cloakTask  = Task.Run(() => hidhide.IsCloakingEnabled());
            var invTask    = Task.Run(() => hidhide.IsInverseMode());
            var hiddenTask = Task.Run(() => hidhide.GetHiddenDevices());
            _ = Task.WhenAll(cloakTask, invTask, hiddenTask).ContinueWith(_ =>
            {
                if (cloakTask.IsCompletedSuccessfully)
                {
                    _hidHideCloaking = cloakTask.Result;
                    _cloakingT = new ToggleAnim { T = cloakTask.Result ? 1f : 0f };
                }
                if (invTask.IsCompletedSuccessfully)
                {
                    _hidHideInverse = invTask.Result;
                    _inverseT = new ToggleAnim { T = invTask.Result ? 1f : 0f };
                }
                if (hiddenTask.IsCompletedSuccessfully)
                    _hidHideHiddenPaths = hiddenTask.Result;
                _hidHideStateLoaded  = true;
                _hidHideStateLoading = false;
                _ctx.InvalidateCanvas();
            }, uiScheduler);
        }

        if (!networkEnabled && !hidHideInstalled)
        {
            // 1-panel: VISUAL only
            _networkPanelHeaderBounds = SKRect.Empty;
            _hidHidePanelHeaderBounds = SKRect.Empty;
            DrawVisualSettingsSubPanel(canvas, bounds);
            return;
        }

        if (!networkEnabled)
        {
            // 2-panel accordion: VISUAL + HIDHIDE
            _networkPanelHeaderBounds = SKRect.Empty;
            if (_settingsRightPanelActive == PanelHidHide)
            {
                float visBottom   = bounds.Top + RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, visBottom);
                var hidHideBounds = new SKRect(bounds.Left, visBottom + gap, bounds.Right, bounds.Bottom);
                DrawVisualPanelCollapsed(canvas, visualBounds);
                DrawHidHideSettingsPanel(canvas, hidHideBounds);
            }
            else
            {
                float hidHideTop  = bounds.Bottom - RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, hidHideTop - gap);
                var hidHideBounds = new SKRect(bounds.Left, hidHideTop, bounds.Right, bounds.Bottom);
                DrawVisualSettingsSubPanel(canvas, visualBounds);
                DrawHidHidePanelCollapsed(canvas, hidHideBounds);
            }
            return;
        }

        if (!hidHideInstalled)
        {
            // 2-panel accordion: VISUAL + NETWORK
            _hidHidePanelHeaderBounds = SKRect.Empty;
            if (_settingsRightPanelActive == PanelNetwork)
            {
                float visBottom   = bounds.Top + RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, visBottom);
                var networkBounds = new SKRect(bounds.Left, visBottom + gap, bounds.Right, bounds.Bottom);
                DrawVisualPanelCollapsed(canvas, visualBounds);
                DrawNetworkSettingsPanel(canvas, networkBounds);
            }
            else
            {
                float netTop      = bounds.Bottom - RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, netTop - gap);
                var networkBounds = new SKRect(bounds.Left, netTop, bounds.Right, bounds.Bottom);
                DrawVisualSettingsSubPanel(canvas, visualBounds);
                DrawNetworkPanelCollapsed(canvas, networkBounds);
            }
            return;
        }

        // 3-panel accordion: VISUAL + NETWORK + HIDHIDE
        // Compute heights from animated expansion weights
        {
            float totalH = bounds.Height - 2 * gap;
            float expandableH = totalH - 3 * RightPanelCollapsedH;
            float wSum = _visualExpandW + _networkExpandW + _hidHideExpandW;
            if (wSum < 0.001f) wSum = 1f; // safety

            float visH = RightPanelCollapsedH + expandableH * (_visualExpandW / wSum);
            float netH = RightPanelCollapsedH + expandableH * (_networkExpandW / wSum);

            float visTop = bounds.Top;
            float netTop = visTop + visH + gap;
            float hidTop = netTop + netH + gap;

            var visualBounds  = new SKRect(bounds.Left, visTop, bounds.Right, visTop + visH);
            var networkBounds = new SKRect(bounds.Left, netTop, bounds.Right, netTop + netH);
            var hidHideBounds = new SKRect(bounds.Left, hidTop, bounds.Right, bounds.Bottom);

            bool visExpanded = _settingsRightPanelActive == PanelVisual;
            bool netExpanded = _settingsRightPanelActive == PanelNetwork;
            bool hidExpanded = _settingsRightPanelActive == PanelHidHide;

            // Visual panel
            canvas.Save();
            canvas.ClipRect(visualBounds);
            if (visExpanded)
            {
                DrawVisualSettingsSubPanel(canvas, visualBounds);
            }
            else
                DrawVisualPanelCollapsed(canvas, visualBounds);
            canvas.Restore();

            // Network panel
            canvas.Save();
            canvas.ClipRect(networkBounds);
            if (netExpanded)
                DrawNetworkSettingsPanel(canvas, networkBounds);
            else
                DrawNetworkPanelCollapsed(canvas, networkBounds);
            canvas.Restore();

            // HidHide panel
            canvas.Save();
            canvas.ClipRect(hidHideBounds);
            if (hidExpanded)
                DrawHidHideSettingsPanel(canvas, hidHideBounds);
            else
                DrawHidHidePanelCollapsed(canvas, hidHideBounds);
            canvas.Restore();
        }
    }

    private void DrawVisualPanelCollapsed(SKCanvas canvas, SKRect bounds)
    {
        Array.Clear(_themeButtonBounds, 0, _themeButtonBounds.Length);
        bool hovered = bounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "VISUAL", false, hovered, out _);
        _visualPanelHeaderBounds = bounds;
    }

    private void DrawNetworkPanelCollapsed(SKCanvas canvas, SKRect bounds)
    {
        bool hovered = bounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "NETWORK", false, hovered, out _);
        _networkPanelHeaderBounds = bounds;
    }

    private void DrawHidHidePanelCollapsed(SKCanvas canvas, SKRect bounds)
    {
        bool hovered = bounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "HIDHIDE", false, hovered, out _);
        _hidHidePanelHeaderBounds = bounds;
    }

}