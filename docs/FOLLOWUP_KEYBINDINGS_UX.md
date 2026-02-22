# Keybindings Tab — Follow-up Design Questions

## Context
During the 2026-02-22 session we fixed several Control Profile dropdown issues
(blank entries, sticky labels, stale IMPORT FROM SC list on installation switch).
The session ended with open design questions that need decisions before
implementing per-installation profile memory.

---

## Open Questions

### 1. Per-installation Control Profiles
**Problem:** Switching SC installations (LIVE → TECH-PREVIEW) leaves the previously
loaded control profile active, even though it belongs to the old installation.

**User expectation:** Each installation should remember its own last-used profile.
Switching installations should load that installation's last profile (or blank if none).

**Design decision needed:**
- Should each installation have its own independent control profile, or should profiles
  be shareable across installations (user explicitly copies them)?
- Proposed: per-environment last-profile stored in `appsettings.json` as
  `lastSCExportProfileByEnvironment: { "LIVE": "Updated", "TECH-PREVIEW": "GameGlass Keyboard 4.0" }`

---

### 2. Unsaved Changes on Installation Switch
**Problem:** If the user modifies bindings and then switches installations without saving,
changes are silently lost (profile resets to new installation's last profile).

**Design decision needed:**
- Prompt on switch if dirty? ("You have unsaved changes — save before switching?")
- Silent auto-save before switching?
- Silent discard (current behavior, bad UX)?

**Related:** Is there even a clear "dirty" state concept for control profiles right now?
A visual indicator (e.g. `*` in the profile name, or a status pill) would help the
user know when they have unsaved changes.

---

### 3. Import as Draft vs. Immediate Save
**Problem:** When you click an "IMPORT FROM SC" file, it immediately saves to disk
under the SC file's display name. This means:
- The user can't preview what the import contains before committing
- Clicking accidentally overwrites the previous file

**Design decision needed:**
- Should import be a **draft/preview** first, with an explicit "Accept" / "Discard"?
- Or is immediate-save acceptable, with undo/revert?
- Related: should "IMPORT FROM SC" create a **new** control profile (never overwriting
  the active one) rather than replacing the active profile's contents?

---

### 4. Leaving the Tab with Unsaved Changes
**Problem:** No prompt when navigating away from the Keybindings tab with unsaved
profile changes.

**Design decision needed:**
- Prompt on tab switch? (Probably too aggressive for small changes)
- Auto-save? (Simplest — every binding change writes through immediately)
- Dirty indicator only, no prompt?

---

## Proposed Implementation Order (once design is settled)

1. **Add dirty tracking** — `_scProfileDirty` flag, set on any binding change, cleared on save
2. **Visual dirty indicator** — `*` suffix in the dropdown header or a small dot
3. **Per-environment last-profile** — `IApplicationSettingsService.GetLastSCExportProfileForEnvironment()` / `SetLastSCExportProfileForEnvironment()`
4. **Installation switch logic** — save current profile for old env, load last profile for new env (or blank)
5. **Unsaved-changes guard** — prompt or auto-save before switching installation / tab

---

## Files That Will Need Changes

| File | Change |
|------|--------|
| `Services/Abstractions/IApplicationSettingsService.cs` | Add `GetLastSCExportProfileForEnvironment` / `SetLastSCExportProfileForEnvironment` |
| `Services/ApplicationSettingsService.cs` | Implement above; add `LastSCExportProfileByEnvironment` dict to `AppSettings` |
| `UI/Controllers/SCBindingsTabController.cs` | Per-env load/save, dirty flag, installation switch guard |
