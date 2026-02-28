using System.Xml;
using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    public void Initialize()
    {
        try
        {
            // SC services are now injected via constructor
            // _scInstallationService, _scProfileCacheService, _scSchemaService,
            // _scExportService, and _scExportProfileService are already assigned

            // Ensure vJoy devices are enumerated for SC Bindings columns
            if (_ctx.VJoyDevices.Count == 0 && _ctx.VJoyService is not null)
            {
                _ctx.VJoyDevices = _ctx.VJoyService.EnumerateDevices();
            }

            RefreshSCInstallations();
            RefreshSCExportProfiles();

            // Try to load the last used SC export profile for the current environment.
            // Skip "asteriq" - that was the old auto-generated default name, never user-chosen.
            var currentEnv = CurrentEnvironment;
            var lastProfileName = currentEnv is not null
                ? _ctx.AppSettings.GetLastSCExportProfileForEnvironment(currentEnv)
                : _ctx.AppSettings.LastSCExportProfile;

            if (lastProfileName == "asteriq")
                lastProfileName = null;

            SCExportProfile? loadedProfile = null;
            if (!string.IsNullOrEmpty(lastProfileName) && _ctx.AppSettings.AutoLoadLastSCExportProfile)
            {
                loadedProfile = _scExportProfileService.LoadProfile(lastProfileName);
            }

            if (loadedProfile is not null)
            {
                _scExportProfile = loadedProfile;
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Loaded last SC export for {currentEnv}: {loadedProfile.ProfileName}");
            }
            else
            {
                // No last profile for this environment - load first non-legacy profile
                var firstNamed = _scExportProfiles.FirstOrDefault(p => p.ProfileName != "asteriq");
                if (firstNamed is not null)
                {
                    var first = _scExportProfileService?.LoadProfile(firstNamed.ProfileName);
                    if (first is not null)
                    {
                        _scExportProfile = first;
                        if (currentEnv is not null)
                            _ctx.AppSettings.SetLastSCExportProfileForEnvironment(currentEnv, first.ProfileName);
                    }
                }
                else
                {
                    // Only "asteriq" or no profiles exist - start blank with auto-detected order
                    _scExportProfile = new SCExportProfile();
                    ApplyAutoDetectedDeviceOrder(_scExportProfile);
                }
            }

            // Fill in any vJoy slots that are missing from the loaded profile's mapping
            // (e.g. a new vJoy device was added since the profile was last saved)
            var missingSlots = _ctx.VJoyDevices
                .Where(v => v.Exists && !_scExportProfile.VJoyToSCInstance.ContainsKey(v.Id))
                .ToList();
            if (missingSlots.Count > 0)
                ApplyAutoDetectedDeviceOrder(_scExportProfile);

            // Initial conflict detection
            UpdateConflictingBindings();
            UpdateSharedCells();

            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings initialized, {_scInstallations.Count} installations found");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs DirectInput-based device order detection and applies the result to <paramref name="profile"/>.
    /// Falls back to identity mapping when DirectInput is unavailable or detection fails.
    /// </summary>
    private void ApplyAutoDetectedDeviceOrder(SCExportProfile profile)
    {
        var vjoySlots = _ctx.VJoyDevices.Where(v => v.Exists);

        if (_directInputService is not null)
        {
            try
            {
                var diDevices = _directInputService.EnumerateDevices();
                var mapping = VJoyDirectInputOrderService.DetectVJoyDiOrder(vjoySlots, diDevices);
                foreach (var (vjoyId, scInstance) in mapping)
                    profile.SetSCInstance(vjoyId, scInstance);
                System.Diagnostics.Debug.WriteLine("[SCBindings] Device order auto-detected via DirectInput");
                return;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                System.Diagnostics.Debug.WriteLine($"[SCBindings] DI auto-detect failed, using identity mapping: {ex.Message}");
            }
        }

        // Fallback: identity mapping (slot N → instance N)
        foreach (var v in vjoySlots)
            profile.SetSCInstance(v.Id, (int)v.Id);
    }

    private void RefreshSCInstallations()
    {
        if (_scInstallationService is null) return;

        _scInstallations = _scInstallationService.Installations.ToList();

        // Restore preferred environment selection (e.g. LIVE, PTU)
        var preferredEnv = _ctx.AppSettings.PreferredSCEnvironment;
        if (!string.IsNullOrEmpty(preferredEnv))
        {
            int preferredIndex = _scInstallations.FindIndex(i =>
                string.Equals(i.Environment, preferredEnv, StringComparison.OrdinalIgnoreCase));
            _selectedSCInstallation = preferredIndex >= 0 ? preferredIndex : 0;
        }
        else if (_selectedSCInstallation >= _scInstallations.Count)
        {
            _selectedSCInstallation = 0;
        }

    }

    private void StartSchemaLoad()
    {
        if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count)
            LoadSCSchema(_scInstallations[_selectedSCInstallation]);
    }

    private void LoadSCSchema(SCInstallation installation, bool autoLoadProfileForEnvironment = false, bool applyDefaultsAfterLoad = false)
    {
        if (_scProfileCacheService is null || _scSchemaService is null) return;

        int version = ++_schemaLoadVersion;
        _scLoading = true;
        _scLoadingMessage = "Loading...";
        _scActions = null;
        _scFilteredActions = null;
        _scActionMaps.Clear();
        _scAvailableProfiles = new();      // clear stale profiles from previous installation immediately
        _scProfileDropdownOpen = false;    // close dropdown so it doesn't show stale data
        _scImportDropdownOpen = false;
        _ctx.InvalidateCanvas();

        Task.Run(() =>
        {
            List<SCAction>? actions = null;
            List<string> actionMaps = new();
            List<SCMappingFile> availableProfiles = new();
            string? loadError = null;

            try
            {
                // Check in-memory schema cache first (avoids re-parsing XML on environment switch)
                var cacheKey = installation.GetCacheKey();
                if (s_schemaCache.TryGetValue(cacheKey, out var cachedActions))
                {
                    actions = cachedActions;
                    ReportProgress(version, $"Using cached schema for {installation.Environment}...");
                }
                else
                {
                    ReportProgress(version, "Checking cache...");
                    var profile = _scProfileCacheService.GetOrExtractProfile(installation,
                        msg => ReportProgress(version, msg));

                    if (profile is not null)
                    {
                        ReportProgress(version, "Parsing actions...");
                        actions = _scSchemaService.ParseActions(profile);
                        s_schemaCache[cacheKey] = actions;
                    }
                }

                if (actions is not null)
                {
                    ReportProgress(version, "Building categories...");
                    var joystickActions = _scSchemaService.FilterJoystickActions(actions);
                    actionMaps = SCCategoryMapper.GetSortedCategoriesFromActions(
                        joystickActions.Select(a => (a.ActionMap, a.ActionName))
                    ).ToList();

                    ReportProgress(version, "Loading profiles...");
                    availableProfiles = SCInstallationService.GetExistingProfiles(installation);
                }
            }
            catch (Exception ex) when (ex is XmlException or IOException or ArgumentException)
            {
                loadError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Failed to load SC schema: {ex.Message}");
            }
            finally
            {
                // Guarantee _scLoading is cleared even if an unexpected exception escapes the catch above.
                // BeginInvoke queues onto the UI thread; if an uncaught exception later faults the task
                // it becomes an unobserved task exception rather than leaving the UI stuck at "Loading...".
                _ctx.OwnerForm.BeginInvoke(() =>
                {
                    if (version != _schemaLoadVersion) return; // A newer load was started; discard this result

                    _scLoading = false;
                    _scLoadingMessage = loadError is not null ? $"Load failed: {loadError}" : "";
                    _scActions = actions;
                    _scActionMaps = actionMaps;
                    _scAvailableProfiles = availableProfiles;

                    if (actions is not null)
                    {
                        // On an explicit installation switch, load the remembered profile for
                        // the new environment (or fall back to first available).
                        if (autoLoadProfileForEnvironment)
                            LoadProfileForEnvironment(installation.Environment);

                        _scExportProfile.TargetEnvironment = installation.Environment;
                        _scExportProfile.TargetBuildId = installation.BuildId;

                        RefreshFilteredActions();
                        CalculateDeviceColumnWidths();

                        if (applyDefaultsAfterLoad)
                        {
                            ApplyDefaultBindingsToProfile();
                            UpdateConflictingBindings();
                            UpdateSharedCells();
                            SetStatus("Reset to defaults");
                        }

                        System.Diagnostics.Debug.WriteLine($"[SCBindings] Loaded {actions.Count} SC actions from {installation.Environment}");
                    }

                    _ctx.InvalidateCanvas();
                });
            } // end finally
        });
    }

    private void LoadProfileForEnvironment(string environment)
    {
        var lastProfileName = _ctx.AppSettings.GetLastSCExportProfileForEnvironment(environment);

        SCExportProfile? profile = null;
        if (!string.IsNullOrEmpty(lastProfileName))
            profile = _scExportProfileService?.LoadProfile(lastProfileName);

        // No fallback to global profiles — each environment gets its own remembered profile
        // or starts blank. Never bleed a profile from another installation.
        if (profile is not null)
        {
            _scExportProfile = profile;
            System.Diagnostics.Debug.WriteLine($"[SCBindings] Switched to profile '{profile.ProfileName}' for {environment}");
        }
        else
        {
            _scExportProfile = new SCExportProfile();
            foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
                _scExportProfile.SetSCInstance(vjoy.Id, (int)vjoy.Id);
            System.Diagnostics.Debug.WriteLine($"[SCBindings] No remembered profile for {environment} — started blank");
        }

        _scProfileDirty = false;
        UpdateConflictingBindings();
        UpdateSharedCells();
    }

    private void ReportProgress(int version, string message)
    {
        _ctx.OwnerForm.BeginInvoke(() =>
        {
            if (version != _schemaLoadVersion) return;
            _scLoadingMessage = message;
            _ctx.InvalidateCanvas();
        });
    }

    private void RefreshSCExportProfiles()
    {
        if (_scExportProfileService is null) return;
        // Exclude profiles with empty names - these are save artifacts from unnamed profiles
        _scExportProfiles = _scExportProfileService.ListProfiles()
            .Where(p => !string.IsNullOrEmpty(p.ProfileName))
            .ToList();
    }

    private void ExportToSC()
    {
        if (_scExportService is null || _scInstallations.Count == 0)
        {
            SetStatus("No SC installation available", SCStatusKind.Error);
            return;
        }

        // Only require vJoy mappings if there are vJoy joystick bindings (not physical device bindings)
        var hasVJoyBindings = _scExportProfile.Bindings.Any(b =>
            b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null);
        if (hasVJoyBindings && _scExportProfile.VJoyToSCInstance.Count == 0)
        {
            SetStatus("No vJoy mappings configured for joystick bindings", SCStatusKind.Warning);
            return;
        }

        try
        {
            var installation = _scInstallations[_selectedSCInstallation];

            // Validate profile
            var validation = _scExportService.Validate(_scExportProfile);
            if (!validation.IsValid)
            {
                SetStatus($"Validation failed: {validation.Errors.FirstOrDefault()}", SCStatusKind.Error);
                return;
            }

            // Export - use custom filename if provided, otherwise auto-generate
            string filename = string.IsNullOrEmpty(_scExportFilename)
                ? _scExportProfile.GetExportFileName()
                : $"{_scExportFilename}.xml";

            // Ensure mappings directory exists
            if (!SCInstallationService.EnsureMappingsDirectory(installation))
            {
                SetStatus($"Cannot create SC mappings directory: {installation.MappingsPath}", SCStatusKind.Error);
                return;
            }

            string exportPath = Path.Combine(installation.MappingsPath, filename);
            _scExportService.ExportToFile(_scExportProfile, exportPath);

            // Export succeeded — notify user before non-critical post-export operations
            SetStatus($"Success! Exported to {filename}", SCStatusKind.Success);
            System.Diagnostics.Debug.WriteLine($"[MainForm] Exported SC profile to: {exportPath}");

            // Refresh available profiles list (non-critical — export already succeeded)
            try
            {
                _scAvailableProfiles = SCInstallationService.GetExistingProfiles(installation);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Failed to refresh available profiles after export: {ex.Message}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus($"Export failed: {ex.Message}", SCStatusKind.Error);
            System.Diagnostics.Debug.WriteLine($"[MainForm] SC export failed: {ex}");
        }
    }

    private void SaveSCExportProfile()
    {
        if (_scExportProfileService is null) return;

        // If there's no name yet, prompt the user (same as Create New)
        if (string.IsNullOrEmpty(_scExportProfile.ProfileName))
        {
            CreateNewSCExportProfile();
            return;
        }

        _scExportProfileService.SaveProfile(_scExportProfile);
        _scProfileDirty = false;
        if (CurrentEnvironment is not null)
            _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, _scExportProfile.ProfileName);
        _ctx.AppSettings.LastSCExportProfile = _scExportProfile.ProfileName;
        RefreshSCExportProfiles();

        SetStatus($"Profile '{_scExportProfile.ProfileName}' saved", SCStatusKind.Success);
    }

    private void CreateNewSCExportProfile()
    {
        var newName = FUIInputDialog.Show(_ctx.OwnerForm, "New Control Profile", "Profile Name:",
            "New Profile", "Create");
        if (newName is not null)
        {

            // Check for duplicate name
            if (_scExportProfileService is not null && _scExportProfileService.ProfileExists(newName))
            {
                using var existsDialog = new FUIConfirmDialog(
                    "Profile Exists",
                    $"A profile named '{newName}' already exists.",
                    "OK", "Cancel");
                existsDialog.ShowDialog(_ctx.OwnerForm);
                return;
            }

            // Create new profile
            _scExportProfile = new SCExportProfile
            {
                ProfileName = newName,
                TargetEnvironment = _scExportProfile.TargetEnvironment,
                TargetBuildId = _scExportProfile.TargetBuildId
            };

            // Set up default vJoy mappings
            foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
            {
                _scExportProfile.SetSCInstance(vjoy.Id, (int)vjoy.Id);
            }

            SaveSCExportProfile();
            SetStatus($"Created profile '{newName}'", SCStatusKind.Success);
        }
    }

    private void DeleteSCExportProfile()
    {
        if (_scExportProfileService is null || _scExportProfiles.Count == 0) return;

        using var confirmDialog = new FUIConfirmDialog(
            "Delete Profile",
            $"Delete control profile '{_scExportProfile.ProfileName}'?\n\nThis cannot be undone.",
            "Delete", "Cancel");

        if (confirmDialog.ShowDialog(_ctx.OwnerForm) == DialogResult.Yes)
        {
            var deletedName = _scExportProfile.ProfileName;
            _scExportProfileService.DeleteProfile(deletedName);
            RefreshSCExportProfiles();

            // Load another profile or create default
            if (_scExportProfiles.Count > 0)
            {
                var nextProfile = _scExportProfileService.LoadProfile(_scExportProfiles[0].ProfileName);
                if (nextProfile is not null)
                {
                    _scExportProfile = nextProfile;
                    _scProfileDirty = false;
                    if (CurrentEnvironment is not null)
                        _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, nextProfile.ProfileName);
                    _ctx.AppSettings.LastSCExportProfile = nextProfile.ProfileName;
                }
            }
            else
            {
                // All profiles deleted - reset to blank unnamed state
                if (CurrentEnvironment is not null)
                    _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, null);
                _ctx.AppSettings.LastSCExportProfile = null;
                _scProfileDirty = false;
                _scExportProfile = new SCExportProfile();
                foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
                {
                    _scExportProfile.SetSCInstance(vjoy.Id, (int)vjoy.Id);
                }
            }

            SetStatus($"Deleted profile '{deletedName}'");
        }
    }

    private void LoadSCExportProfile(string profileName)
    {
        if (_scExportProfileService is null) return;

        var profile = _scExportProfileService.LoadProfile(profileName);
        if (profile is not null)
        {
            _scExportProfile = profile;
            _scProfileDirty = false;
            if (CurrentEnvironment is not null)
                _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, profileName);
            _ctx.AppSettings.LastSCExportProfile = profileName;
            _scProfileDropdownOpen = false;
            SetStatus($"Loaded profile '{profileName}'");
        }
    }

    private void ImportSCProfile(SCMappingFile mappingFile)
    {
        if (_scExportService is null) return;

        // Warn before overwriting an existing named profile's bindings
        string oldProfileName = _scExportProfile.ProfileName;
        if (!string.IsNullOrEmpty(oldProfileName) && _scExportProfile.Bindings.Count > 0)
        {
            var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
                $"Profile '{oldProfileName}' has {_scExportProfile.Bindings.Count} existing binding(s).\n\n" +
                "Import will replace all current bindings. Continue?",
                "Replace Bindings");

            if (!result)
                return;
        }

        // Always adopt the SC file's name so the dropdown reflects what was imported
        _scExportProfile.ProfileName = mappingFile.DisplayName;

        // Import the profile
        var importResult = _scExportService.ImportFromFile(mappingFile.FilePath);

        if (!importResult.Success)
        {
            SetStatus($"Import failed: {importResult.Error}", SCStatusKind.Error);
            return;
        }

        // Log import stats for debugging
        var kbCount = importResult.Bindings.Count(b => b.DeviceType == SCDeviceType.Keyboard);
        var moCount = importResult.Bindings.Count(b => b.DeviceType == SCDeviceType.Mouse);
        var jsCount = importResult.Bindings.Count(b => b.DeviceType == SCDeviceType.Joystick);
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Import parsed: {kbCount} KB, {moCount} Mouse, {jsCount} Joystick bindings");


        // Clear existing bindings and add imported ones
        _scExportProfile.ClearBindings();
        foreach (var binding in importResult.Bindings)
        {
            _scExportProfile.SetBinding(binding.ActionMap, binding.ActionName, binding);
        }

        // Update VJoy to SC instance mappings from imported joystick bindings
        var usedInstances = importResult.Bindings
            .Where(b => b.DeviceType == SCDeviceType.Joystick)
            .Select(b => b.VJoyDevice)
            .Distinct();
        foreach (var instance in usedInstances)
        {
            _scExportProfile.SetSCInstance(instance, (int)instance);
        }

        // Save the profile under the new name; delete old file if the name changed
        _scExportProfileService?.SaveProfile(_scExportProfile);
        if (!string.IsNullOrEmpty(oldProfileName) && oldProfileName != _scExportProfile.ProfileName)
            _scExportProfileService?.DeleteProfile(oldProfileName);
        _scProfileDirty = false;
        if (CurrentEnvironment is not null)
            _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, _scExportProfile.ProfileName);
        _ctx.AppSettings.LastSCExportProfile = _scExportProfile.ProfileName;
        RefreshSCExportProfiles();

        // Update conflicts
        UpdateConflictingBindings();
        UpdateSharedCells();

        // Log final profile stats
        var finalKb = _scExportProfile.Bindings.Count(b => b.DeviceType == SCDeviceType.Keyboard);
        var finalMo = _scExportProfile.Bindings.Count(b => b.DeviceType == SCDeviceType.Mouse);
        var finalJs = _scExportProfile.Bindings.Count(b => b.DeviceType == SCDeviceType.Joystick);
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Profile after save: {finalKb} KB, {finalMo} Mouse, {finalJs} Joystick bindings");

        SetStatus($"Imported {importResult.Bindings.Count} bindings ({jsCount} JS, {kbCount} KB, {moCount} Mouse)", SCStatusKind.Success);

        System.Diagnostics.Debug.WriteLine($"[SCBindings] Imported {importResult.Bindings.Count} bindings from {mappingFile.FilePath}");
    }

    private void EditSCProfileName()
    {
        var oldName = _scExportProfile.ProfileName;
        var name = FUIInputDialog.Show(_ctx.OwnerForm, "Rename Profile", "Profile Name:", oldName);
        if (name is null || name == oldName)
            return;

        // Rename on disk if the old profile was saved
        if (!string.IsNullOrEmpty(oldName) && _scExportProfileService is not null &&
            _scExportProfileService.ProfileExists(oldName))
        {
            _scExportProfileService.RenameProfile(oldName, name);
            RefreshSCExportProfiles();
        }

        _scExportProfile.ProfileName = name;
        _scProfileDirty = false;
        _scExportProfileService?.SaveProfile(_scExportProfile);
        _ctx.InvalidateCanvas();
    }

    private void ClearAllBindings()
    {
        using var dialog = new FUIConfirmDialog(
            "Clear All Bindings",
            $"Clear all {_scExportProfile.Bindings.Count} binding(s) from profile '{_scExportProfile.ProfileName}'?\n\nThis will remove ALL bindings.\nUse 'Reset to Defaults' to restore SC default bindings.",
            "Clear", "Cancel");

        if (dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.Yes)
        {
            int count = _scExportProfile.Bindings.Count;
            _scExportProfile.ClearBindings();
            _scExportProfileService?.SaveProfile(_scExportProfile);
            UpdateConflictingBindings();
            UpdateSharedCells();

            SetStatus($"Cleared {count} binding(s)");

            System.Diagnostics.Debug.WriteLine($"[MainForm] Cleared all SC bindings");
        }
    }

    private void ResetToDefaults()
    {
        using var dialog = new FUIConfirmDialog(
            "Reset to Defaults",
            "Reset all bindings to default values from\nStar Citizen's defaultProfile.xml?\n\nThis will clear your custom bindings and reload the defaults.",
            "Reset", "Cancel");

        if (dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.Yes)
        {
            _scExportProfile.ClearBindings();

            if (_scActions is not null)
            {
                // Schema already loaded — apply defaults immediately
                ApplyDefaultBindingsToProfile();
                UpdateConflictingBindings();
                UpdateSharedCells();
                SetStatus("Reset to defaults");
            }
            else if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count)
            {
                // Schema not yet loaded — trigger load and apply defaults once it completes
                LoadSCSchema(_scInstallations[_selectedSCInstallation], applyDefaultsAfterLoad: true);
            }
            else
            {
                SetStatus("No SC installation found", SCStatusKind.Error);
            }

            System.Diagnostics.Debug.WriteLine($"[MainForm] Reset SC bindings to defaults");
        }
    }

    private void SetStatus(string message, SCStatusKind kind = SCStatusKind.Info)
    {
        _scExportStatus = message;
        _scStatusKind = kind;
        _scExportStatusTime = DateTime.Now;
        _ctx.InvalidateCanvas();
    }

    private static string TruncatePath(string path, int maxLength)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
            return path;

        // Try to truncate by removing middle part
        int start = maxLength / 3;
        int end = maxLength - start - 3;  // 3 for "..."
        return path.Substring(0, start) + "..." + path.Substring(path.Length - end);
    }
}
