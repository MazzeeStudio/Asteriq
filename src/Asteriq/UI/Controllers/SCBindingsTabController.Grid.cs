using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private List<SCGridColumn> GetSCGridColumns()
    {
        var columns = new List<SCGridColumn>
        {
            new SCGridColumn { Id = "kb", Header = "KB", DevicePrefix = "kb1", IsKeyboard = true },
            new SCGridColumn { Id = "mouse", Header = "Mouse", DevicePrefix = "mo1", IsMouse = true }
        };

        // Track all SC instances in use to avoid collisions between vJoy and physical columns
        var usedSCInstances = new HashSet<int>();

        // Add a column for each vJoy device that exists and is mapped in the export profile
        var existingVJoyIds = _ctx.VJoyDevices.Where(v => v.Exists).Select(v => v.Id).ToHashSet();
        foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
        {
            int scInstance = _scExportProfile.GetSCInstance(vjoy.Id);
            columns.Add(new SCGridColumn
            {
                Id = $"js{scInstance}",
                Header = $"JS{scInstance}",
                DevicePrefix = $"js{scInstance}",
                VJoyDeviceId = vjoy.Id,
                SCInstance = scInstance,
                IsJoystick = true
            });
            usedSCInstances.Add(scInstance);
        }

        // Add read-only columns for JS instances stored in the profile that have no backing vJoy device.
        // This lets users view bindings they previously configured even when vJoy isn't installed.
        foreach (var kv in _scExportProfile.VJoyToSCInstance
            .Where(kv => !existingVJoyIds.Contains(kv.Key))
            .OrderBy(kv => kv.Value))
        {
            columns.Add(new SCGridColumn
            {
                Id = $"js{kv.Value}",
                Header = $"JS{kv.Value}",
                DevicePrefix = $"js{kv.Value}",
                VJoyDeviceId = kv.Key,
                SCInstance = kv.Value,
                IsJoystick = true,
                IsReadOnly = true
            });
            usedSCInstances.Add(kv.Value);
        }

        // Add physical device columns only when no vJoy devices exist.
        // When vJoy is installed, physical devices are routed through vJoy â€”
        // showing them as separate columns would create confusion and duplicate bindings.
        if (existingVJoyIds.Count > 0)
            return columns;

        // Track VID:PID counts for disambiguating multiple identical devices
        var vidPidCounts = new Dictionary<string, int>();

        foreach (var device in _ctx.Devices)
        {
            if (device.IsVirtual || !device.IsConnected) continue;

            // Use VID:PID as the stable device key (survives unplug/replug).
            // HidDevicePath is preferred when available but not all devices have it.
            string baseKey = GetPhysicalDeviceKey(device);
            if (string.IsNullOrEmpty(baseKey)) continue;

            // Disambiguate duplicate VID:PID devices (e.g. two Alpha Primes)
            vidPidCounts.TryGetValue(baseKey, out int count);
            vidPidCounts[baseKey] = count + 1;
            string deviceKey = count == 0 ? baseKey : $"{baseKey}#{count + 1}";

            // Check if this device already has a persisted SC instance
            int scInstance = _scExportProfile.GetSCInstanceForPhysical(deviceKey);
            if (scInstance == 0)
            {
                // Assign next available SC instance
                scInstance = usedSCInstances.Count > 0 ? usedSCInstances.Max() + 1 : 1;
                _scExportProfile.SetSCInstanceForPhysical(deviceKey, scInstance);
                // Persist the DirectInput GUID for XML export
                if (device.DirectInputGuid != Guid.Empty)
                {
                    _scExportProfile.PhysicalDeviceDirectInputGuids[deviceKey] = device.DirectInputGuid;
                }
                _scExportProfileService?.SaveProfile(_scExportProfile);
            }

            usedSCInstances.Add(scInstance);

            // Build a truncated header from the device name
            string shortName = TruncateDeviceName(device.Name);

            columns.Add(new SCGridColumn
            {
                Id = $"phys:{deviceKey}",
                Header = shortName,
                DevicePrefix = $"js{scInstance}",
                SCInstance = scInstance,
                IsJoystick = true,
                PhysicalDevice = device,
                PhysicalDeviceKey = deviceKey
            });
        }

        return columns;
    }

    /// <summary>
    /// Get a stable key for a physical device. Uses VID:PID from the SDL GUID which
    /// survives unplug/replug. Falls back to HidDevicePath if VID:PID is unavailable.
    /// </summary>
    private static string GetPhysicalDeviceKey(PhysicalDeviceInfo device)
    {
        var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(device.InstanceGuid);
        if (vid > 0 && pid > 0)
            return $"{vid:X4}:{pid:X4}";

        // Fall back to HID device path if available
        if (!string.IsNullOrEmpty(device.HidDevicePath))
            return device.HidDevicePath;

        return string.Empty;
    }

    private static string TruncateDeviceName(string name)
    {
        // Strip common generic suffixes
        string[] stripSuffixes = { " USB Joystick", " USB", " HID", " Device" };
        foreach (var suffix in stripSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
                name = name[..^suffix.Length];
        }

        // Truncate to max 16 chars
        if (name.Length > 16)
            name = name[..14] + "..";

        return name;
    }

    private void CalculateDeviceColumnWidths()
    {
        _grid.DeviceColWidths.Clear();

        if (_scInstall.Actions is null) return;

        var columns = GetSCGridColumns();
        float padding = 12f; // Cell padding

        foreach (var col in columns)
        {
            float maxWidth = _grid.DeviceColMinWidth;

            // Determine device type for this column
            SCDeviceType? deviceType = col.IsKeyboard ? SCDeviceType.Keyboard :
                                        col.IsMouse ? SCDeviceType.Mouse :
                                        col.IsJoystick ? SCDeviceType.Joystick : null;

            foreach (var action in _scInstall.Actions)
            {
                // Check profile bindings for this column
                if (deviceType.HasValue)
                {
                    SCActionBinding? binding = null;
                    if (col.IsJoystick)
                    {
                        binding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, SCDeviceType.Joystick);
                        // Check if this binding matches the current column's device
                        if (binding is not null && _scExportProfile.GetSCInstance(binding.VJoyDevice) != col.SCInstance)
                            binding = null;
                    }
                    else
                    {
                        binding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, deviceType.Value);
                    }

                    if (binding is not null && !string.IsNullOrEmpty(binding.InputName))
                    {
                        var components = SCBindingsRenderer.GetBindingComponents(binding.InputName, binding.Modifiers);
                        float badgesWidth = SCBindingsRenderer.MeasureMultiKeycapWidth(components, binding.InputType) + padding;
                        maxWidth = Math.Max(maxWidth, badgesWidth);
                    }

                    // Shared cells also contribute to column width. A share is a standalone input
                    // reference with no modifiers of its own, so measure with an empty modifier list.
                    if (col.IsJoystick && !col.IsPhysical)
                    {
                        foreach (var primary in _scExportProfile.Bindings)
                        {
                            if (primary.DeviceType != SCDeviceType.Joystick || primary.PhysicalDeviceId is not null) continue;
                            if (primary.ActionMap != action.ActionMap || primary.ActionName != action.ActionName) continue;
                            var shared = primary.SharedWith.FirstOrDefault(s => s.VJoySlot == col.VJoyDeviceId);
                            if (shared is null || string.IsNullOrEmpty(shared.InputName)) continue;
                            var components = SCBindingsRenderer.GetBindingComponents(shared.InputName, null);
                            float badgesWidth = SCBindingsRenderer.MeasureMultiKeycapWidth(components, primary.InputType) + padding;
                            maxWidth = Math.Max(maxWidth, badgesWidth);
                        }
                    }
                }

                // Also check default bindings
                var defaultBinding = action.DefaultBindings
                    .FirstOrDefault(b => b.DevicePrefix.Equals(col.DevicePrefix, StringComparison.OrdinalIgnoreCase));
                if (defaultBinding is not null && !string.IsNullOrEmpty(defaultBinding.Input))
                {
                    var modifiers = defaultBinding.Modifiers?.Where(m => !string.IsNullOrEmpty(m)).ToList();
                    var components = SCBindingsRenderer.GetBindingComponents(defaultBinding.Input, modifiers);
                    // Default bindings don't have input type info
                    float badgesWidth = SCBindingsRenderer.MeasureMultiKeycapWidth(components, null) + padding;
                    maxWidth = Math.Max(maxWidth, badgesWidth);
                }
            }

            _grid.DeviceColWidths[col.Id] = maxWidth;
        }
    }

    private void RefreshFilteredActions()
    {
        if (_scInstall.Actions is null || _scSchemaService is null)
        {
            _scFilteredActions = null;
            return;
        }

        // Start with joystick-relevant actions
        var actions = SCSchemaService.FilterJoystickActions(_scInstall.Actions);

        // Apply action map filter if set (use category name for filtering)
        // Use GetCategoryNameForAction to respect action-level overrides (e.g., Emergency)
        if (!string.IsNullOrEmpty(_searchFilter.ActionMapFilter))
        {
            actions = actions.Where(a =>
                SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName) == _searchFilter.ActionMapFilter).ToList();
        }

        // Apply search filter if set
        if (!string.IsNullOrEmpty(_searchFilter.SearchText))
        {
            // Button-capture mode: exact match on the captured input restricted to the
            // highlighted column.  This prevents "button3" from matching "button30" etc.
            // Text-entry mode: broad substring search across names, categories and bindings.
            if (_searchFilter.ButtonCaptureTextActive
                && _grid.Columns is not null
                && _colImport.HighlightedColumn >= 0
                && _colImport.HighlightedColumn < _grid.Columns.Count)
            {
                var col = _grid.Columns[_colImport.HighlightedColumn];
                string captured = _searchFilter.SearchText;
                // Strip modifier prefix to get the raw input name
                string capturedInput = captured.Contains('+')
                    ? captured[(captured.LastIndexOf('+') + 1)..]
                    : captured;
                string? capturedModifier = captured.Contains('+')
                    ? captured[..captured.LastIndexOf('+')]
                    : null;

                uint? vjoyId = (col.IsJoystick && !col.IsPhysical) ? col.VJoyDeviceId : null;
                string? physId = col.IsPhysical ? col.PhysicalDeviceKey : null;
                SCDeviceType? capDevType = col.IsKeyboard ? SCDeviceType.Keyboard
                    : col.IsMouse ? SCDeviceType.Mouse
                    : null;

                actions = actions.Where(a => SCBindingsSearch.MatchesButtonCapture(
                    a, _scExportProfile.Bindings, capturedInput, capturedModifier, vjoyId, physId, capDevType)).ToList();
            }
            else
            {
                var searchLower = _searchFilter.SearchText.ToLowerInvariant();
                actions = actions.Where(a => SCBindingsSearch.MatchesTextSearch(
                    a, _scExportProfile.Bindings, searchLower)).ToList();
            }
        }

        // Apply "show bound only" filter if enabled â€” includes JS, KB, and Mouse bindings
        if (_ctx.AppSettings.SCBindingsShowBoundOnly)
        {
            actions = actions.Where(a =>
                _scExportProfile.HasAnyBinding(a.ActionMap, a.ActionName)
            ).ToList();
        }

        // Sort by category order (like SCVirtStick), then by action name
        // IMPORTANT: Use GetSortOrderForAction to respect action-level overrides (e.g., Emergency)
        _scFilteredActions = actions
            .OrderBy(a => SCCategoryMapper.GetSortOrderForAction(a.ActionMap, a.ActionName))
            .ThenBy(a => SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName))
            .ThenBy(a => a.ActionName)
            .ToList();

        _scBindingsScrollOffset = 0;  // Reset scroll when filter changes
        _scSelectedActionIndex = -1;  // Clear selection
    }


    /// <summary>
    /// Rebuilds the shared-cell lookup from the current export profile.
    /// Called after any binding change that may affect SharedWith lists.
    /// </summary>
    private void ScrollToAction(int actionIndex)
    {
        if (_scFilteredActions is null || actionIndex < 0 || actionIndex >= _scFilteredActions.Count)
            return;

        // Expand the category if it is collapsed
        var target = _scFilteredActions[actionIndex];
        string categoryName = SCCategoryMapper.GetCategoryNameForAction(target.ActionMap, target.ActionName);
        _scCollapsedCategories.Remove(categoryName);

        // Compute the Y offset of this action row within the content area
        float rowHeight = 28f, rowGap = 2f, categoryHeaderHeight = 28f;
        string? lastCategory = null;
        float contentY = 0;
        for (int i = 0; i <= actionIndex; i++)
        {
            var action = _scFilteredActions[i];
            string cat = SCCategoryMapper.GetCategoryNameForAction(action.ActionMap, action.ActionName);
            if (cat != lastCategory)
            {
                lastCategory = cat;
                contentY += categoryHeaderHeight;
            }
            if (i == actionIndex) break;
            contentY += rowHeight + rowGap;
        }

        // Centre the row vertically in the visible list area
        float rowMid = contentY + rowHeight / 2f;
        float viewHalf = _scBindingsListBounds.Height / 2f;
        float maxScroll = Math.Max(0, _scBindingsContentHeight - _scBindingsListBounds.Height);
        _scBindingsScrollOffset = Math.Clamp(rowMid - viewHalf, 0, maxScroll);

        // Select the row and start the highlight pulse
        _scSelectedActionIndex = actionIndex;
        _cell.SelectedCell = (actionIndex, _cell.SelectedCell.colIndex);
        _conflicts.HighlightActionIndex = actionIndex;
        _conflicts.HighlightStartTime = DateTime.Now;
        UpdateConflictLinks();
        _ctx.MarkDirty();
    }
}