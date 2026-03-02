# Network Event Notifications

## Overview

When a network forwarding session is established or terminated, both machines should
receive a brief ambient notification — visible even when the user is in-game — without
requiring focus or a modal dialog inside the Asteriq window.

Windows Toast Notifications satisfy this requirement:
- They appear over fullscreen applications in **borderless windowed** mode (the recommended
  SC display mode) and over the taskbar in any mode
- They are fire-and-forget; no window handle or foreground permission is needed
- They auto-dismiss after a few seconds and queue in Action Center

> **Note:** Toasts do NOT appear over exclusive fullscreen. That is acceptable — SC's
> recommended mode is borderless windowed. Exclusive fullscreen is out of scope.

---

## API Options

### Option A — `Windows.UI.Notifications` (WinRT, .NET 8 built-in)

Available without any extra NuGet package on Windows 10/11 when targeting `net8.0-windows`.

```xml
<!-- Asteriq.csproj — already set -->
<TargetFramework>net8.0-windows10.0.17763.0</TargetFramework>
```

```csharp
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

static void ShowToast(string title, string message)
{
    const string AppId = "Asteriq";          // must match Start Menu shortcut AUMID if registered

    string xml = $"""
        <toast>
          <visual>
            <binding template="ToastGeneric">
              <text>{title}</text>
              <text>{message}</text>
            </binding>
          </visual>
        </toast>
        """;

    var doc = new XmlDocument();
    doc.LoadXml(xml);
    var notif = new ToastNotification(doc);
    ToastNotificationManager.CreateToastNotifier(AppId).Show(notif);
}
```

**Constraint:** `ToastNotificationManager.CreateToastNotifier(appId)` requires the app to
be registered in the Start Menu (a shortcut with an AppUserModelID). Unregistered apps
silently drop toasts on Windows 10. On Windows 11 the restriction is relaxed.

---

### Option B — `Microsoft.Windows.AppNotifications` (Windows App SDK)

Requires the `Microsoft.WindowsAppSDK` NuGet package (≈10 MB). Supports un-packaged
(non-MSIX) apps without a Start Menu registration.

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.5.*" />
```

```csharp
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

// Call once at startup:
AppNotificationManager.Default.Register();

// Then anywhere:
static void ShowToast(string title, string message)
{
    var builder = new AppNotificationBuilder()
        .AddText(title)
        .AddText(message);
    AppNotificationManager.Default.Show(builder.BuildNotification());
}

// Call once at shutdown:
AppNotificationManager.Default.Unregister();
```

This is the **recommended option** for Asteriq because it works without MSIX packaging
and without a Start Menu shortcut.

---

## Notification Events and Messages

| Event | Side | Title | Body |
|-------|------|-------|------|
| TX forwarding started | TX (master) | `Asteriq — Connected` | `Forwarding to {rxHostname}` |
| RX session accepted | RX (slave) | `Asteriq — Connected` | `{txHostname} is now forwarding` |
| TX forwarding stopped (user) | TX | `Asteriq — Disconnected` | `Forwarding stopped` |
| RX session lost (timeout) | RX | `Asteriq — Disconnected` | `Lost connection from {txHostname}` |

`rxHostname` / `txHostname` are already available on `PeerInfo.HostName` (populated by
`NetworkDiscoveryService` from UDP beacon packets).

---

## Integration Points

The two natural call sites in the existing codebase:

| Method | File | Toast to show |
|--------|------|---------------|
| `ConnectAsMasterAsync` — after successful TCP handshake | `MainForm.cs` | TX "Connected" toast |
| `NetworkInputService.RunReceiveLoopAsync` — on first snapshot received | `NetworkInputService.cs` | RX "Connected" toast |
| `SwitchToLocalAsync` | `MainForm.cs` | TX "Disconnected" toast |
| Receive-loop catch/timeout | `NetworkInputService.cs` | RX "Disconnected" toast |

A thin `INotificationService` interface (single `Show(string title, string message)` method)
keeps the call sites decoupled from the WinRT/AppSDK choice and makes unit testing trivial
with a no-op stub.

---

## Implementation Notes

- **Threading:** Both APIs are COM-apartment-safe; they can be called from a background
  `Task` without marshalling to the UI thread.
- **Icon:** Toasts use the app's Start Menu icon by default. With Option B (AppSDK), the
  icon path can be set explicitly via `AppNotificationBuilder.SetAppLogoOverride()`.
- **Sound:** Default toast sound plays automatically. Use
  `<audio silent="true"/>` in the XML (Option A) or
  `.SetAudioUri(new Uri("ms-appx:///"), AppNotificationAudioLooping.None)` (Option B)
  to suppress it if desired.
- **Deduplication:** Guard with a short cooldown (e.g. 5 s) to avoid duplicate toasts if
  the connection is re-established rapidly.
