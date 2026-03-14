using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public class SettingsTabController : ITabController, IDisposable
{
    private readonly TabContext _ctx;

    // Profile action button bounds
    private SKRect _newProfileButtonBounds;
    private SKRect _duplicateProfileButtonBounds;
    private SKRect _importProfileButtonBounds;
    private SKRect _exportProfileButtonBounds;
    private SKRect _deleteProfileButtonBounds;

    // Theme selector state
    private SKRect[] _themeButtonBounds = new SKRect[12];


    // Background settings slider bounds
    private SKRect _bgGridSliderBounds;
    private SKRect _bgGlowSliderBounds;
    private SKRect _bgNoiseSliderBounds;
    private SKRect _bgScanlineSliderBounds;
    private SKRect _bgVignetteSliderBounds;
    private SKRect _autoLoadToggleBounds;
    private SKRect _closeToTrayToggleBounds;
    private SKRect _clientOnlyToggleBounds;
    private SKRect _checkUpdatesToggleBounds;
    private string? _draggingBgSlider;

    // Support panel button bounds
    private SKRect _bmacButtonBounds;
    private SKRect _referralCopyButtonBounds;
    private SKRect _referralLinkButtonBounds;

    // Profile name edit
    private SKRect _profileNameBounds;
    private SKRect _profileNameEditBounds;
    private bool _profileNameEditHovered;

    // Driver setup button
    private SKRect _driverSetupButtonBounds;

    // Network forwarding toggle + role/connect UI
    private SKRect _networkEnabledToggleBounds;
    private SKRect[] _netRoleButtonBounds = new SKRect[2]; // 0=Master, 1=Client
    private SKRect _netRegenerateBounds;
    private SKRect _netDisconnectBounds;   // RX (client) role only
    private SKRect _netForgetBounds;
    // Per-peer toggle bounds (TX role) — rebuilt every frame; one entry per discovered peer
    private readonly List<(SKRect Toggle, string IpAddress)> _peerActionBounds = [];
    private readonly Dictionary<string, float> _peerToggleT = new();  // per-peer knob animation (0=off, 0.5=connecting, 1=on)
    private string? _connectingPeerIp;     // IP of the peer a connect attempt is currently targeting

    // Version / update button bounds
    private SKRect _checkButtonBounds;
    private SKRect _downloadButtonBounds;
    private SKRect _applyButtonBounds;

    // Toggle knob animation positions (0 = off, 1 = on); initialized from current settings
    private float _autoLoadT;
    private float _closeToTrayT;
    private float _clientOnlyT;
    private float _networkEnabledT;
    private float _checkUpdatesT;
    private const float ToggleLerpSpeed = 0.14f;  // per 60Hz tick ≈ ~120 ms transition

    // Settings right panel accordion state — "visual" | "network" | "hidhide"
    private string _settingsRightPanelActive;
    private SKRect _visualPanelHeaderBounds;
    private SKRect _networkPanelHeaderBounds;
    private SKRect _hidHidePanelHeaderBounds;

    // HidHide panel state
    private SKRect _hidHideCloakingToggleBounds;
    private SKRect _hidHideInverseToggleBounds;
    private SKRect _hidHideUpdateButtonBounds;
    private bool _hidHideCloaking;
    private bool _hidHideInverse;
    private bool _hidHideStateLoaded;
    private float _cloakingT;
    private float _inverseT;
    private string? _hidHideInstalledVersion;
    private string? _hidHideLatestVersion;
    private string? _hidHideInstallerUrl;
    private bool _hidHideVersionChecked;
    private HidHideInstallPhase _hidHideInstallPhase;
    private int _hidHideDownloadProgress;
    private CancellationTokenSource? _hidHideDownloadCts;

    private enum HidHideInstallPhase { Idle, Downloading, Launching, Error }

    public SettingsTabController(TabContext ctx)
    {
        _ctx = ctx;
        // Snap to current settings on first construction — no animation on startup
        _autoLoadT      = ctx.AppSettings.AutoLoadLastProfile ? 1f : 0f;
        _closeToTrayT   = ctx.AppSettings.CloseToTray ? 1f : 0f;
        _clientOnlyT    = ctx.AppSettings.ClientOnlyMode ? 1f : 0f;
        _networkEnabledT = ctx.AppSettings.NetworkEnabled ? 1f : 0f;
        _checkUpdatesT  = ctx.AppSettings.AutoCheckUpdates ? 1f : 0f;
        var savedPanel = ctx.AppSettings.SettingsRightPanel ?? "visual";
        _settingsRightPanelActive = (savedPanel == "network" && !ctx.AppSettings.NetworkEnabled) ? "visual" : savedPanel;
    }

    public void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom)
    {
        float frameInset = FUIRenderer.FrameInset;
        var contentBounds = new SKRect(padLeft, contentTop, bounds.Right - padLeft, contentBottom);

        const float supportPanelHeight = 100f;
        float supportPanelSep = FUIRenderer.SpaceLG;
        float topAreaBottom = contentBounds.Bottom - supportPanelHeight - supportPanelSep;
        var supportBounds = new SKRect(contentBounds.Left, topAreaBottom + supportPanelSep, contentBounds.Right, contentBounds.Bottom);

        // Three-panel layout: Profile Management | System | Visual (each ~1/3 width)
        float panelGap = FUIRenderer.SpaceLG;
        float availableWidth = contentBounds.Width - panelGap * 2;
        float colWidth = availableWidth / 3f;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + colWidth, topAreaBottom);
        var centerBounds = new SKRect(leftBounds.Right + panelGap, contentBounds.Top,
            leftBounds.Right + panelGap + colWidth, topAreaBottom);
        var rightBounds = new SKRect(centerBounds.Right + panelGap, contentBounds.Top,
            contentBounds.Right, topAreaBottom);

        DrawProfileManagementPanel(canvas, leftBounds, frameInset);
        DrawSystemSettingsSubPanel(canvas, centerBounds, frameInset);
        DrawRightPanel(canvas, rightBounds, frameInset);
        DrawSupportPanel(canvas, supportBounds, frameInset);
    }

    public void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        HandleSettingsTabClick(new SKPoint(e.X, e.Y));
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingBgSlider is not null)
        {
            UpdateBgSliderFromPoint(e.X);
            return;
        }

        var pt = new SKPoint(e.X, e.Y);

        // Profile name box (rename on click)
        if (_profileNameBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Profile action buttons
        if (_newProfileButtonBounds.Contains(pt) ||
            (_duplicateProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null) ||
            _importProfileButtonBounds.Contains(pt) ||
            (_exportProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null) ||
            (_deleteProfileButtonBounds != default && _deleteProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Theme buttons
        foreach (var b in _themeButtonBounds)
        {
            if (!b.IsEmpty && b.Contains(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }



        // Toggles
        if (_autoLoadToggleBounds.Contains(pt) || _closeToTrayToggleBounds.Contains(pt) || _clientOnlyToggleBounds.Contains(pt) || _checkUpdatesToggleBounds.Contains(pt) || _networkEnabledToggleBounds.Contains(pt))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Background sliders
        if (_bgGridSliderBounds.Contains(pt) || _bgGlowSliderBounds.Contains(pt) ||
            _bgNoiseSliderBounds.Contains(pt) || _bgScanlineSliderBounds.Contains(pt) ||
            _bgVignetteSliderBounds.Contains(pt))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Support panel buttons
        if (_bmacButtonBounds.Contains(pt) || _referralCopyButtonBounds.Contains(pt) || _referralLinkButtonBounds.Contains(pt))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Driver setup button
        if (!_driverSetupButtonBounds.IsEmpty && _driverSetupButtonBounds.Contains(pt))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Network buttons
        foreach (var b in _netRoleButtonBounds)
        {
            if (!b.IsEmpty && b.Contains(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }
        if ((!_netRegenerateBounds.IsEmpty  && _netRegenerateBounds.Contains(pt))
            || (!_netDisconnectBounds.IsEmpty && _netDisconnectBounds.Contains(pt))
            || (!_netForgetBounds.IsEmpty    && _netForgetBounds.Contains(pt)))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }
        foreach (var (toggleRect, _) in _peerActionBounds)
        {
            if (toggleRect.Contains(pt))
            {
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }
        }

        // Update section buttons
        if ((!_checkButtonBounds.IsEmpty && _checkButtonBounds.Contains(pt))
            || (!_downloadButtonBounds.IsEmpty && _downloadButtonBounds.Contains(pt))
            || (!_applyButtonBounds.IsEmpty && _applyButtonBounds.Contains(pt)))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }

        // Right panel accordion headers
        if ((!_visualPanelHeaderBounds.IsEmpty && _visualPanelHeaderBounds.Contains(pt))
            || (!_networkPanelHeaderBounds.IsEmpty && _networkPanelHeaderBounds.Contains(pt)))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
    }

    public void OnMouseUp(MouseEventArgs e)
    {
        if (_draggingBgSlider is not null)
        {
            _draggingBgSlider = null;
            SaveBackgroundSettings();
            _ctx.MarkDirty();
        }
    }

    public void OnMouseWheel(MouseEventArgs e) { }
    public bool ProcessCmdKey(ref Message msg, Keys keyData) => false;
    public void OnMouseLeave() { }
    public void OnTick()
    {
        _autoLoadT      = LerpToggle(_autoLoadT,      _ctx.AppSettings.AutoLoadLastProfile);
        _closeToTrayT   = LerpToggle(_closeToTrayT,   _ctx.AppSettings.CloseToTray);
        _clientOnlyT    = LerpToggle(_clientOnlyT,    _ctx.AppSettings.ClientOnlyMode);
        _networkEnabledT = LerpToggle(_networkEnabledT, _ctx.AppSettings.NetworkEnabled);
        _checkUpdatesT  = LerpToggle(_checkUpdatesT,  _ctx.AppSettings.AutoCheckUpdates);
        _cloakingT      = LerpToggle(_cloakingT,      _hidHideCloaking);
        _inverseT       = LerpToggle(_inverseT,        _hidHideInverse);

        // Per-peer connection toggles
        bool netConnected  = _ctx.NetworkMode == NetworkInputMode.Remote;
        bool netConnecting = _ctx.IsNetworkConnecting;
        string? connectedIp  = _ctx.ConnectedPeerIp;
        string? connectingIp = _connectingPeerIp;
        var peers = _ctx.NetworkDiscovery?.KnownPeers.Values.ToList() ?? [];
        var visibleIps = new HashSet<string>(peers.Select(p => p.IpAddress));

        foreach (var p in peers)
        {
            float target = (netConnected  && connectedIp  == p.IpAddress) ? 1f
                         : (netConnecting && connectingIp == p.IpAddress) ? 0.5f
                         : 0f;
            if (!_peerToggleT.TryGetValue(p.IpAddress, out float cur))
                cur = target;  // snap on first appearance — no animation
            float delta = target - cur;
            _peerToggleT[p.IpAddress] = MathF.Abs(delta) < 0.002f ? target : cur + delta * ToggleLerpSpeed;
        }

        // Remove stale entries
        foreach (var key in _peerToggleT.Keys.Where(k => !visibleIps.Contains(k)).ToList())
            _peerToggleT.Remove(key);
    }

    private static float LerpToggle(float current, bool on)
    {
        float target = on ? 1f : 0f;
        float delta = target - current;
        return MathF.Abs(delta) < 0.002f ? target : current + delta * ToggleLerpSpeed;
    }
    public void OnActivated() { }
    public void OnDeactivated() { }

    public bool IsDraggingSlider => _draggingBgSlider is not null;

    #region Drawing

    private void DrawProfileManagementPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var metrics = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = metrics.Y;
        float leftMargin = metrics.LeftMargin;
        float rightMargin = metrics.RightMargin;
        float bottom = bounds.Bottom - frameInset - FUIRenderer.SpaceLG;

        canvas.Save();
        canvas.ClipRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom - frameInset));

        y = FUIRenderer.DrawPanelHeader(canvas, "CONFIGURATION MANAGEMENT", leftMargin, y);

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is not null)
        {
            y = FUIRenderer.DrawSectionHeader(canvas, "ACTIVE CONFIGURATION", leftMargin, y);

            float nameBoxHeight = 32f;
            _profileNameBounds = new SKRect(leftMargin, y, rightMargin, y + nameBoxHeight);
            bool nameHovered = _profileNameBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            FUIRenderer.DrawRoundedPanel(canvas, _profileNameBounds, FUIColors.Active.WithAlpha(30), FUIColors.Active, 4f);

            float nameTextY = y + (nameBoxHeight - FUIRenderer.FontBody) / 2 + FUIRenderer.FontBody - 3;
            FUIRenderer.DrawText(canvas, profile.Name, new SKPoint(leftMargin + 10, nameTextY), FUIColors.TextBright, FUIRenderer.FontBody, true);

            // Pencil edit icon on hover (right side of name box)
            if (nameHovered)
            {
                float editSize = 20f;
                float editX = _profileNameBounds.Right - editSize - 6f;
                float editY = _profileNameBounds.MidY - editSize / 2f;
                _profileNameEditBounds = new SKRect(editX, editY, editX + editSize, editY + editSize);
                _profileNameEditHovered = _profileNameEditBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                var iconColor = _profileNameEditHovered ? FUIColors.Active : FUIColors.TextDim;
                float cx = _profileNameEditBounds.MidX;
                float cy = _profileNameEditBounds.MidY;
                using var penPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = iconColor,
                    StrokeWidth = 1.2f,
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round
                };
                canvas.DrawLine(cx - 4f, cy + 4f, cx + 3f, cy - 3f, penPaint);
                canvas.DrawLine(cx - 4f, cy + 4f, cx - 5f, cy + 5.5f, penPaint);
                canvas.DrawLine(cx + 3f, cy - 3f, cx + 5f, cy - 5f, penPaint);
            }
            else
            {
                _profileNameEditBounds = SKRect.Empty;
                _profileNameEditHovered = false;
            }

            y += nameBoxHeight + 24f;

            float lineHeight = metrics.RowHeight;
            y = FUIRenderer.DrawSectionHeader(canvas, "STATISTICS", leftMargin, y);

            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Axis Mappings", profile.AxisMappings.Count.ToString());
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Button Mappings", profile.ButtonMappings.Count.ToString());
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Hat Mappings", profile.HatMappings.Count.ToString());
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Shift Layers", profile.ShiftLayers.Count.ToString());
            y += lineHeight + 6f;

            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Created", profile.CreatedAt.ToLocalTime().ToString("g"));
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Modified", profile.ModifiedAt.ToLocalTime().ToString("g"));
            y += lineHeight + 10f;
        }
        else
        {
            FUIRenderer.DrawText(canvas, "No configuration active", new SKPoint(leftMargin, y), FUIColors.TextDim, 15f);
            y += 40f;
        }

        y = FUIRenderer.DrawSectionHeader(canvas, "ACTIONS", leftMargin, y);

        float buttonHeight = 28f;
        float buttonGap = FUIRenderer.SpaceSM;
        float buttonWidth = (metrics.ContentWidth - buttonGap) / 2;

        _newProfileButtonBounds = new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight);
        _duplicateProfileButtonBounds = new SKRect(rightMargin - buttonWidth, y, rightMargin, y + buttonHeight);
        bool newHovered = _newProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool dupHovered = _duplicateProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _newProfileButtonBounds, "New Configuration",
            newHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawButton(canvas, _duplicateProfileButtonBounds,
            profile is not null ? "Duplicate" : "---",
            profile is null ? FUIRenderer.ButtonState.Disabled : (dupHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal));
        y += buttonHeight + buttonGap;

        _importProfileButtonBounds = new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight);
        _exportProfileButtonBounds = new SKRect(rightMargin - buttonWidth, y, rightMargin, y + buttonHeight);
        bool importHovered = _importProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool exportHovered = _exportProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _importProfileButtonBounds, "Import",
            importHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawButton(canvas, _exportProfileButtonBounds,
            profile is not null ? "Export" : "---",
            profile is null ? FUIRenderer.ButtonState.Disabled : (exportHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal));
        y += buttonHeight + buttonGap;

        if (profile is not null && y + buttonHeight <= bottom)
        {
            _deleteProfileButtonBounds = new SKRect(leftMargin, y, rightMargin, y + buttonHeight);
            bool deleteHovered = _deleteProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIRenderer.DrawButton(canvas, _deleteProfileButtonBounds, "Delete Configuration",
                deleteHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal, isDanger: true);
            y += buttonHeight + 20f;

            if (y < bottom - 60)
            {
                FUIWidgets.DrawShiftLayersSection(canvas, leftMargin, rightMargin, y, bottom, profile);
            }
        }

        canvas.Restore();
    }

    private void DrawApplicationSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        float gap = FUIRenderer.SpaceSM;
        float leftWidth = (bounds.Width - gap) * 0.52f;
        float rightWidth = (bounds.Width - gap) * 0.48f;

        var leftBounds = new SKRect(bounds.Left, bounds.Top, bounds.Left + leftWidth, bounds.Bottom);
        var rightBounds = new SKRect(bounds.Left + leftWidth + gap, bounds.Top, bounds.Right, bounds.Bottom);

        DrawSystemSettingsSubPanel(canvas, leftBounds, frameInset);
        DrawVisualSettingsSubPanel(canvas, rightBounds, frameInset);
    }

    private void DrawSystemSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        float contentWidth = m.ContentWidth;
        float sectionSpacing = 20f;
        float rowHeight = 24f;
        float minControlGap = 12f;

        FUIRenderer.DrawText(canvas, "SYSTEM", new SKPoint(leftMargin, y), FUIColors.TextBright, FUIRenderer.FontBody, true);
        y += 32f;

        // Auto-load setting
        float toggleWidth = 48f;
        float toggleHeight = 24f;
        float autoLoadLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float autoLoadLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Auto-load configuration", new SKPoint(leftMargin, autoLoadLabelY),
            autoLoadLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float toggleY = y + (rowHeight - toggleHeight) / 2;
        _autoLoadToggleBounds = new SKRect(rightMargin - toggleWidth, toggleY, rightMargin, toggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _autoLoadToggleBounds, _autoLoadT, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Close to Tray toggle
        float closeToTrayLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float closeToTrayLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Close to tray", new SKPoint(leftMargin, closeToTrayLabelY),
            closeToTrayLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float closeToTrayToggleY = y + (rowHeight - toggleHeight) / 2;
        _closeToTrayToggleBounds = new SKRect(rightMargin - toggleWidth, closeToTrayToggleY, rightMargin, closeToTrayToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _closeToTrayToggleBounds, _closeToTrayT, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Client Only Mode toggle
        float clientOnlyLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float clientOnlyLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Client Only Mode", new SKPoint(leftMargin, clientOnlyLabelY),
            clientOnlyLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float clientOnlyToggleY = y + (rowHeight - toggleHeight) / 2;
        _clientOnlyToggleBounds = new SKRect(rightMargin - toggleWidth, clientOnlyToggleY, rightMargin, clientOnlyToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _clientOnlyToggleBounds, _clientOnlyT, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // DRIVERS section
        FUIRenderer.DrawText(canvas, "DRIVERS", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += sectionSpacing;

        var vjoyDevices = _ctx.VJoyService.EnumerateDevices();
        bool vjoyEnabled = vjoyDevices.Count > 0;
        var driverStatus = _ctx.DriverSetupManager.GetSetupStatus();

        float statusDotRadius = 4f;
        float statusLineHeight = 21f;
        float statusTextX = leftMargin + statusDotRadius * 2 + 8;

        // vJoy status row
        {
            string vjoyStatusStr = vjoyEnabled ? "Driver active" : "Not installed";
            var vjoyColor = vjoyEnabled ? FUIColors.Success : FUIColors.Danger;
            float dotY = y + (statusLineHeight / 2);
            float textY = y + (statusLineHeight / 2) + 4;

            using var vjoyDot = FUIRenderer.CreateFillPaint(vjoyColor);
            canvas.DrawCircle(leftMargin + statusDotRadius + 1, dotY, statusDotRadius, vjoyDot);
            FUIRenderer.DrawText(canvas, "vJoy", new SKPoint(statusTextX, textY), FUIColors.TextPrimary, 14f);
            float vjoyLabelW = FUIRenderer.MeasureText("vJoy", 14f);
            FUIRenderer.DrawText(canvas, vjoyStatusStr, new SKPoint(statusTextX + vjoyLabelW + 12f, textY),
                vjoyEnabled ? FUIColors.TextDim : FUIColors.Danger, 13f);
            y += statusLineHeight;
        }

        // HidHide status row + DRIVER SETUP button
        {
            string hidHideStatusStr = driverStatus.HidHideInstalled ? "Available" : "Not installed";
            var hidHideColor = driverStatus.HidHideInstalled ? FUIColors.Success : FUIColors.Warning;
            float dotY = y + (statusLineHeight / 2);
            float textY = y + (statusLineHeight / 2) + 4;

            using var hidHideDot = FUIRenderer.CreateFillPaint(hidHideColor);
            canvas.DrawCircle(leftMargin + statusDotRadius + 1, dotY, statusDotRadius, hidHideDot);
            FUIRenderer.DrawText(canvas, "HidHide", new SKPoint(statusTextX, textY), FUIColors.TextPrimary, 14f);
            float hidHideLabelW = FUIRenderer.MeasureText("HidHide", 14f);
            FUIRenderer.DrawText(canvas, hidHideStatusStr, new SKPoint(statusTextX + hidHideLabelW + 12f, textY),
                driverStatus.HidHideInstalled ? FUIColors.TextDim : FUIColors.Warning, 13f);

            // DRIVER SETUP button — right-aligned on the HidHide row
            float setupBtnWidth = 110f;
            float setupBtnHeight = 22f;
            float setupBtnY = y + (statusLineHeight - setupBtnHeight) / 2;
            _driverSetupButtonBounds = new SKRect(rightMargin - setupBtnWidth, setupBtnY, rightMargin, setupBtnY + setupBtnHeight);
            bool setupHovered = _driverSetupButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIRenderer.DrawButton(canvas, _driverSetupButtonBounds, "DRIVER SETUP",
                setupHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            y += statusLineHeight;
        }

        // Available devices line (only when vJoy is active)
        if (vjoyEnabled)
        {
            y += 4f; // extra spacing from the status dot rows
            FUIRenderer.DrawTextTruncated(canvas, $"Available devices: {vjoyDevices.Count}",
                new SKPoint(leftMargin, y + 12f), contentWidth, FUIColors.TextDim, 13f);
            y += rowHeight;
        }
        y += sectionSpacing;

        // NETWORK enable toggle — only shown when vJoy is installed
        if (driverStatus.IsComplete)
        {
            FUIRenderer.DrawText(canvas, "NETWORK", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += sectionSpacing;

            float netLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
            float netLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
            FUIRenderer.DrawTextTruncated(canvas, "Enable network forwarding", new SKPoint(leftMargin, netLabelY),
                netLabelMaxWidth, FUIColors.TextPrimary, 14f);
            float netToggleY = y + (rowHeight - toggleHeight) / 2;
            _networkEnabledToggleBounds = new SKRect(rightMargin - toggleWidth, netToggleY, rightMargin, netToggleY + toggleHeight);
            FUIWidgets.DrawToggleSwitch(canvas, _networkEnabledToggleBounds, _networkEnabledT, _ctx.MousePosition);
            y += rowHeight + sectionSpacing;
        }
        else
        {
            _networkEnabledToggleBounds = SKRect.Empty;
        }

        // VERSION & UPDATES section
        FUIRenderer.DrawText(canvas, "VERSION & UPDATES", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += sectionSpacing;

        // "Check for updates automatically" toggle — first in this section
        float autoCheckLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float autoCheckLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Check for updates automatically", new SKPoint(leftMargin, autoCheckLabelY),
            autoCheckLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float autoCheckToggleY = y + (rowHeight - toggleHeight) / 2;
        _checkUpdatesToggleBounds = new SKRect(rightMargin - toggleWidth, autoCheckToggleY, rightMargin, autoCheckToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _checkUpdatesToggleBounds, _checkUpdatesT, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Version row: "v0.8.289" left, [Check for updates] button right
        string currentVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        string versionStr = $"v{currentVersion}";

        var updateStatus = _ctx.UpdateService.Status;
        float checkBtnWidth = 150f;
        float checkBtnHeight = 28f;
        bool checkBtnEnabled = updateStatus is UpdateStatus.Unknown or UpdateStatus.UpToDate or UpdateStatus.Error;
        _checkButtonBounds = new SKRect(rightMargin - checkBtnWidth, y, rightMargin, y + checkBtnHeight);
        bool checkHovered = checkBtnEnabled && _checkButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        string checkLabel = updateStatus == UpdateStatus.Checking ? "CHECKING\u2026" : "CHECK FOR UPDATES";
        FUIRenderer.DrawButton(canvas, _checkButtonBounds, checkLabel,
            (!checkBtnEnabled || updateStatus == UpdateStatus.Checking) ? FUIRenderer.ButtonState.Disabled
            : checkHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Version text — vertically aligned with the button
        float versionTextY = y + (checkBtnHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawText(canvas, versionStr, new SKPoint(leftMargin, versionTextY), FUIColors.TextPrimary, 14f);
        y += checkBtnHeight + 4f;

        // "Last checked" timestamp
        var lastChecked = _ctx.UpdateService.LastChecked;
        string lastCheckedStr = lastChecked.HasValue
            ? $"Last checked: {lastChecked.Value:dd/MM/yyyy HH:mm}"
            : "Last checked: never";
        FUIRenderer.DrawText(canvas, lastCheckedStr, new SKPoint(leftMargin, y + 10f), FUIColors.TextDim, 12f);
        y += 20f;

        // Status banner — contextual based on update status
        _downloadButtonBounds = SKRect.Empty;
        _applyButtonBounds = SKRect.Empty;

        float bannerHeight = 32f;
        float bannerRadius = 4f;
        string latest = _ctx.UpdateService.LatestVersion ?? "?";

        if (updateStatus != UpdateStatus.Unknown && updateStatus != UpdateStatus.Checking)
        {
            var bannerRect = new SKRect(leftMargin, y, rightMargin, y + bannerHeight);
            float dotRadius = 4f;
            float dotX = leftMargin + 12f;
            float dotY = y + bannerHeight / 2f;
            float textStartX = dotX + dotRadius + 8f;
            float textY = y + bannerHeight / 2f + 4f;

            switch (updateStatus)
            {
                case UpdateStatus.UpToDate:
                {
                    FUIRenderer.DrawRoundedPanel(canvas, bannerRect, FUIColors.Active.WithAlpha(25), FUIColors.Active.WithAlpha(50), bannerRadius);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, "Asteriq is up to date", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);
                    break;
                }

                case UpdateStatus.UpdateAvailable:
                {
                    FUIRenderer.DrawRoundedPanel(canvas, bannerRect, FUIColors.Active.WithAlpha(25), FUIColors.Active.WithAlpha(50), bannerRadius);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    string availText = $"Update available: v{latest}";
                    FUIRenderer.DrawText(canvas, availText, new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);

                    // Inline "Download update" button
                    float dlBtnWidth = FUIRenderer.MeasureText("DOWNLOAD", 13f) + 24f;
                    _downloadButtonBounds = new SKRect(rightMargin - dlBtnWidth - 6f, y + 4f, rightMargin - 6f, y + bannerHeight - 4f);
                    bool dlHovered = _downloadButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                    FUIRenderer.DrawButton(canvas, _downloadButtonBounds, "DOWNLOAD",
                        dlHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
                    break;
                }

                case UpdateStatus.Downloading:
                {
                    int pct = _ctx.UpdateService.DownloadProgress;
                    // Progress bar fill as banner background
                    using var bannerBg = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(15));
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBg);
                    float fillWidth = bannerRect.Width * (pct / 100f);
                    var fillRect = new SKRect(bannerRect.Left, bannerRect.Top, bannerRect.Left + fillWidth, bannerRect.Bottom);
                    using var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(40));
                    canvas.Save();
                    using var bannerClip = new SKRoundRect(bannerRect, bannerRadius);
                    canvas.ClipRoundRect(bannerClip);
                    canvas.DrawRect(fillRect, fillPaint);
                    canvas.Restore();
                    using var bannerBorder = FUIRenderer.CreateStrokePaint(FUIColors.Active.WithAlpha(50));
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBorder);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, $"Downloading update\u2026 {pct}%", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);
                    break;
                }

                case UpdateStatus.ReadyToApply:
                {
                    FUIRenderer.DrawRoundedPanel(canvas, bannerRect, FUIColors.Active.WithAlpha(25), FUIColors.Active.WithAlpha(50), bannerRadius);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, "Update ready", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);

                    // Inline "Apply update" button
                    float apBtnWidth = FUIRenderer.MeasureText("APPLY", 13f) + 24f;
                    _applyButtonBounds = new SKRect(rightMargin - apBtnWidth - 6f, y + 4f, rightMargin - 6f, y + bannerHeight - 4f);
                    bool apHovered = _applyButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                    FUIRenderer.DrawButton(canvas, _applyButtonBounds, "APPLY",
                        apHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
                    break;
                }

                case UpdateStatus.Error:
                {
                    FUIRenderer.DrawRoundedPanel(canvas, bannerRect, FUIColors.Danger.WithAlpha(25), FUIColors.Danger.WithAlpha(50), bannerRadius);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Danger);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, "Check failed", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);
                    break;
                }
            }
            y += bannerHeight + 8f;
        }
    }

    private void DrawVisualSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        float contentWidth = m.ContentWidth;
        float sectionSpacing = 16f;

        FUIRenderer.DrawText(canvas, "VISUAL", new SKPoint(leftMargin, y), FUIColors.TextBright, FUIRenderer.FontBody, true);
        y += 32f;

        // Theme section
        float themeLabelWidth = 36f;
        float themeAreaWidth = contentWidth - themeLabelWidth;
        float themeBtnGap = 4f;
        float themeBtnWidth = Math.Min(40f, (themeAreaWidth - themeBtnGap * 3) / 4);
        float themeBtnHeight = FUIRenderer.TouchTargetMinHeight;
        float themeBtnsStartX = leftMargin + themeLabelWidth;

        float themeLabelY = themeBtnHeight / 2 + 3;
        FUIRenderer.DrawTextTruncated(canvas, "Core", new SKPoint(leftMargin, y + themeLabelY), themeLabelWidth - 5, FUIColors.TextDim, 12f);

        FUITheme[] coreThemes = { FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice };
        string[] coreNames = { "MID", "MTX", "AMB", "ICE" };
        SKColor[] coreColors = {
            new SKColor(0x40, 0xA0, 0xFF), new SKColor(0x40, 0xFF, 0x40),
            new SKColor(0xFF, 0xA0, 0x40), new SKColor(0x40, 0xE0, 0xFF)
        };

        for (int i = 0; i < coreThemes.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);
            StoreThemeButtonBounds(i, themeBounds);
            FUIWidgets.DrawThemeButton(canvas, themeBounds, coreNames[i], coreColors[i], FUIColors.CurrentTheme == coreThemes[i], _ctx.MousePosition);
        }
        y += themeBtnHeight + 8;

        FUIRenderer.DrawTextTruncated(canvas, "Mfr", new SKPoint(leftMargin, y + themeLabelY), themeLabelWidth - 5, FUIColors.TextDim, 12f);

        FUITheme[] mfrThemes1 = { FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo };
        string[] mfrNames1 = { "DRK", "AEG", "ANV", "ARG" };
        SKColor[] mfrColors1 = {
            new SKColor(0xFF, 0x80, 0x20), new SKColor(0x40, 0x90, 0xE0),
            new SKColor(0x90, 0xC0, 0x40), new SKColor(0xFF, 0xC0, 0x00)
        };

        for (int i = 0; i < mfrThemes1.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);
            StoreThemeButtonBounds(4 + i, themeBounds);
            FUIWidgets.DrawThemeButton(canvas, themeBounds, mfrNames1[i], mfrColors1[i], FUIColors.CurrentTheme == mfrThemes1[i], _ctx.MousePosition);
        }
        y += themeBtnHeight + 4;

        FUITheme[] mfrThemes2 = { FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI };
        string[] mfrNames2 = { "CRU", "ORI", "MSC", "RSI" };
        SKColor[] mfrColors2 = {
            new SKColor(0x40, 0x90, 0xE0), new SKColor(0xD4, 0xAF, 0x37),
            new SKColor(0x40, 0xC0, 0x90), new SKColor(0x50, 0xA0, 0xF0)
        };

        for (int i = 0; i < mfrThemes2.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);
            StoreThemeButtonBounds(8 + i, themeBounds);
            FUIWidgets.DrawThemeButton(canvas, themeBounds, mfrNames2[i], mfrColors2[i], FUIColors.CurrentTheme == mfrThemes2[i], _ctx.MousePosition);
        }
        y += themeBtnHeight + 14f;

        // ── Colour Palette preview ─────────────────────────────────────────
        FUIRenderer.DrawText(canvas, "PALETTE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 22f;

        float swatchGap = 5f;
        float swatchW   = (contentWidth - swatchGap * 3f) / 4f;
        float swatchH   = 28f;

        // Row 1 — accent / state colours rendered as "mini button" style (tinted bg + border + text)
        // This matches how these colours actually appear in UI elements (e.g. the Danger Delete button).
        (SKColor color, string label)[] row1 =
        [
            (FUIColors.Primary, "PRIMARY"),
            (FUIColors.Active,  "ACTIVE"),
            (FUIColors.Warning, "WARNING"),
            (FUIColors.Danger,  "DANGER"),
        ];
        for (int i = 0; i < row1.Length; i++)
        {
            float sx = leftMargin + i * (swatchW + swatchGap);
            var rect = new SKRect(sx, y, sx + swatchW, y + swatchH);
            // Dark background tinted with the colour — mirrors Delete / Share button style
            using var tintFill = FUIRenderer.CreateFillPaint(row1[i].color.WithAlpha(35));
            canvas.DrawRect(rect, tintFill);
            using var borderPaint = FUIRenderer.CreateStrokePaint(row1[i].color.WithAlpha(180));
            canvas.DrawRect(rect, borderPaint);
            float lblW = FUIRenderer.MeasureText(row1[i].label, 9f);
            FUIRenderer.DrawText(canvas, row1[i].label,
                new SKPoint(sx + swatchW / 2f - lblW / 2f, y + swatchH / 2f + 4f),
                row1[i].color, 9f);
        }
        y += swatchH + 5f;

        // Row 2 — text hierarchy + frame: solid fills showing the literal colour values
        (SKColor color, string label)[] row2 =
        [
            (FUIColors.TextBright,  "BRIGHT"),
            (FUIColors.TextPrimary, "TEXT"),
            (FUIColors.TextDim,     "DIM"),
            (FUIColors.Frame,       "FRAME"),
        ];
        for (int i = 0; i < row2.Length; i++)
        {
            float sx = leftMargin + i * (swatchW + swatchGap);
            var rect = new SKRect(sx, y, sx + swatchW, y + swatchH);
            using var fill = FUIRenderer.CreateFillPaint(row2[i].color);
            canvas.DrawRect(rect, fill);
            // Dark label band at bottom
            var band = new SKRect(rect.Left, rect.Bottom - 14f, rect.Right, rect.Bottom);
            using var bandFill = FUIRenderer.CreateFillPaint(new SKColor(0, 0, 0, 130));
            canvas.DrawRect(band, bandFill);
            float lblW = FUIRenderer.MeasureText(row2[i].label, 9f);
            FUIRenderer.DrawText(canvas, row2[i].label,
                new SKPoint(sx + swatchW / 2f - lblW / 2f, rect.Bottom - 2f),
                new SKColor(0xFF, 0xFF, 0xFF, 200), 9f);
        }
        y += swatchH + sectionSpacing;
        // ── end palette ────────────────────────────────────────────────────

        // Background effects section — directly below themes
        FUIRenderer.DrawText(canvas, "BACKGROUND", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += sectionSpacing + 8;

        string[] sliderLabels = { "Grid", "Glow", "Noise", "Scanlines", "Vignette" };
        float maxLabelWidth = 0;
        foreach (var label in sliderLabels)
        {
            float w = FUIRenderer.MeasureText(label, 14f);
            if (w > maxLabelWidth) maxLabelWidth = w;
        }

        float labelColumnWidth = maxLabelWidth + 10f;
        float valueColumnWidth = FUIRenderer.MeasureText("100", 13f) + 8f;
        float sliderLeft = leftMargin + labelColumnWidth;
        float sliderRight = rightMargin - valueColumnWidth;
        float sliderRowHeight = 22f;
        float sliderRowGap = 8f;

        if (sliderRight - sliderLeft < 50)
        {
            sliderLeft = leftMargin + 50;
            sliderRight = rightMargin - 30;
        }

        float sliderHeight = 12f;
        float sliderYOff = (sliderRowHeight - sliderHeight) / 2;
        float textY = sliderRowHeight / 2 + 4;

        var bg = _ctx.Background;

        FUIRenderer.DrawTextTruncated(canvas, "Grid", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgGridSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgGridSliderBounds, bg.GridStrength, 100);
        FUIRenderer.DrawText(canvas, bg.GridStrength.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Glow", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgGlowSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgGlowSliderBounds, bg.GlowIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.GlowIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Noise", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgNoiseSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgNoiseSliderBounds, bg.NoiseIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.NoiseIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Scanlines", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgScanlineSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgScanlineSliderBounds, bg.ScanlineIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.ScanlineIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Vignette", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgVignetteSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgVignetteSliderBounds, bg.VignetteStrength, 100);
        FUIRenderer.DrawText(canvas, bg.VignetteStrength.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sectionSpacing;

    }

    private void DrawSupportPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;

        // Header row: "SUPPORT" left, SC referral descriptor right-aligned
        FUIRenderer.DrawText(canvas, "SUPPORT", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        const string scDescriptor = "Referral Code \u00b7 50,000 Bonus aUEC";
        float descWidth = FUIRenderer.MeasureText(scDescriptor, 12f);
        FUIRenderer.DrawText(canvas, scDescriptor, new SKPoint(rightMargin - descWidth, y + 1f), FUIColors.TextDim, 12f);
        y += 20f;

        float btnHeight = 28f;

        // Left: Buy Me a Coffee button
        float bmacWidth = 170f;
        _bmacButtonBounds = new SKRect(leftMargin, y, leftMargin + bmacWidth, y + btnHeight);
        bool bmacHovered = _bmacButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _bmacButtonBounds, "BUY ME A COFFEE",
            bmacHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Right: Join Star Citizen button — anchored to right margin
        float scLinkWidth = 180f;
        _referralLinkButtonBounds = new SKRect(rightMargin - scLinkWidth, y, rightMargin, y + btnHeight);
        bool scLinkHovered = _referralLinkButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _referralLinkButtonBounds, "JOIN STAR CITIZEN \u2192",
            scLinkHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Center: code field + copy button — centered between BMAC and JOIN
        float codeFieldWidth = 138f;
        float copyBtnWidth = 56f;
        float referralGroupWidth = codeFieldWidth + FUIRenderer.SpaceSM + copyBtnWidth;
        float referralGroupX = leftMargin + (rightMargin - leftMargin - referralGroupWidth) / 2;

        var codeDisplayBounds = new SKRect(referralGroupX, y, referralGroupX + codeFieldWidth, y + btnHeight);
        FUIWidgets.DrawSettingsValueField(canvas, codeDisplayBounds, "STAR-RBDQ-Z4JG");

        _referralCopyButtonBounds = new SKRect(
            codeDisplayBounds.Right + FUIRenderer.SpaceSM, y,
            codeDisplayBounds.Right + FUIRenderer.SpaceSM + copyBtnWidth, y + btnHeight);
        bool copyHovered = _referralCopyButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _referralCopyButtonBounds, "COPY",
            copyHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
    }


    private void StoreThemeButtonBounds(int index, SKRect bounds)
    {
        if (index >= 0 && index < _themeButtonBounds.Length)
        {
            _themeButtonBounds[index] = bounds;
        }
    }

    private const float RightPanelCollapsedH = 40f;

    private void DrawRightPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        bool networkEnabled  = _ctx.AppSettings.NetworkEnabled;
        bool hidHideInstalled = _ctx.HidHide?.IsAvailable() == true;
        const float gap = 8f;

        // If HidHide was uninstalled while the panel was active, fall back to visual
        if (!hidHideInstalled && _settingsRightPanelActive == "hidhide")
        {
            _settingsRightPanelActive = "visual";
            _hidHidePanelHeaderBounds = SKRect.Empty;
        }

        // Load HidHide state fresh when the panel first opens
        if (_settingsRightPanelActive == "hidhide" && !_hidHideStateLoaded)
        {
            _hidHideCloaking = _ctx.HidHide!.IsCloakingEnabled();
            _hidHideInverse  = _ctx.HidHide!.IsInverseMode();
            _cloakingT = _hidHideCloaking ? 1f : 0f;
            _inverseT  = _hidHideInverse  ? 1f : 0f;
            _hidHideStateLoaded = true;
        }

        if (!networkEnabled && !hidHideInstalled)
        {
            // 1-panel: VISUAL only
            _networkPanelHeaderBounds = SKRect.Empty;
            _hidHidePanelHeaderBounds = SKRect.Empty;
            DrawVisualSettingsSubPanel(canvas, bounds, frameInset);
            _visualPanelHeaderBounds = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + RightPanelCollapsedH);
            return;
        }

        if (!networkEnabled)
        {
            // 2-panel accordion: VISUAL + HIDHIDE
            _networkPanelHeaderBounds = SKRect.Empty;
            if (_settingsRightPanelActive == "hidhide")
            {
                float visBottom   = bounds.Top + RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, visBottom);
                var hidHideBounds = new SKRect(bounds.Left, visBottom + gap, bounds.Right, bounds.Bottom);
                DrawVisualPanelCollapsed(canvas, visualBounds);
                DrawHidHideSettingsPanel(canvas, hidHideBounds, frameInset);
            }
            else
            {
                float hidHideTop  = bounds.Bottom - RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, hidHideTop - gap);
                var hidHideBounds = new SKRect(bounds.Left, hidHideTop, bounds.Right, bounds.Bottom);
                DrawVisualSettingsSubPanel(canvas, visualBounds, frameInset);
                _visualPanelHeaderBounds = new SKRect(visualBounds.Left, visualBounds.Top, visualBounds.Right, visualBounds.Top + RightPanelCollapsedH);
                bool visHov = _visualPanelHeaderBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                float indW = FUIRenderer.MeasureText("-", 13f);
                FUIRenderer.DrawText(canvas, "-", new SKPoint(visualBounds.Right - FUIRenderer.FrameInset - indW, visualBounds.Top + FUIRenderer.FrameInset + 18f),
                    visHov ? FUIColors.TextBright : FUIColors.Active.WithAlpha(100), 13f, true);
                DrawHidHidePanelCollapsed(canvas, hidHideBounds);
            }
            return;
        }

        if (!hidHideInstalled)
        {
            // 2-panel accordion: VISUAL + NETWORK
            _hidHidePanelHeaderBounds = SKRect.Empty;
            if (_settingsRightPanelActive == "network")
            {
                float visBottom   = bounds.Top + RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, visBottom);
                var networkBounds = new SKRect(bounds.Left, visBottom + gap, bounds.Right, bounds.Bottom);
                DrawVisualPanelCollapsed(canvas, visualBounds);
                DrawNetworkSettingsPanel(canvas, networkBounds, frameInset);
            }
            else
            {
                float netTop      = bounds.Bottom - RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, netTop - gap);
                var networkBounds = new SKRect(bounds.Left, netTop, bounds.Right, bounds.Bottom);
                DrawVisualSettingsSubPanel(canvas, visualBounds, frameInset);
                _visualPanelHeaderBounds = new SKRect(visualBounds.Left, visualBounds.Top, visualBounds.Right, visualBounds.Top + RightPanelCollapsedH);
                bool visHov = _visualPanelHeaderBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                float indW = FUIRenderer.MeasureText("-", 13f);
                FUIRenderer.DrawText(canvas, "-", new SKPoint(visualBounds.Right - FUIRenderer.FrameInset - indW, visualBounds.Top + FUIRenderer.FrameInset + 18f),
                    visHov ? FUIColors.TextBright : FUIColors.Active.WithAlpha(100), 13f, true);
                DrawNetworkPanelCollapsed(canvas, networkBounds);
            }
            return;
        }

        // 3-panel accordion: VISUAL + NETWORK + HIDHIDE
        switch (_settingsRightPanelActive)
        {
            case "network":
            {
                float visBottom  = bounds.Top + RightPanelCollapsedH;
                float hidHideTop = bounds.Bottom - RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, visBottom);
                var networkBounds = new SKRect(bounds.Left, visBottom + gap, bounds.Right, hidHideTop - gap);
                var hidHideBounds = new SKRect(bounds.Left, hidHideTop, bounds.Right, bounds.Bottom);
                DrawVisualPanelCollapsed(canvas, visualBounds);
                DrawNetworkSettingsPanel(canvas, networkBounds, frameInset);
                DrawHidHidePanelCollapsed(canvas, hidHideBounds);
                break;
            }
            case "hidhide":
            {
                float visBottom  = bounds.Top + RightPanelCollapsedH;
                float netBottom  = visBottom + gap + RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, visBottom);
                var networkBounds = new SKRect(bounds.Left, visBottom + gap, bounds.Right, netBottom);
                var hidHideBounds = new SKRect(bounds.Left, netBottom + gap, bounds.Right, bounds.Bottom);
                DrawVisualPanelCollapsed(canvas, visualBounds);
                DrawNetworkPanelCollapsed(canvas, networkBounds);
                DrawHidHideSettingsPanel(canvas, hidHideBounds, frameInset);
                break;
            }
            default: // "visual"
            {
                float netTop     = bounds.Bottom - gap - RightPanelCollapsedH - gap - RightPanelCollapsedH;
                float hidHideTop = bounds.Bottom - RightPanelCollapsedH;
                var visualBounds  = new SKRect(bounds.Left, bounds.Top, bounds.Right, netTop - gap);
                var networkBounds = new SKRect(bounds.Left, netTop, bounds.Right, hidHideTop - gap);
                var hidHideBounds = new SKRect(bounds.Left, hidHideTop, bounds.Right, bounds.Bottom);
                DrawVisualSettingsSubPanel(canvas, visualBounds, frameInset);
                _visualPanelHeaderBounds = new SKRect(visualBounds.Left, visualBounds.Top, visualBounds.Right, visualBounds.Top + RightPanelCollapsedH);
                bool visHov = _visualPanelHeaderBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                float indW = FUIRenderer.MeasureText("-", 13f);
                FUIRenderer.DrawText(canvas, "-", new SKPoint(visualBounds.Right - FUIRenderer.FrameInset - indW, visualBounds.Top + FUIRenderer.FrameInset + 18f),
                    visHov ? FUIColors.TextBright : FUIColors.Active.WithAlpha(100), 13f, true);
                DrawNetworkPanelCollapsed(canvas, networkBounds);
                DrawHidHidePanelCollapsed(canvas, hidHideBounds);
                break;
            }
        }
    }

    private void DrawVisualPanelCollapsed(SKCanvas canvas, SKRect bounds)
    {
        // Clear theme button bounds so stale rects don't receive clicks
        Array.Clear(_themeButtonBounds, 0, _themeButtonBounds.Length);

        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        bool hovered = bounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        _visualPanelHeaderBounds = bounds;
        FUIWidgets.DrawPanelTitle(canvas, m.LeftMargin, m.RightMargin, ref y, "VISUAL");
        float indW = FUIRenderer.MeasureText("+", 13f);
        FUIRenderer.DrawText(canvas, "+", new SKPoint(m.RightMargin - indW, y - 18f),
            hovered ? FUIColors.TextBright : FUIColors.Active.WithAlpha(180), 13f, true);
    }

    private void DrawNetworkPanelCollapsed(SKCanvas canvas, SKRect bounds)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        bool hovered = bounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        _networkPanelHeaderBounds = bounds;
        FUIWidgets.DrawPanelTitle(canvas, m.LeftMargin, m.RightMargin, ref y, "NETWORK");
        float indW = FUIRenderer.MeasureText("+", 13f);
        FUIRenderer.DrawText(canvas, "+", new SKPoint(m.RightMargin - indW, y - 18f),
            hovered ? FUIColors.TextBright : FUIColors.Active.WithAlpha(180), 13f, true);
    }

    private void DrawHidHidePanelCollapsed(SKCanvas canvas, SKRect bounds)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        bool hovered = bounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        _hidHidePanelHeaderBounds = bounds;
        FUIWidgets.DrawPanelTitle(canvas, m.LeftMargin, m.RightMargin, ref y, "HIDHIDE");
        float indW = FUIRenderer.MeasureText("+", 13f);
        FUIRenderer.DrawText(canvas, "+", new SKPoint(m.RightMargin - indW, y - 18f),
            hovered ? FUIColors.TextBright : FUIColors.Active.WithAlpha(180), 13f, true);
    }

    private void DrawHidHideSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        const float toggleW = 48f;
        const float rowH = 26f;
        const float sectionGap = 10f;

        canvas.Save();
        canvas.ClipRect(bounds.Inset(2f, 2f));

        _hidHidePanelHeaderBounds = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + RightPanelCollapsedH);
        bool headerHovered = _hidHidePanelHeaderBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "HIDHIDE");
        float indW = FUIRenderer.MeasureText("-", 13f);
        FUIRenderer.DrawText(canvas, "-", new SKPoint(rightMargin - indW, y - 18f),
            headerHovered ? FUIColors.TextBright : FUIColors.Active.WithAlpha(100), 13f, true);

        bool available = _ctx.HidHide?.IsAvailable() ?? false;

        // Kick off version check once per panel open
        if (!_hidHideVersionChecked && _ctx.HidHide is not null)
        {
            _hidHideVersionChecked = true;
            _hidHideInstalledVersion = _ctx.HidHide.GetInstalledVersion();
            _ = _ctx.HidHide.GetLatestReleaseAsync().ContinueWith(t =>
            {
                if (!t.IsFaulted && t.Result is not null)
                {
                    _hidHideLatestVersion = t.Result.Version;
                    _hidHideInstallerUrl  = t.Result.InstallerUrl;
                    _ctx.InvalidateCanvas();
                }
            }, TaskScheduler.Default);
        }

        if (!available)
        {
            _hidHideCloakingToggleBounds = SKRect.Empty;
            _hidHideInverseToggleBounds  = SKRect.Empty;
            FUIRenderer.DrawText(canvas, "HidHide not installed.",
                new SKPoint(leftMargin, y + rowH / 2f + 4f), FUIColors.Warning, 13f);
            y += rowH + sectionGap;
            // Show download button
            DrawHidHideDownloadButton(canvas, leftMargin, rightMargin, ref y, rowH,
                _hidHideLatestVersion is not null ? $"Download v{_hidHideLatestVersion}" : "Download HidHide");
            canvas.Restore();
            return;
        }

        // Version row
        if (_hidHideInstalledVersion is not null)
        {
            FUIRenderer.DrawText(canvas, $"v{_hidHideInstalledVersion}",
                new SKPoint(leftMargin, y + rowH / 2f + 4f), FUIColors.TextDim, 12f);
            y += rowH;

            bool updateAvailable = IsHidHideUpdateAvailable();
            if (updateAvailable)
            {
                DrawHidHideDownloadButton(canvas, leftMargin, rightMargin, ref y, rowH,
                    $"Update to v{_hidHideLatestVersion}");
                y += sectionGap;
            }
            else
            {
                _hidHideUpdateButtonBounds = SKRect.Empty;
                y += sectionGap;
            }
        }

        // CLOAKING toggle
        FUIRenderer.DrawTextTruncated(canvas, "Cloaking",
            new SKPoint(leftMargin, y + rowH / 2f + 4f), rightMargin - leftMargin - toggleW - 8f, FUIColors.TextPrimary, 13f);
        _hidHideCloakingToggleBounds = new SKRect(rightMargin - toggleW, y, rightMargin, y + rowH);
        FUIWidgets.DrawToggleSwitch(canvas, _hidHideCloakingToggleBounds, _cloakingT, _ctx.MousePosition);
        y += rowH + sectionGap;

        // INVERSE MODE toggle
        FUIRenderer.DrawTextTruncated(canvas, "Inverse mode",
            new SKPoint(leftMargin, y + rowH / 2f + 4f), rightMargin - leftMargin - toggleW - 8f, FUIColors.TextPrimary, 13f);
        _hidHideInverseToggleBounds = new SKRect(rightMargin - toggleW, y, rightMargin, y + rowH);
        FUIWidgets.DrawToggleSwitch(canvas, _hidHideInverseToggleBounds, _inverseT, _ctx.MousePosition);
        y += rowH + sectionGap;

        // Description of inverse mode
        var descText = _hidHideInverse
            ? "Whitelisted apps are blocked."
            : "Whitelisted apps can see devices.";
        FUIRenderer.DrawText(canvas, descText, new SKPoint(leftMargin, y + 4f),
            FUIColors.TextDim.WithAlpha(160), 11f);

        canvas.Restore();
    }

    private void DrawHidHideDownloadButton(SKCanvas canvas, float leftMargin, float rightMargin,
        ref float y, float rowH, string idleLabel)
    {
        _hidHideUpdateButtonBounds = new SKRect(leftMargin, y, rightMargin, y + rowH);

        string label;
        FUIRenderer.ButtonState state;
        switch (_hidHideInstallPhase)
        {
            case HidHideInstallPhase.Downloading:
                label = $"Downloading... {_hidHideDownloadProgress}%";
                state = FUIRenderer.ButtonState.Disabled;
                break;
            case HidHideInstallPhase.Launching:
                label = "Installing...";
                state = FUIRenderer.ButtonState.Disabled;
                break;
            case HidHideInstallPhase.Error:
                label = "Download failed — retry";
                state = _hidHideUpdateButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y)
                    ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                break;
            default:
                label = idleLabel;
                state = _hidHideUpdateButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y)
                    ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                break;
        }

        FUIRenderer.DrawButton(canvas, _hidHideUpdateButtonBounds, label, state);
        y += rowH;
    }

    public void Dispose()
    {
        _hidHideDownloadCts?.Cancel();
        _hidHideDownloadCts?.Dispose();
        _hidHideDownloadCts = null;
        GC.SuppressFinalize(this);
    }

    private bool IsHidHideUpdateAvailable()
    {
        if (_hidHideInstalledVersion is null || _hidHideLatestVersion is null)
            return false;
        if (Version.TryParse(_hidHideInstalledVersion, out var installed) &&
            Version.TryParse(_hidHideLatestVersion, out var latest))
            return latest > installed;
        return false;
    }

    private void StartHidHideInstall()
    {
        string? url = _hidHideInstallerUrl;
        if (string.IsNullOrEmpty(url) || _ctx.HidHide is null)
            return;

        _hidHideInstallPhase = HidHideInstallPhase.Downloading;
        _hidHideDownloadProgress = 0;
        _ctx.InvalidateCanvas();

        _hidHideDownloadCts?.Cancel();
        _hidHideDownloadCts = new CancellationTokenSource();
        var ct = _hidHideDownloadCts.Token;

        var progress = new Progress<int>(pct =>
        {
            _hidHideDownloadProgress = pct;
            _ctx.InvalidateCanvas();
        });

        _ = _ctx.HidHide.DownloadInstallerAsync(url, progress, ct).ContinueWith(t =>
        {
            if (t.IsCanceled)
            {
                _hidHideInstallPhase = HidHideInstallPhase.Idle;
                _ctx.InvalidateCanvas();
                return;
            }

            string? installerPath = t.IsFaulted ? null : t.Result;
            if (installerPath is null)
            {
                _hidHideInstallPhase = HidHideInstallPhase.Error;
                _ctx.InvalidateCanvas();
                return;
            }

            _hidHideInstallPhase = HidHideInstallPhase.Launching;
            _ctx.InvalidateCanvas();

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true  // triggers UAC elevation if needed
            });
        }, TaskScheduler.Default);
    }

    private void DrawNetworkSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        float contentWidth = m.ContentWidth;
        const float rowH = 26f;      // info rows
        const float btnH = 24f;      // action button height
        const float toggleW = 48f;
        const float sectionGap = 8f; // gap between logical groups

        // Clip content to prevent overflow when panel is compressed
        canvas.Save();
        canvas.ClipRect(bounds.Inset(2f, 2f));

        _networkPanelHeaderBounds = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + RightPanelCollapsedH);
        bool headerHovered = _networkPanelHeaderBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "NETWORK");
        float indW = FUIRenderer.MeasureText("-", 13f);
        FUIRenderer.DrawText(canvas, "-", new SKPoint(rightMargin - indW, y - 18f),
            headerHovered ? FUIColors.TextBright : FUIColors.Active.WithAlpha(100), 13f, true);

        // Machine name + port
        string machineName = string.IsNullOrEmpty(_ctx.AppSettings.NetworkMachineName)
            ? Environment.MachineName
            : _ctx.AppSettings.NetworkMachineName;
        FUIRenderer.DrawTextTruncated(canvas, $"Machine: {machineName}  ·  Port: {_ctx.AppSettings.NetworkListenPort}",
            new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, FUIColors.TextDim, 13f);
        y += rowH + sectionGap;

        // Role selector  [TX]  [RX]
        float roleBtnW = 60f;
        float roleBtnGap = 6f;
        var currentRole = _ctx.AppSettings.NetworkRole;
        FUIRenderer.DrawTextTruncated(canvas, "Role", new SKPoint(leftMargin, y + btnH / 2f + 4f),
            FUIRenderer.MeasureText("Role", 13f) + 4f, FUIColors.TextDim, 13f);
        float rolePairX = rightMargin - (roleBtnW * 2 + roleBtnGap);
        for (int ri = 0; ri < 2; ri++)
        {
            float rx = rolePairX + ri * (roleBtnW + roleBtnGap);
            _netRoleButtonBounds[ri] = new SKRect(rx, y, rx + roleBtnW, y + btnH);
            bool isActive = (ri == 0 && currentRole == NetworkRole.Master)
                         || (ri == 1 && currentRole == NetworkRole.Client);
            bool hov = _netRoleButtonBounds[ri].Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIWidgets.DrawToggleButton(canvas, _netRoleButtonBounds[ri], new[] { "TX", "RX" }[ri], isActive, hov, 13f);
        }
        y += btnH + sectionGap * 1.5f;

        // ── Master-specific UI ─────────────────────────────────────────
        if (currentRole == NetworkRole.Master)
        {
            string code = _ctx.AppSettings.NetworkMasterCode;
            FUIRenderer.DrawTextTruncated(canvas, $"Your code:  {code}",
                new SKPoint(leftMargin, y + btnH / 2f + 4f), contentWidth - 110f, FUIColors.TextBright, 13f);
            _netRegenerateBounds = new SKRect(rightMargin - 100f, y, rightMargin, y + btnH);
            bool regenHov = _netRegenerateBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIRenderer.DrawButton(canvas, _netRegenerateBounds, "REGENERATE",
                regenHov ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            y += btnH + sectionGap;

            var peers = _ctx.NetworkDiscovery?.KnownPeers.Values.ToList() ?? [];
            var netMode = _ctx.NetworkInput?.Mode ?? NetworkInputMode.Local;
            bool isConnecting = _ctx.IsNetworkConnecting;
            string? connectedIp = _ctx.ConnectedPeerIp;
            _peerActionBounds.Clear();

            if (peers.Count > 0)
            {
                for (int pi = 0; pi < peers.Count; pi++)
                {
                    var p = peers[pi];
                    bool thisConnected = netMode == NetworkInputMode.Remote && connectedIp == p.IpAddress;
                    var pColor = thisConnected ? FUIColors.Active
                               : p.IsStale    ? FUIColors.TextDim : FUIColors.TextPrimary;
                    string peerLabel = $"  {p.MachineName}  {p.IpAddress}";

                    float tglY = y + (rowH - btnH) / 2f;
                    var toggleRect = new SKRect(rightMargin - toggleW, tglY, rightMargin, tglY + btnH);
                    _peerActionBounds.Add((toggleRect, p.IpAddress));

                    bool rowHov = toggleRect.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y)
                        || new SKRect(leftMargin, y, rightMargin - toggleW - 6f, y + rowH)
                            .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                    if (rowHov)
                    {
                        using var hpaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(12));
                        canvas.DrawRect(new SKRect(leftMargin, y, rightMargin, y + rowH), hpaint);
                    }
                    FUIRenderer.DrawTextTruncated(canvas, peerLabel, new SKPoint(leftMargin, y + rowH / 2f + 4f),
                        contentWidth - toggleW - 8f, pColor, 13f);
                    float knobT = _peerToggleT.TryGetValue(p.IpAddress, out float t) ? t : (thisConnected ? 1f : 0f);
                    FUIWidgets.DrawToggleSwitch(canvas, toggleRect, knobT, _ctx.MousePosition);
                    y += rowH + 8f;
                }
            }
            else
            {
                FUIRenderer.DrawTextTruncated(canvas, "No clients discovered yet",
                    new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, FUIColors.TextDim, 13f);
                y += rowH;
            }

            bool isConnected = netMode == NetworkInputMode.Remote;
            string statusText = isConnecting ? "Connecting..." : isConnected ? "Connected — sending" : "Not connected";
            var statusColor = isConnected ? FUIColors.Active : isConnecting ? FUIColors.Warning : FUIColors.TextDim;
            y += sectionGap;
            FUIRenderer.DrawTextTruncated(canvas, $"Status:  {statusText}",
                new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, statusColor, 13f);
        }
        // ── Client-specific UI ─────────────────────────────────────────
        else if (currentRole == NetworkRole.Client)
        {
            var trusted = _ctx.AppSettings.TrustedMaster;
            var netMode2 = _ctx.NetworkInput?.Mode ?? NetworkInputMode.Local;
            bool isReceiving = netMode2 == NetworkInputMode.Receiving;

            if (trusted is not null)
            {
                FUIRenderer.DrawTextTruncated(canvas, $"Trusted:  {trusted.MachineName}",
                    new SKPoint(leftMargin, y + btnH / 2f + 4f), contentWidth - 100f, FUIColors.TextPrimary, 13f);
                _netForgetBounds = new SKRect(rightMargin - 90f, y, rightMargin, y + btnH);
                bool forgetHov = _netForgetBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                FUIRenderer.DrawButton(canvas, _netForgetBounds, "FORGET",
                    forgetHov ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal, isDanger: true);
                y += btnH + sectionGap;
            }
            else
            {
                FUIRenderer.DrawTextTruncated(canvas, "No trusted master — waiting for connection",
                    new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, FUIColors.TextDim, 13f);
                y += rowH + sectionGap;
            }

            _netDisconnectBounds = new SKRect(rightMargin - 110f, y, rightMargin, y + btnH);
            bool discHov = isReceiving && _netDisconnectBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIRenderer.DrawButton(canvas, _netDisconnectBounds, "DISCONNECT",
                !isReceiving ? FUIRenderer.ButtonState.Disabled :
                discHov ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal, isDanger: true);
            y += btnH + sectionGap;

            string statusText2 = isReceiving ? $"Connected to {trusted?.MachineName ?? "master"}" : "Waiting for master";
            var statusColor2 = isReceiving ? FUIColors.Active : FUIColors.TextDim;
            FUIRenderer.DrawTextTruncated(canvas, $"Status:  {statusText2}",
                new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, statusColor2, 13f);
        }
        else
        {
            if (_ctx.NetworkDiscovery is not null)
            {
                int peerCount = _ctx.NetworkDiscovery.KnownPeers.Count;
                var peerColor = peerCount > 0 ? FUIColors.Active : FUIColors.TextDim;
                FUIRenderer.DrawTextTruncated(canvas, $"Peers visible: {peerCount}",
                    new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, peerColor, 13f);
            }
        }

        canvas.Restore();
    }

    #endregion

    #region Click Handling

    private void HandleSettingsTabClick(SKPoint pt)
    {
        // Profile name edit icon
        if (_profileNameEditBounds != SKRect.Empty && _profileNameEditBounds.Contains(pt))
        {
            RenameActiveProfile();
            return;
        }

        // Profile name box click (whole box)
        if (_profileNameBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            RenameActiveProfile();
            return;
        }

        // Profile action buttons
        if (_newProfileButtonBounds.Contains(pt))
        {
            _ctx.CreateNewProfilePrompt?.Invoke();
            return;
        }
        if (_duplicateProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.DuplicateActiveProfile?.Invoke();
            return;
        }
        if (_importProfileButtonBounds.Contains(pt))
        {
            _ctx.ImportProfile?.Invoke();
            return;
        }
        if (_exportProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.ExportActiveProfile?.Invoke();
            return;
        }
        if (_deleteProfileButtonBounds != default && _deleteProfileButtonBounds.Contains(pt) &&
            _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.DeleteActiveProfile?.Invoke();
            return;
        }

        // Theme button clicks
        FUITheme[] themes = {
            FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice,
            FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo,
            FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI
        };
        for (int i = 0; i < _themeButtonBounds.Length && i < themes.Length; i++)
        {
            if (_themeButtonBounds[i].Contains(pt))
            {
                FUIColors.SetTheme(themes[i]);
                _ctx.ThemeService.Theme = themes[i];
                _ctx.BackgroundDirty = true;
                _ctx.TrayIcon.RefreshThemeColors();
                _ctx.InvalidateCanvas();
                return;
            }
        }

        // Auto-load toggle
        if (_autoLoadToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.AutoLoadLastProfile = !_ctx.AppSettings.AutoLoadLastProfile;
            _ctx.InvalidateCanvas();
            return;
        }

        // Close to tray toggle
        if (_closeToTrayToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.CloseToTray = !_ctx.AppSettings.CloseToTray;
            _ctx.InvalidateCanvas();
            return;
        }

        // Client Only Mode toggle
        if (_clientOnlyToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.ClientOnlyMode = !_ctx.AppSettings.ClientOnlyMode;
            _ctx.MarkDirty();
            return;
        }

        // Check for updates automatically toggle
        if (_checkUpdatesToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.AutoCheckUpdates = !_ctx.AppSettings.AutoCheckUpdates;
            _ctx.InvalidateCanvas();
            return;
        }

        // Network forwarding toggle
        if (_networkEnabledToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.NetworkEnabled = !_ctx.AppSettings.NetworkEnabled;
            if (_ctx.AppSettings.NetworkEnabled)
            {
                _ctx.StartNetworking?.Invoke();
                // Switch right panel to NETWORK so user can configure immediately
                _settingsRightPanelActive = "network";
                _ctx.AppSettings.SettingsRightPanel = "network";
            }
            else
            {
                _ctx.ShutdownNetworking?.Invoke();
            }
            _ctx.InvalidateCanvas();
            return;
        }

        // Right panel accordion header clicks
        if (!_visualPanelHeaderBounds.IsEmpty && _visualPanelHeaderBounds.Contains(pt))
        {
            _settingsRightPanelActive = "visual";
            _ctx.AppSettings.SettingsRightPanel = "visual";
            _ctx.InvalidateCanvas();
            return;
        }
        if (!_networkPanelHeaderBounds.IsEmpty && _networkPanelHeaderBounds.Contains(pt))
        {
            _settingsRightPanelActive = "network";
            _ctx.AppSettings.SettingsRightPanel = "network";
            _ctx.InvalidateCanvas();
            return;
        }
        if (!_hidHidePanelHeaderBounds.IsEmpty && _hidHidePanelHeaderBounds.Contains(pt))
        {
            _settingsRightPanelActive = "hidhide";
            _ctx.AppSettings.SettingsRightPanel = "hidhide";
            _hidHideStateLoaded = false;    // force toggle state reload
            _hidHideVersionChecked = false; // force version re-check
            _hidHideInstallPhase = HidHideInstallPhase.Idle;
            _ctx.InvalidateCanvas();
            return;
        }

        // HidHide panel clicks
        if (_settingsRightPanelActive == "hidhide")
        {
            if (!_hidHideUpdateButtonBounds.IsEmpty && _hidHideUpdateButtonBounds.Contains(pt)
                && _hidHideInstallPhase is HidHideInstallPhase.Idle or HidHideInstallPhase.Error)
            {
                StartHidHideInstall();
                return;
            }
        }

        if (_settingsRightPanelActive == "hidhide" && _ctx.HidHide?.IsAvailable() == true)
        {
            if (!_hidHideCloakingToggleBounds.IsEmpty && _hidHideCloakingToggleBounds.Contains(pt))
            {
                _hidHideCloaking = !_hidHideCloaking;
                if (_hidHideCloaking)
                    _ctx.HidHide.EnableCloaking();
                else
                    _ctx.HidHide.DisableCloaking();
                _ctx.InvalidateCanvas();
                return;
            }
            if (!_hidHideInverseToggleBounds.IsEmpty && _hidHideInverseToggleBounds.Contains(pt))
            {
                _hidHideInverse = !_hidHideInverse;
                if (_hidHideInverse)
                    _ctx.HidHide.EnableInverseMode();
                else
                    _ctx.HidHide.DisableInverseMode();
                _ctx.InvalidateCanvas();
                return;
            }
        }

        // Role selector buttons (only when networking is enabled)
        if (_ctx.AppSettings.NetworkEnabled)
        {
            var roleValues = new[] { NetworkRole.Master, NetworkRole.Client };
            for (int ri = 0; ri < _netRoleButtonBounds.Length; ri++)
            {
                if (_netRoleButtonBounds[ri].Contains(pt))
                {
                    _ctx.AppSettings.NetworkRole = roleValues[ri];
                    _ctx.InvalidateCanvas();
                    return;
                }
            }

            // Master-specific buttons
            if (_ctx.AppSettings.NetworkRole == NetworkRole.Master)
            {
                if (_netRegenerateBounds.Contains(pt))
                {
                    // Clear the code — getter in ApplicationSettingsService auto-generates a new one
                    _ctx.AppSettings.NetworkMasterCode = "";
                    _ctx.InvalidateCanvas();
                    return;
                }

                // Per-peer connection toggles
                foreach (var (toggleRect, peerIp) in _peerActionBounds)
                {
                    if (!toggleRect.Contains(pt)) continue;

                    bool thisConnected = _ctx.NetworkMode == NetworkInputMode.Remote
                                     && _ctx.ConnectedPeerIp == peerIp;
                    if (thisConnected)
                    {
                        Log.Debug("[UI] Peer toggle OFF | peer={Ip}", peerIp);
                        _ = _ctx.NetworkDisconnectAsync?.Invoke();
                        _ctx.InvalidateCanvas();
                        return;
                    }
                    if (!_ctx.IsNetworkConnecting && _ctx.ConnectToPeerAsync is not null)
                    {
                        var peer = _ctx.NetworkDiscovery?.KnownPeers.Values
                            .FirstOrDefault(p => p.IpAddress == peerIp);
                        if (peer is not null)
                        {
                            Log.Debug("[UI] Peer toggle ON | peer={Ip} prevConnected={Prev}", peerIp, _ctx.ConnectedPeerIp ?? "none");
                            _connectingPeerIp = peerIp;
                            _ = _ctx.ConnectToPeerAsync(peer);
                        }
                        _ctx.InvalidateCanvas();
                        return;
                    }
                }
            }
            // Client-specific buttons
            else if (_ctx.AppSettings.NetworkRole == NetworkRole.Client)
            {
                if (_netForgetBounds.Contains(pt))
                {
                    _ctx.AppSettings.TrustedMaster = null;
                    _ctx.InvalidateCanvas();
                    return;
                }

                if (_netDisconnectBounds.Contains(pt) &&
                    _ctx.NetworkInput?.Mode == NetworkInputMode.Receiving)
                {
                    // Route through MainForm.SwitchToLocalAsync — clears client state
                    _ = _ctx.NetworkDisconnectAsync?.Invoke();
                    _ctx.InvalidateCanvas();
                    return;
                }
            }
        }


        // Background slider clicks (start drag)
        if (_bgGridSliderBounds.Contains(pt)) { _draggingBgSlider = "grid"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgGlowSliderBounds.Contains(pt)) { _draggingBgSlider = "glow"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgNoiseSliderBounds.Contains(pt)) { _draggingBgSlider = "noise"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgScanlineSliderBounds.Contains(pt)) { _draggingBgSlider = "scanline"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgVignetteSliderBounds.Contains(pt)) { _draggingBgSlider = "vignette"; UpdateBgSliderFromPoint(pt.X); return; }

        // Support panel clicks
        if (_bmacButtonBounds.Contains(pt))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://buymeacoffee.com/nerosilentr",
                UseShellExecute = true
            });
            return;
        }
        if (_referralCopyButtonBounds.Contains(pt))
        {
            Clipboard.SetText("STAR-RBDQ-Z4JG");
            return;
        }
        if (_referralLinkButtonBounds.Contains(pt))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.robertsspaceindustries.com/enlist?referral=STAR-RBDQ-Z4JG",
                UseShellExecute = true
            });
            return;
        }

        // Driver setup button click
        if (!_driverSetupButtonBounds.IsEmpty && _driverSetupButtonBounds.Contains(pt))
        {
            _ctx.OpenDriverSetup?.Invoke();
            return;
        }

        // Check for updates button click
        if (!_checkButtonBounds.IsEmpty && _checkButtonBounds.Contains(pt))
        {
            var status = _ctx.UpdateService.Status;
            if (status is UpdateStatus.Unknown or UpdateStatus.UpToDate or UpdateStatus.Error)
            {
                _ = _ctx.UpdateService.CheckAsync().ContinueWith(
                    _ => _ctx.OwnerForm.Invoke(_ctx.InvalidateCanvas),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                _ctx.InvalidateCanvas();
            }
            return;
        }

        // Download update button click (inside UpdateAvailable banner)
        if (!_downloadButtonBounds.IsEmpty && _downloadButtonBounds.Contains(pt))
        {
            _ = _ctx.UpdateService.DownloadAsync().ContinueWith(
                _ => _ctx.OwnerForm.Invoke(_ctx.InvalidateCanvas),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _ctx.InvalidateCanvas();
            return;
        }

        // Apply update button click (inside ReadyToApply banner)
        if (!_applyButtonBounds.IsEmpty && _applyButtonBounds.Contains(pt))
        {
            _ = _ctx.UpdateService.ApplyUpdateAsync();
            return;
        }
    }

    private void RenameActiveProfile()
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var newName = FUIInputDialog.Show(_ctx.OwnerForm, "Rename Configuration", "Configuration Name:", profile.Name);
        if (newName is null || newName == profile.Name)
            return;

        profile.Name = newName;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.RefreshProfileList();
        _ctx.InvalidateCanvas();
    }

    #endregion

    #region Slider Handling

    private void UpdateBgSliderFromPoint(float x)
    {
        SKRect bounds = _draggingBgSlider switch
        {
            "grid" => _bgGridSliderBounds,
            "glow" => _bgGlowSliderBounds,
            "noise" => _bgNoiseSliderBounds,
            "scanline" => _bgScanlineSliderBounds,
            "vignette" => _bgVignetteSliderBounds,
            _ => default
        };

        if (bounds == default) return;

        float ratio = Math.Clamp((x - bounds.Left) / bounds.Width, 0f, 1f);
        int value = (int)(ratio * 100);
        var bg = _ctx.Background;

        switch (_draggingBgSlider)
        {
            case "grid": bg.GridStrength = value; break;
            case "glow": bg.GlowIntensity = value; break;
            case "noise": bg.NoiseIntensity = value; break;
            case "scanline": bg.ScanlineIntensity = value; break;
            case "vignette": bg.VignetteStrength = value; break;
        }

        _ctx.BackgroundDirty = true;
        _ctx.InvalidateCanvas();
    }

    private void SaveBackgroundSettings()
    {
        var bg = _ctx.Background;
        _ctx.ThemeService.SaveBackgroundSettings(
            bg.GridStrength, bg.GlowIntensity, bg.NoiseIntensity,
            bg.ScanlineIntensity, bg.VignetteStrength);
    }

    #endregion
}
