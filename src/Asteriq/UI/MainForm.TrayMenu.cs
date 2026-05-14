using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Asteriq.UI.Controllers;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Svg.Skia;

namespace Asteriq.UI;

public partial class MainForm
{
    private void InitializeTrayMenu()
    {
        var textColor = SkiaColorToGdi(FUIColors.TextPrimary);
        var dimColor  = SkiaColorToGdi(FUIColors.TextDim);

        var menu = new ContextMenuStrip
        {
            Renderer         = new DarkContextMenuRenderer(),
            Font             = new Font("Segoe UI", 9.5f),
            ImageScalingSize = new Size(TrayIconSize, TrayIconSize),
            Padding          = new Padding(0, 4, 0, 4),  // top/bottom breathing room
        };

        // Windows 11: rounded corners, no DWM border highlight
        menu.Opened += (s, e) =>
        {
            if (s is ContextMenuStrip strip && strip.IsHandleCreated)
            {
                int pref    = DWMWCP_ROUND;
                int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
                DwmSetWindowAttribute(strip.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref,     sizeof(int));
                DwmSetWindowAttribute(strip.Handle, DWMWA_BORDER_COLOR,             ref noBorder, sizeof(int));
            }
        };

        // â”€â”€ Open Asteriq â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var openItem = new ToolStripMenuItem("Open Asteriq")
        {
            Image   = TrayMenuIcons.Open(TrayIconSize, textColor),
            Padding = TrayItemPadding,
        };
        openItem.Click += (s, e) => ShowAndActivateWindow();
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        // â”€â”€ Start / Stop Forwarding â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var forwardingItem = new ToolStripMenuItem("Start Forwarding")
        {
            Image   = TrayMenuIcons.Play(TrayIconSize, dimColor),
            Name    = "forwarding",
            Padding = TrayItemPadding,
        };
        forwardingItem.Click += (s, e) =>
        {
            if (_isForwarding)
                StopForwarding();
            else
                StartForwarding();
            UpdateTrayMenu();
        };
        menu.Items.Add(forwardingItem);

        // â”€â”€ Connect to... (only when networking is available) â”€â”€â”€â”€â”€
        bool hasNetwork = _networkDiscovery is not NullNetworkDiscoveryService;
        if (hasNetwork)
        {
            var connectItem = new ToolStripMenuItem("Connect to...")
            {
                Image   = TrayMenuIcons.Network(TrayIconSize, dimColor),
                Name    = "connect",
                Padding = TrayItemPadding,
            };
            menu.Items.Add(connectItem);

            // Rebuild peer submenu each time the menu opens; also re-evaluate visibility
            menu.Opening += (s, e) =>
            {
                bool isClientRole = _appSettings.NetworkEnabled && _appSettings.NetworkRole == Models.NetworkRole.Client;
                connectItem.Visible = !isClientRole && _networkMode != NetworkInputMode.Receiving;
                if (_trayIcon.ContextMenuStrip?.Items["forwarding"] is ToolStripMenuItem fwd)
                    fwd.Visible = !isClientRole;
                if (connectItem.Visible) RefreshPeerSubmenu(connectItem);
            };
        }

        menu.Items.Add(new ToolStripSeparator());

        // â”€â”€ Exit â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var exitItem = new ToolStripMenuItem("Exit Asteriq")
        {
            Image   = TrayMenuIcons.Exit(TrayIconSize, dimColor),
            Padding = TrayItemPadding,
        };
        exitItem.Click += (s, e) =>
        {
            _forceClose = true;
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (s, e) => ShowAndActivateWindow();
    }

    private void RefreshPeerSubmenu(ToolStripMenuItem connectItem)
    {
        connectItem.DropDownItems.Clear();

        var peers     = _networkDiscovery.KnownPeers.Values.ToList();
        var textColor = SkiaColorToGdi(FUIColors.TextPrimary);
        var dimColor  = SkiaColorToGdi(FUIColors.TextDim);

        if (peers.Count == 0)
        {
            connectItem.DropDownItems.Add(new ToolStripMenuItem("No peers discovered") { Enabled = false });
            return;
        }

        foreach (var peer in peers)
        {
            bool isConnected = _tabContext.ConnectedPeerIp == peer.IpAddress;
            string label     = isConnected
                ? $"Disconnect from {peer.MachineName}"
                : peer.MachineName;

            // CA2000: ToolStripMenuItem ownership transferred to DropDownItems which manages disposal
#pragma warning disable CA2000
            var peerItem = new ToolStripMenuItem(label)
            {
                Image = TrayMenuIcons.Monitor(TrayIconSize, isConnected ? SkiaColorToGdi(FUIColors.Active) : dimColor),
            };
#pragma warning restore CA2000

            var captured = peer;
            peerItem.Click += (s, e) =>
            {
                if (_tabContext.ConnectedPeerIp == captured.IpAddress)
                    _ = SwitchToLocalAsync();
                else
                    _ = ConnectAsMasterAsync(captured);
            };

            connectItem.DropDownItems.Add(peerItem);
        }
    }

    // (StartForwarding / StopForwarding moved to MainForm.Networking.cs)

    private void UpdateTrayMenu()
    {
        if (_trayIcon.ContextMenuStrip is null) return;

        var forwardingItem = _trayIcon.ContextMenuStrip.Items["forwarding"] as ToolStripMenuItem;
        if (forwardingItem is null) return;

        // Read from TabContext which is always up-to-date â€” _isForwarding may not
        // have been synced yet when controllers invoke this via delegate.
        if (_tabContext.IsForwarding)
        {
            forwardingItem.Text  = "Stop Forwarding";
            forwardingItem.Image = TrayMenuIcons.Stop(TrayIconSize, SkiaColorToGdi(FUIColors.Active));
        }
        else
        {
            forwardingItem.Text  = "Start Forwarding";
            forwardingItem.Image = TrayMenuIcons.Play(TrayIconSize, SkiaColorToGdi(FUIColors.TextDim));
        }

        // Connect to... and forwarding are irrelevant in Rx role
        bool isClientRole = _appSettings.NetworkEnabled && _appSettings.NetworkRole == Models.NetworkRole.Client;
        forwardingItem.Visible = !isClientRole;
        if (_trayIcon.ContextMenuStrip.Items["connect"] is ToolStripMenuItem connectItem)
            connectItem.Visible = !isClientRole && _networkMode != NetworkInputMode.Receiving;
    }

    private static Color SkiaColorToGdi(SkiaSharp.SKColor c) => Color.FromArgb(c.Red, c.Green, c.Blue);


    // (Networking methods moved to MainForm.Networking.cs)


    private bool _forceClose;

}