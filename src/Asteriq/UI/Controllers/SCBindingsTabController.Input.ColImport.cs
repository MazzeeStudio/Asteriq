using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void LoadColImportSourceColumns()
    {
        _colImport.LoadedProfile = null;
        _colImport.SourceColumns.Clear();
        _colImport.ColumnIndex = -1;

        if (_colImport.ProfileIndex < 0) return;

        var (savedProfiles, xmlFiles) = GetColImportSources();
        int savedCount = savedProfiles.Count;

        if (_colImport.ProfileIndex < savedCount)
        {
            // Load saved Asteriq JSON profile
            _colImport.LoadedProfile = _scExportProfileService.LoadProfile(savedProfiles[_colImport.ProfileIndex].ProfileName);
        }
        else
        {
            // Load from SC XML mapping file
            int xmlIdx = _colImport.ProfileIndex - savedCount;
            if (xmlIdx < xmlFiles.Count)
            {
                var xmlFile = xmlFiles[xmlIdx];
                var importResult = SCXmlExportService.ImportFromFile(xmlFile.FilePath);
                if (importResult.Success)
                {
                    _colImport.LoadedProfile = new SCExportProfile { ProfileName = xmlFile.DisplayName };
                    foreach (var binding in importResult.Bindings)
                        _colImport.LoadedProfile.Bindings.Add(binding);
                    // SC XML bindings use the instance number directly as VJoyDevice. Union with
                    // DeclaredJoystickInstances so devices declared via <joystick instance="N"/>
                    // but without any rebind lines still surface as columns (they typically
                    // carry only shared-binding secondaries, exported from the JSON side).
                    var usedInstances = importResult.Bindings
                        .Where(b => b.DeviceType == SCDeviceType.Joystick)
                        .Select(b => (int)b.VJoyDevice)
                        .Concat(importResult.DeclaredJoystickInstances)
                        .Distinct();
                    foreach (var inst in usedInstances)
                        _colImport.LoadedProfile.SetSCInstance((uint)inst, inst);
                }
            }
        }

        if (_colImport.LoadedProfile is null) return;

        // Enumerate every joystick instance the source profile knows about â€” both the vJoy
        // slots persisted in VJoyToSCInstance and the physical devices in
        // PhysicalDeviceToSCInstance. We intentionally list instances that have no bindings
        // yet so the dropdown matches the profile's declared column structure (same basis as
        // GetSCGridColumns uses for the current grid).
        var profile = _colImport.LoadedProfile;
        var entries = new List<(string Label, uint VJoyDeviceId, string? PhysicalDeviceId, int SCInstance)>();

        foreach (var kv in profile.VJoyToSCInstance)
            entries.Add(($"JS{kv.Value}", kv.Key, null, kv.Value));

        foreach (var kv in profile.PhysicalDeviceToSCInstance)
            entries.Add(($"JS{kv.Value}", 0u, kv.Key, kv.Value));

        _colImport.SourceColumns = entries
            .OrderBy(e => e.SCInstance)
            .ThenBy(e => e.Label, StringComparer.Ordinal)
            .Select(e => (e.Label, e.VJoyDeviceId, e.PhysicalDeviceId))
            .ToList();

        if (_colImport.SourceColumns.Count == 1)
            _colImport.ColumnIndex = 0;
    }

    private void ExecuteImportFromProfile()
    {
        if (_grid.Columns is null || _colImport.HighlightedColumn < 0 || _colImport.HighlightedColumn >= _grid.Columns.Count)
            return;
        if (_colImport.LoadedProfile is null || _colImport.ColumnIndex < 0 || _colImport.ColumnIndex >= _colImport.SourceColumns.Count)
            return;

        var targetCol = _grid.Columns[_colImport.HighlightedColumn];
        var (sourceLabel, sourceVJoyId, sourcePhysicalId) = _colImport.SourceColumns[_colImport.ColumnIndex];
        string sourceName = _colImport.LoadedProfile.ProfileName;

        // Split the source column's bindings into two groups so we can preserve the shared
        // relationship on the target side instead of flattening everything into primaries.
        //  - sourcePrimaries: bindings whose primary input IS the source column.
        //  - sourceSharedRefs: bindings primary on a DIFFERENT column whose SharedWith list
        //    references the source column (these should become shared on the target too).
        var sourcePrimaries = new List<SCActionBinding>();
        var sourceSharedRefs = new List<(SCActionBinding SourcePrimary, string SharedInputName)>();
        foreach (var b in _colImport.LoadedProfile.Bindings)
        {
            if (b.DeviceType != SCDeviceType.Joystick) continue;

            if (sourcePhysicalId is not null)
            {
                if (b.PhysicalDeviceId == sourcePhysicalId)
                    sourcePrimaries.Add(b);
                continue;
            }

            if (b.PhysicalDeviceId is null && b.VJoyDevice == sourceVJoyId)
            {
                sourcePrimaries.Add(b);
                continue;
            }

            var shared = b.SharedWith.FirstOrDefault(s => s.VJoySlot == sourceVJoyId);
            if (shared is not null)
                sourceSharedRefs.Add((b, shared.InputName));
        }

        int totalToImport = sourcePrimaries.Count + sourceSharedRefs.Count;
        if (totalToImport == 0)
        {
            DeselectColumn();
            return;
        }

        int existingPrimaries = _scExportProfile.Bindings.Count(b =>
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId is null &&
            _scExportProfile.GetSCInstance(b.VJoyDevice) == targetCol.SCInstance);
        int existingSharedRefs = _scExportProfile.Bindings.Sum(b =>
            b.SharedWith.Count(s => s.VJoySlot == targetCol.VJoyDeviceId));
        int existingCount = existingPrimaries + existingSharedRefs;

        string replaceNote = existingCount > 0
            ? $"\n\nThis will replace {existingCount} existing binding{(existingCount == 1 ? "" : "s")} on JS{targetCol.SCInstance}."
            : string.Empty;

        int btnCount = sourcePrimaries.Count(b => b.InputType == SCInputType.Button) + sourceSharedRefs.Count;
        int axisCount = sourcePrimaries.Count(b => b.InputType == SCInputType.Axis);
        int hatCount = sourcePrimaries.Count(b => b.InputType == SCInputType.Hat);
        var detailParts = new List<string>();
        if (btnCount > 0)  detailParts.Add($"  {btnCount} button binding{(btnCount == 1 ? "" : "s")}");
        if (axisCount > 0) detailParts.Add($"  {axisCount} axis binding{(axisCount == 1 ? "" : "s")}");
        if (hatCount > 0)  detailParts.Add($"  {hatCount} hat binding{(hatCount == 1 ? "" : "s")}");
        if (sourceSharedRefs.Count > 0)
            detailParts.Add($"  {sourceSharedRefs.Count} will come in as shared");

        string message = $"Import {totalToImport} binding{(totalToImport == 1 ? "" : "s")} from '{sourceName}' ({sourceLabel}) into JS{targetCol.SCInstance}?{replaceNote}";
        bool confirmed = FUIMessageBox.ShowDestructiveConfirm(
            _ctx.OwnerForm,
            message,
            "Import Bindings",
            "IMPORT",
            detailParts.Count > 0 ? detailParts.ToArray() : null);

        if (!confirmed) return;

        // Clear existing state for the target column â€” both primary bindings and shared
        // secondary references â€” so the import is a clean replace.
        _scExportProfile.Bindings.RemoveAll(b =>
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId is null &&
            _scExportProfile.GetSCInstance(b.VJoyDevice) == targetCol.SCInstance);
        foreach (var b in _scExportProfile.Bindings)
            b.SharedWith.RemoveAll(s => s.VJoySlot == targetCol.VJoyDeviceId);

        // Primary imports land as primaries on the target column.
        foreach (var b in sourcePrimaries)
        {
            _scExportProfile.Bindings.Add(new SCActionBinding
            {
                ActionMap = b.ActionMap,
                ActionName = b.ActionName,
                DeviceType = b.DeviceType,
                VJoyDevice = targetCol.VJoyDeviceId,
                InputName = b.InputName,
                InputType = b.InputType,
                Inverted = b.Inverted,
                ActivationMode = b.ActivationMode,
                Modifiers = new List<string>(b.Modifiers)
            });
        }

        // Shared refs attach to the matching primary in the target. If the target has no
        // primary for that action yet (user hasn't imported that column), fall back to
        // creating the binding as a primary on the target column so no bindings are lost.
        // The fallback primary is created WITHOUT the source primary's modifiers â€” a share
        // represents a standalone input that fires the action on its own, matching how the
        // secondary would behave if attached to a real primary via SharedWith.
        foreach (var (srcPrimary, sharedInputName) in sourceSharedRefs)
        {
            var targetPrimary = _scExportProfile.Bindings.FirstOrDefault(b =>
                b.DeviceType == SCDeviceType.Joystick &&
                b.PhysicalDeviceId is null &&
                b.ActionMap == srcPrimary.ActionMap &&
                b.ActionName == srcPrimary.ActionName &&
                b.VJoyDevice != targetCol.VJoyDeviceId);

            if (targetPrimary is not null)
            {
                targetPrimary.SharedWith.Add(new SCSharedInput
                {
                    VJoySlot = targetCol.VJoyDeviceId,
                    InputName = sharedInputName
                });
            }
            else
            {
                _scExportProfile.Bindings.Add(new SCActionBinding
                {
                    ActionMap = srcPrimary.ActionMap,
                    ActionName = srcPrimary.ActionName,
                    DeviceType = srcPrimary.DeviceType,
                    VJoyDevice = targetCol.VJoyDeviceId,
                    InputName = sharedInputName,
                    InputType = srcPrimary.InputType,
                    Inverted = srcPrimary.Inverted,
                    ActivationMode = srcPrimary.ActivationMode,
                    Modifiers = new List<string>()
                });
            }
        }

        _scExportProfile.Modified = DateTime.UtcNow;
        _scExportProfileService.SaveProfile(_scExportProfile);
        UpdateConflictingBindings();
        UpdateSharedCells();
        RefreshFilteredActions();
        DeselectColumn();
        _ctx.MarkDirty();
    }
}