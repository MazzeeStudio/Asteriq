using Asteriq.DirectInput;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    /// <summary>
    /// Assigns a vJoy slot to a specific SC joystick instance, swapping the displaced slot
    /// to take the previous instance of the selected slot.
    /// </summary>
    private void AssignDeviceOrderSlot(int scInst, uint newVJoySlotId)
    {
        var existingSlots = _ctx.VJoyDevices.Where(v => v.Exists).ToList();
        if (existingSlots.Count == 0) return;

        // Find the vJoy slot that currently owns scInst
        uint? prevSlotId = null;
        foreach (var slot in existingSlots)
        {
            if (_scExportProfile.GetSCInstance(slot.Id) == scInst)
            {
                prevSlotId = slot.Id;
                break;
            }
        }

        if (prevSlotId == newVJoySlotId) return; // No change

        // The displaced slot gets the SC instance that newVJoySlot currently has
        int newSlotCurrentInst = _scExportProfile.GetSCInstance(newVJoySlotId);

        _scExportProfile.SetSCInstance(newVJoySlotId, scInst);
        if (prevSlotId.HasValue)
            _scExportProfile.SetSCInstance(prevSlotId.Value, newSlotCurrentInst);

        SaveAndRefreshAfterDeviceOrderChange();
    }

    /// <summary>
    /// Runs DirectInput-based auto-detection and updates VJoyToSCInstance in the active profile.
    /// </summary>
    private void RunDeviceOrderAutoDetect()
    {
        if (_directInputService is null) return;

        try
        {
            var diDevices = _directInputService.EnumerateDevices();
            var vjoySlots = _ctx.VJoyDevices.Where(v => v.Exists);
            var mapping = VJoyDirectInputOrderService.DetectVJoyDiOrder(vjoySlots, diDevices);

            foreach (var (vjoyId, scInstance) in mapping)
                _scExportProfile.SetSCInstance(vjoyId, scInstance);

            SaveAndRefreshAfterDeviceOrderChange();
            SetStatus("Device order auto-detected");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            SetStatus("Auto-detect failed: DirectInput unavailable", SCStatusKind.Error);
        }
    }

    private void SaveAndRefreshAfterDeviceOrderChange()
    {
        if (!string.IsNullOrEmpty(_scExportProfile.ProfileName))
            _scExportProfileService.SaveProfile(_scExportProfile);

        UpdateConflictingBindings();
        _ctx.MarkDirty();
    }
}
