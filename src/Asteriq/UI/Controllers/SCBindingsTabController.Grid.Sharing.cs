using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void UpdateSharedCells()
    {
        _conflicts.SharedCells.Clear();
        foreach (var binding in _scExportProfile.Bindings)
        {
            foreach (var shared in binding.SharedWith)
            {
                string key = $"{binding.ActionKey}|{shared.VJoySlot}";
                _conflicts.SharedCells[key] = (binding.VJoyDevice, binding.InputName, shared.InputName);
            }
        }
    }

    /// <summary>
    /// Reverts all shared binding reroutes from the current SCExportProfile, restoring
    /// the mapping profile's button outputs to their original values.
    /// Must be called BEFORE switching to a new SCExportProfile.
    /// </summary>
    private void RevertSharedBindingReroutes()
    {
        var mappingProfile = _ctx.ProfileManager.ActiveProfile;
        if (mappingProfile is null) return;

        bool changed = false;
        foreach (var binding in _scExportProfile.Bindings)
        {
            foreach (var shared in binding.SharedWith)
            {
                int secondaryButton = ParseButtonIndex(shared.InputName);
                if (secondaryButton < 0) continue;

                foreach (var mappingId in shared.ReroutedMappingIds)
                {
                    var mapping = mappingProfile.ButtonMappings.FirstOrDefault(m => m.Id == mappingId);
                    if (mapping is not null)
                    {
                        mapping.Output.VJoyDevice = shared.VJoySlot;
                        mapping.Output.Index = secondaryButton;
                        changed = true;
                    }
                }
            }
        }

        if (changed)
        {
            _ctx.OnMappingsChanged();
        }
    }

    /// <summary>
    /// Re-applies shared binding reroutes from the active SCExportProfile to the active
    /// MappingProfile. Called after switching control profiles or Asteriq configurations
    /// so that shares are profile-independent.
    /// </summary>
    /// <param name="notifyEngine">If true, calls OnMappingsChanged to hot-reload the engine.
    /// Pass false during initialization when the engine isn't running yet.</param>
    private void ReapplySharedBindingReroutes(bool notifyEngine = true)
    {
        var mappingProfile = _ctx.ProfileManager.ActiveProfile;
        if (mappingProfile is null) return;

        var reroutes = BuildRerouteMap();
        if (reroutes.Count == 0) return;

        bool changed = ApplyReroutesToMappingProfile(mappingProfile, reroutes);

        if (changed && notifyEngine)
            _ctx.OnMappingsChanged();
    }

    // Collect unique reroute specs: (fromVJoy, fromButton) -> (toVJoy, toButton).
    // Skip primaries with modifiers — those shares rely on per-<rebind> XML emission
    // rather than reroute, so the secondary input reaches SC as its own vJoy button.
    private Dictionary<(uint fromVJoy, int fromButton), (uint toVJoy, int toButton)> BuildRerouteMap()
    {
        var reroutes = new Dictionary<(uint fromVJoy, int fromButton), (uint toVJoy, int toButton)>();
        foreach (var binding in _scExportProfile.Bindings)
        {
            if (binding.Modifiers.Count > 0) continue;
            int primaryButton = ParseButtonIndex(binding.InputName);
            if (primaryButton < 0) continue;

            foreach (var shared in binding.SharedWith)
            {
                int secondaryButton = ParseButtonIndex(shared.InputName);
                if (secondaryButton < 0) continue;
                reroutes[(shared.VJoySlot, secondaryButton)] = (binding.VJoyDevice, primaryButton);
            }
        }
        return reroutes;
    }

    private bool ApplyReroutesToMappingProfile(
        MappingProfile mappingProfile,
        IReadOnlyDictionary<(uint fromVJoy, int fromButton), (uint toVJoy, int toButton)> reroutes)
    {
        bool changed = false;
        foreach (var binding in _scExportProfile.Bindings)
        {
            if (binding.Modifiers.Count > 0) continue;
            foreach (var shared in binding.SharedWith)
            {
                if (ApplyRerouteForSharedEntry(mappingProfile, binding, shared, reroutes))
                    changed = true;
            }
        }
        return changed;
    }

    private static bool ApplyRerouteForSharedEntry(
        MappingProfile mappingProfile,
        SCActionBinding binding,
        SCSharedInput shared,
        IReadOnlyDictionary<(uint fromVJoy, int fromButton), (uint toVJoy, int toButton)> reroutes)
    {
        int secondaryButton = ParseButtonIndex(shared.InputName);
        if (secondaryButton < 0) return false;

        var newIds = RerouteMatchingMappings(mappingProfile, binding, shared.VJoySlot, secondaryButton);
        bool changed = newIds.Count > 0;

        // Fallback: another SharedWith entry on the same binding already applied the
        // reroute (PerformShare only reroutes once, other entries share the IDs).
        // Copy the IDs from the reroute spec so RevertSharedBindingReroutes can find them.
        if (newIds.Count == 0 && reroutes.TryGetValue((shared.VJoySlot, secondaryButton), out var target))
            newIds = CollectAlreadyReroutedIds(mappingProfile, target, shared.ReroutedMappingIds);

        if (newIds.Count > 0)
            shared.ReroutedMappingIds = newIds;

        return changed;
    }

    private static List<Guid> RerouteMatchingMappings(
        MappingProfile mappingProfile,
        SCActionBinding binding,
        uint fromVJoy,
        int fromButton)
    {
        var newIds = new List<Guid>();
        int primaryButton = ParseButtonIndex(binding.InputName);
        if (primaryButton < 0) return newIds;

        foreach (var bm in mappingProfile.ButtonMappings)
        {
            if (bm.Output.Type == OutputType.VJoyButton &&
                bm.Output.VJoyDevice == fromVJoy &&
                bm.Output.Index == fromButton)
            {
                bm.Output.VJoyDevice = binding.VJoyDevice;
                bm.Output.Index = primaryButton;
                newIds.Add(bm.Id);
            }
        }
        return newIds;
    }

    private static List<Guid> CollectAlreadyReroutedIds(
        MappingProfile mappingProfile,
        (uint toVJoy, int toButton) target,
        IReadOnlyCollection<Guid> existingIds)
    {
        var newIds = new List<Guid>();
        foreach (var bm in mappingProfile.ButtonMappings)
        {
            if (bm.Output.Type == OutputType.VJoyButton &&
                bm.Output.VJoyDevice == target.toVJoy &&
                bm.Output.Index == target.toButton &&
                (existingIds.Count == 0 || existingIds.Contains(bm.Id)))
            {
                newIds.Add(bm.Id);
            }
        }
        return newIds;
    }

    /// <summary>
    /// Parses a vJoy button index (0-based) from an SC input name like "button33".
    /// Returns -1 if the input is not a button or cannot be parsed.
    /// </summary>
    private void PerformShare(SCActionBinding primaryBinding, uint secondaryVJoySlot, string secondaryInputName)
    {
        // A primary can only have one shared secondary per target slot. If one already exists,
        // remove it first so reassigning a shared cell replaces the input cleanly instead of
        // stacking a second SharedWith entry (which would export two duplicate <rebind>s).
        var stale = primaryBinding.SharedWith.Where(s => s.VJoySlot == secondaryVJoySlot).ToList();
        primaryBinding.SharedWith.RemoveAll(s => s.VJoySlot == secondaryVJoySlot);

        int secondaryButtonIndex = ParseButtonIndex(secondaryInputName);
        int primaryButtonIndex = ParseButtonIndex(primaryBinding.InputName);
        bool primaryHasModifiers = primaryBinding.Modifiers.Count > 0;

        var reroutedIds = new List<Guid>();
        var mappingProfile = _ctx.ProfileManager.ActiveProfile;
        bool mappingChanged = false;

        // Restore any runtime reroute the stale entry applied, so the secondary button is
        // not stuck pointing at the old primary in the mapping profile.
        if (mappingProfile is not null)
        {
            foreach (var s in stale)
            {
                int staleSecondaryButton = ParseButtonIndex(s.InputName);
                if (staleSecondaryButton < 0) continue;
                foreach (var mappingId in s.ReroutedMappingIds)
                {
                    var m = mappingProfile.ButtonMappings.FirstOrDefault(b => b.Id == mappingId);
                    if (m is not null)
                    {
                        m.Output.VJoyDevice = s.VJoySlot;
                        m.Output.Index = staleSecondaryButton;
                        mappingChanged = true;
                    }
                }
            }
        }

        if (!primaryHasModifiers && mappingProfile is not null && secondaryButtonIndex >= 0 && primaryButtonIndex >= 0)
        {
            foreach (var bm in mappingProfile.ButtonMappings)
            {
                if (bm.Output.Type == OutputType.VJoyButton &&
                    bm.Output.VJoyDevice == secondaryVJoySlot &&
                    bm.Output.Index == secondaryButtonIndex)
                {
                    bm.Output.VJoyDevice = primaryBinding.VJoyDevice;
                    bm.Output.Index = primaryButtonIndex;
                    reroutedIds.Add(bm.Id);
                    mappingChanged = true;
                }
            }

            if (reroutedIds.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SCBindings] Rerouted {reroutedIds.Count} mapping(s) from vJoy{secondaryVJoySlot}/{secondaryInputName} â†’ vJoy{primaryBinding.VJoyDevice}/{primaryBinding.InputName}");
            }
        }

        if (mappingChanged)
            _ctx.OnMappingsChanged();

        primaryBinding.SharedWith.Add(new SCSharedInput
        {
            VJoySlot = secondaryVJoySlot,
            InputName = secondaryInputName,
            ReroutedMappingIds = reroutedIds
        });
    }

    /// <summary>
    /// Handles a click on a shared cell â€” shows the Unshare dialog and performs the operation if confirmed.
    /// </summary>
    private void HandleSharedCellClick(SCAction action, SCGridColumn col)
    {
        string sharedKey = $"{action.Key}|{col.VJoyDeviceId}";
        if (!_conflicts.SharedCells.TryGetValue(sharedKey, out var linkInfo))
            return;

        var primaryBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
            b.ActionMap == action.ActionMap &&
            b.ActionName == action.ActionName &&
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId is null &&
            b.VJoyDevice == linkInfo.PrimaryVJoyDevice);

        if (primaryBinding is null) return;

        var sharedEntry = primaryBinding.SharedWith.FirstOrDefault(s => s.VJoySlot == col.VJoyDeviceId);
        if (sharedEntry is null) return;

        int primaryInstance = _scExportProfile.GetSCInstance(primaryBinding.VJoyDevice);
        string primaryInputDisplay = SCBindingsRenderer.FormatInputName(primaryBinding.InputName);
        string secondaryInputDisplay = SCBindingsRenderer.FormatInputName(sharedEntry.InputName);

        // Find ALL bindings that share the same primary binding (same vJoy device, input,
        // AND modifier set) and are shared to this secondary slot. Bindings with the same
        // button but different modifiers are independent actions â€” don't bundle them.
        var allSharedBindings = _scExportProfile.Bindings
            .Where(b => b.DeviceType == SCDeviceType.Joystick &&
                b.PhysicalDeviceId is null &&
                b.VJoyDevice == linkInfo.PrimaryVJoyDevice &&
                b.InputName == primaryBinding.InputName &&
                b.Modifiers.SequenceEqual(primaryBinding.Modifiers) &&
                b.SharedWith.Any(s => s.VJoySlot == col.VJoyDeviceId))
            .ToList();

        string message;
        if (allSharedBindings.Count > 1)
        {
            var otherNames = allSharedBindings
                .Where(b => !(b.ActionMap == action.ActionMap && b.ActionName == action.ActionName))
                .Select(b => SCCategoryMapper.FormatActionName(b.ActionName))
                .Distinct().ToList();
            message = $"{secondaryInputDisplay} on JS{col.SCInstance} is shared with JS{primaryInstance} / {primaryInputDisplay}.\n\n" +
                $"Unsharing will also affect:\n" +
                string.Join("\n", otherNames.Select(n => $"  â€¢ {n}")) +
                $"\n\nUnshare all {allSharedBindings.Count} binding(s)?";
        }
        else
        {
            message = $"{secondaryInputDisplay} on JS{col.SCInstance} is shared with JS{primaryInstance} / {primaryInputDisplay}.\n\nUnshare this binding?";
        }

        using var dialog = new FUIConfirmDialog(
            "Shared Binding",
            message,
            "Unshare", "Cancel");

        if (dialog.ShowDialog(_ctx.OwnerForm) != DialogResult.Yes)
            return;

        // Unshare the primary binding (restores the vJoy mapping reroute)
        PerformUnshare(primaryBinding, sharedEntry, col.SCInstance, primaryInstance, primaryInputDisplay, secondaryInputDisplay);

        // Remove SharedWith entries from all other affected bindings
        foreach (var binding in allSharedBindings)
        {
            if (binding == primaryBinding) continue;
            var entry = binding.SharedWith.FirstOrDefault(s => s.VJoySlot == col.VJoyDeviceId);
            if (entry is not null)
                binding.SharedWith.Remove(entry);
        }

        // Save is already done inside PerformUnshare, but we modified additional bindings
        _scExportProfileService?.SaveProfile(_scExportProfile);
        UpdateSharedCells();
        _ctx.MarkDirty();
    }

    /// <summary>
    /// Performs the Unshare operation: restores rerouted mappings and removes the SharedWith entry.
    /// </summary>
    private void PerformUnshare(
        SCActionBinding primaryBinding,
        Models.SCSharedInput sharedEntry,
        int secondarySCInstance,
        int primarySCInstance,
        string primaryInputDisplay,
        string secondaryInputDisplay)
    {
        int secondaryButtonIndex = ParseButtonIndex(sharedEntry.InputName);

        // Restore rerouted mappings back to their original output
        var mappingProfile = _ctx.ProfileManager.ActiveProfile;
        if (mappingProfile is not null && secondaryButtonIndex >= 0 && sharedEntry.ReroutedMappingIds.Count > 0)
        {
            foreach (var mappingId in sharedEntry.ReroutedMappingIds)
            {
                var mapping = mappingProfile.ButtonMappings.FirstOrDefault(m => m.Id == mappingId);
                if (mapping is not null)
                {
                    mapping.Output.VJoyDevice = sharedEntry.VJoySlot;
                    mapping.Output.Index = secondaryButtonIndex;
                }
            }
            _ctx.OnMappingsChanged();
            System.Diagnostics.Debug.WriteLine(
                $"[SCBindings] Restored {sharedEntry.ReroutedMappingIds.Count} mapping(s) back to vJoy{sharedEntry.VJoySlot}/{sharedEntry.InputName}");
        }

        primaryBinding.SharedWith.Remove(sharedEntry);

        _scExportProfileService?.SaveProfile(_scExportProfile);
        UpdateSharedCells();
        UpdateConflictingBindings();
        _ctx.MarkDirty();

        SetStatus($"Unshared JS{secondarySCInstance} {secondaryInputDisplay} from JS{primarySCInstance} {primaryInputDisplay}");
    }

}