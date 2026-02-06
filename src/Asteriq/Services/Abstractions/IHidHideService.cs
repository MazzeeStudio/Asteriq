namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for managing HidHide device hiding
/// </summary>
public interface IHidHideService
{
    /// <summary>
    /// Check if HidHide CLI is available
    /// </summary>
    bool IsAvailable();

    /// <summary>
    /// Get all gaming HID devices
    /// </summary>
    List<HidHideDeviceGroup> GetGamingDevices();

    /// <summary>
    /// Get all HID devices
    /// </summary>
    List<HidHideDeviceGroup> GetAllDevices();

    /// <summary>
    /// Get list of hidden device instance paths
    /// </summary>
    List<string> GetHiddenDevices();

    /// <summary>
    /// Hide a device by its instance path
    /// </summary>
    bool HideDevice(string deviceInstancePath);

    /// <summary>
    /// Unhide a device by its instance path
    /// </summary>
    bool UnhideDevice(string deviceInstancePath);

    /// <summary>
    /// Get current cloaking state
    /// </summary>
    bool IsCloakingEnabled();

    /// <summary>
    /// Enable cloaking (hiding becomes active)
    /// </summary>
    bool EnableCloaking();

    /// <summary>
    /// Disable cloaking (all devices visible)
    /// </summary>
    bool DisableCloaking();

    /// <summary>
    /// Check if inverse application cloak is enabled.
    /// In inverse mode, whitelisted apps are BLOCKED from seeing hidden devices.
    /// In normal mode, whitelisted apps CAN see hidden devices.
    /// </summary>
    bool IsInverseMode();

    /// <summary>
    /// Enable inverse application cloak mode
    /// </summary>
    bool EnableInverseMode();

    /// <summary>
    /// Disable inverse application cloak mode
    /// </summary>
    bool DisableInverseMode();

    /// <summary>
    /// Get list of whitelisted application paths
    /// </summary>
    List<string> GetWhitelistedApps();

    /// <summary>
    /// Add an application to the whitelist (can see hidden devices)
    /// </summary>
    bool WhitelistApp(string appPath);

    /// <summary>
    /// Remove an application from the whitelist
    /// </summary>
    bool UnwhitelistApp(string appPath);

    /// <summary>
    /// Ensure Asteriq can see hidden devices based on current mode.
    /// In normal mode: adds to whitelist.
    /// In inverse mode: removes from whitelist.
    /// </summary>
    bool EnsureSelfCanSeeDevices();

    /// <summary>
    /// Ensure Asteriq is whitelisted so it can see hidden devices.
    /// DEPRECATED: Use EnsureSelfCanSeeDevices() which handles inverse mode.
    /// </summary>
    bool EnsureSelfWhitelisted();

    /// <summary>
    /// Hide all gaming devices except vJoy devices
    /// </summary>
    int HideAllPhysicalDevices();

    /// <summary>
    /// Unhide all currently hidden devices
    /// </summary>
    int UnhideAllDevices();
}
