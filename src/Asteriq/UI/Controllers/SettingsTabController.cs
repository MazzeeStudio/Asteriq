using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SettingsTabController : ITabController, IDisposable
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
    private SKRect _launchOnStartToggleBounds;
    private SKRect _autoStartFwdToggleBounds;
    private SKRect _openMinimizedToggleBounds;
    private string? _draggingBgSlider;

    // Support panel button bounds
    private SKRect _bmacButtonBounds;
    private SKRect _referralCopyButtonBounds;
    private SKRect _referralLinkButtonBounds;
    private long _referralCopiedTicks; // 0 = not showing

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
    private SKRect _netMatchMasterBounds; // RX (client) role only — match master's vJoy config
    private bool _matchingMasterInProgress;
    // Per-peer toggle bounds (TX role) — rebuilt every frame; one entry per discovered peer
    private readonly List<(SKRect Toggle, string IpAddress)> _peerActionBounds = [];
    private readonly Dictionary<string, float> _peerToggleT = new();  // per-peer knob animation (0=off, 0.5=connecting, 1=on)
    private string? _connectingPeerIp;     // IP of the peer a connect attempt is currently targeting

    // Version / update button bounds
    private SKRect _checkButtonBounds;
    private SKRect _downloadButtonBounds;
    private SKRect _applyButtonBounds;

    // Update channel segmented control
    private SKRect[] _updateChannelSegBounds = Array.Empty<SKRect>();
    private static readonly string[] UpdateChannelLabels = ["STABLE", "NIGHTLY"];

    // Toggle knob animation positions (0 = off, 1 = on); initialized from current settings
    private ToggleAnim _autoLoadT;
    private ToggleAnim _closeToTrayT;
    private ToggleAnim _clientOnlyT;
    private ToggleAnim _networkEnabledT;
    private ToggleAnim _checkUpdatesT;
    private ToggleAnim _launchOnStartT;
    private ToggleAnim _autoStartFwdT;
    private ToggleAnim _openMinimizedT;
    private const float ToggleLerpSpeed = 0.14f;  // per 60Hz tick ≈ ~120 ms transition

    // Settings right panel accordion state — PanelVisual | PanelNetwork | PanelHidHide
    // Right panel accordion identifiers
    private const string PanelVisual = "visual";
    private const string PanelNetwork = "network";
    private const string PanelHidHide = "hidhide";

    private string _settingsRightPanelActive;
    private SKRect _visualPanelHeaderBounds;
    private SKRect _networkPanelHeaderBounds;
    private SKRect _hidHidePanelHeaderBounds;
    // Panel expansion weights for animation (0=collapsed, 1=expanded). Lerp toward active panel.
    private float _visualExpandW = 1f;
    private float _networkExpandW;
    private float _hidHideExpandW;

    // HidHide panel state
    private SKRect _hidHideCloakingToggleBounds;
    private SKRect _hidHideInverseToggleBounds;
    private SKRect _hidHideUpdateButtonBounds;
    private bool _hidHideCloaking;
    private bool _hidHideInverse;
    private bool _hidHideStateLoaded;
    private bool _hidHideStateLoading;
    private ToggleAnim _cloakingT;
    private ToggleAnim _inverseT;
    private string? _hidHideInstalledVersion;
    private string? _hidHideLatestVersion;
    private string? _hidHideInstallerUrl;
    private bool _hidHideVersionChecked;
    private HidHideInstallPhase _hidHideInstallPhase;
    private int _hidHideDownloadProgress;
    private CancellationTokenSource? _hidHideDownloadCts;
    // Per-device HidHide toggle rows in the Settings panel
    private readonly List<(SKRect Bounds, PhysicalDeviceInfo Device)> _hidHideDeviceToggleBounds = new();
    // Cached hidden device-instance paths, populated by the same background load as the toggles
    private List<string>? _hidHideHiddenPaths;
    // Transient status line for HidHide operations (e.g. failed toggle), 4-second display
    private string? _hidHideStatusMessage;
    private DateTime _hidHideStatusTime;

    private enum HidHideInstallPhase { Idle, Downloading, Launching, Error }

    private struct ToggleAnim
    {
        public float T;

        public void Update(bool target, float speed = 0.14f)
        {
            float goal = target ? 1f : 0f;
            T += (goal - T) * speed;
            if (MathF.Abs(T - goal) < 0.001f) T = goal;
        }
    }

    public SettingsTabController(TabContext ctx)
    {
        _ctx = ctx;
        // Snap to current settings on first construction — no animation on startup
        _autoLoadT      = new ToggleAnim { T = ctx.AppSettings.AutoLoadLastProfile ? 1f : 0f };
        _closeToTrayT   = new ToggleAnim { T = ctx.AppSettings.CloseToTray ? 1f : 0f };
        _clientOnlyT    = new ToggleAnim { T = ctx.AppSettings.ClientOnlyMode ? 1f : 0f };
        _networkEnabledT = new ToggleAnim { T = ctx.AppSettings.NetworkEnabled ? 1f : 0f };
        _checkUpdatesT  = new ToggleAnim { T = ctx.AppSettings.AutoCheckUpdates ? 1f : 0f };
        _launchOnStartT = new ToggleAnim { T = ctx.AppSettings.LaunchOnWindowsStart ? 1f : 0f };
        _autoStartFwdT  = new ToggleAnim { T = ctx.AppSettings.AutoStartForwarding ? 1f : 0f };
        _openMinimizedT = new ToggleAnim { T = ctx.AppSettings.OpenMinimized ? 1f : 0f };
        var savedPanel = ctx.AppSettings.SettingsRightPanel ?? PanelVisual;
        _settingsRightPanelActive = (savedPanel == PanelNetwork && !ctx.AppSettings.NetworkEnabled) ? PanelVisual : savedPanel;
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
        bool hasProfile = _ctx.ProfileManager.ActiveProfile is not null;
        _ctx.OwnerForm.Cursor = Cursors.Default;

        // Profile name box
        if (_profileNameBounds.Contains(pt) && hasProfile)
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }

        // Profile action buttons
        if (_newProfileButtonBounds.Contains(pt) ||
            (_duplicateProfileButtonBounds.Contains(pt) && hasProfile) ||
            _importProfileButtonBounds.Contains(pt) ||
            (_exportProfileButtonBounds.Contains(pt) && hasProfile) ||
            (_deleteProfileButtonBounds.HitTest(pt) && hasProfile))
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }

        // Theme buttons
        foreach (var b in _themeButtonBounds)
        {
            if (b.HitTest(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }

        // Toggles
        if (_autoLoadToggleBounds.Contains(pt) || _closeToTrayToggleBounds.Contains(pt) ||
            _clientOnlyToggleBounds.Contains(pt) || _checkUpdatesToggleBounds.Contains(pt) ||
            _networkEnabledToggleBounds.Contains(pt) ||
            _launchOnStartToggleBounds.Contains(pt) || _autoStartFwdToggleBounds.Contains(pt) ||
            _openMinimizedToggleBounds.Contains(pt))
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }

        // Background sliders
        if (_bgGridSliderBounds.Contains(pt) || _bgGlowSliderBounds.Contains(pt) ||
            _bgNoiseSliderBounds.Contains(pt) || _bgScanlineSliderBounds.Contains(pt) ||
            _bgVignetteSliderBounds.Contains(pt))
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }

        // Support panel buttons
        if (_bmacButtonBounds.Contains(pt) || _referralCopyButtonBounds.Contains(pt) ||
            _referralLinkButtonBounds.Contains(pt))
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }

        // Driver setup button
        if (_driverSetupButtonBounds.HitTest(pt))
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }

        // Network buttons
        foreach (var b in _netRoleButtonBounds)
        {
            if (b.HitTest(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }
        if (_netRegenerateBounds.HitTest(pt) || _netDisconnectBounds.HitTest(pt) || _netForgetBounds.HitTest(pt) ||
            _netMatchMasterBounds.HitTest(pt))
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }

        foreach (var (toggleRect, _) in _peerActionBounds)
        {
            if (toggleRect.Contains(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }

        // HidHide panel toggles
        if (_hidHideCloakingToggleBounds.HitTest(pt) || _hidHideInverseToggleBounds.HitTest(pt) ||
            _hidHideUpdateButtonBounds.HitTest(pt))
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }

        // Update channel segments
        foreach (var seg in _updateChannelSegBounds)
        {
            if (seg.Contains(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }

        // Update section buttons
        if (_checkButtonBounds.HitTest(pt) || _downloadButtonBounds.HitTest(pt) || _applyButtonBounds.HitTest(pt))
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }

        // Right panel accordion headers
        if (_visualPanelHeaderBounds.HitTest(pt) || _networkPanelHeaderBounds.HitTest(pt) ||
            _hidHidePanelHeaderBounds.HitTest(pt))
        { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
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
        // Toggle animations
        var settings = _ctx.AppSettings;
        _autoLoadT.Update(settings.AutoLoadLastProfile);
        _closeToTrayT.Update(settings.CloseToTray);
        _clientOnlyT.Update(settings.ClientOnlyMode);
        _networkEnabledT.Update(settings.NetworkEnabled);
        _checkUpdatesT.Update(settings.AutoCheckUpdates);
        _launchOnStartT.Update(settings.LaunchOnWindowsStart);
        _autoStartFwdT.Update(settings.AutoStartForwarding);
        _openMinimizedT.Update(settings.OpenMinimized);
        _cloakingT.Update(_hidHideCloaking);
        _inverseT.Update(_hidHideInverse);

        UpdatePeerToggleAnimations();
        UpdateAccordionAnimations();
    }

    private void UpdatePeerToggleAnimations()
    {
        var peersSource = _ctx.NetworkDiscovery?.KnownPeers.Values;
        if (peersSource is null) return;

        bool netConnected = _ctx.NetworkMode == NetworkInputMode.Remote;
        bool netConnecting = _ctx.IsNetworkConnecting;
        string? connectedIp = _ctx.ConnectedPeerIp;
        string? connectingIp = _connectingPeerIp;

        var visibleIps = new HashSet<string>();

        foreach (var p in peersSource)
        {
            visibleIps.Add(p.IpAddress);

            float target = 0f;
            if (netConnected && connectedIp == p.IpAddress)
                target = 1f;
            else if (netConnecting && connectingIp == p.IpAddress)
                target = 0.5f;

            if (!_peerToggleT.TryGetValue(p.IpAddress, out float current))
                current = target;

            _peerToggleT[p.IpAddress] = LerpSnap(current, target, ToggleLerpSpeed);
        }

        // Remove stale entries
        foreach (var ip in _peerToggleT.Keys.ToList())
        {
            if (!visibleIps.Contains(ip))
                _peerToggleT.Remove(ip);
        }
    }

    private void UpdateAccordionAnimations()
    {
        float vTarget = _settingsRightPanelActive == PanelVisual ? 1f : 0f;
        float nTarget = _settingsRightPanelActive == PanelNetwork ? 1f : 0f;
        float hTarget = _settingsRightPanelActive == PanelHidHide ? 1f : 0f;

        const float panelLerp = 0.18f;
        _visualExpandW  = LerpSnap(_visualExpandW, vTarget, panelLerp);
        _networkExpandW = LerpSnap(_networkExpandW, nTarget, panelLerp);
        _hidHideExpandW = LerpSnap(_hidHideExpandW, hTarget, panelLerp);

        if (MathF.Abs(_visualExpandW - vTarget) > 0.0001f
            || MathF.Abs(_networkExpandW - nTarget) > 0.0001f
            || MathF.Abs(_hidHideExpandW - hTarget) > 0.0001f)
            _ctx.MarkDirty();
    }

    /// <summary>
    /// Lerp towards target, snapping when close enough to avoid endless tiny deltas.
    /// </summary>
    private static float LerpSnap(float current, float target, float speed, float epsilon = 0.001f)
    {
        float delta = target - current;
        return MathF.Abs(delta) < epsilon ? target : current + delta * speed;
    }

    public void OnActivated() { }
    public void OnDeactivated() { }

    public bool IsDraggingSlider => _draggingBgSlider is not null;


    public void Dispose()
    {
        _hidHideDownloadCts?.Cancel();
        _hidHideDownloadCts?.Dispose();
        _hidHideDownloadCts = null;
        GC.SuppressFinalize(this);
    }

}