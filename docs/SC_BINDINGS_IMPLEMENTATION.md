# Star Citizen Bindings Integration - Implementation Plan

## Overview

This document details the implementation plan for Asteriq's Star Citizen bindings integration. The goal is to allow users to export their vJoy device mappings as Star Citizen actionmaps.xml files.

**Key Principle**: Export-only. We generate files for the user to import into SC, never modifying their existing actionmaps.xml directly.

## Reference Implementation

The architecture is based on SCVirtStick (our in-house proof of concept):
- Location: `C:\Users\mhams\source\repos\SCVirtStick\SCVirtStick\`
- Key files to reference:
  - `Core/SCVersionDetector.cs` - Installation detection
  - `Core/P4kExtractor.cs` - Data.p4k archive extraction
  - `Core/CryXmlSerializer.cs` - CryXmlB binary format parsing
  - `Core/DefaultProfileCache.cs` - Profile caching with version keys
  - `Core/SCDefaultProfileParser.cs` - Parse default bindings from SC
  - `Core/SCProfileImporter.cs` - Import existing SC profiles
  - `Core/SCXmlExporter.cs` - Export to SC actionmaps.xml format
  - `Models/BindingProfile.cs` - Internal binding profile model
  - `Models/ActionBinding.cs` - Action to input mapping
  - `Models/DeviceReference.cs` - Device identification

## Architecture

### Phase 1: Core Services

#### 1.1 SC Installation Detection (`Services/SCInstallationService.cs`)

Detects installed Star Citizen versions and their paths.

```
Responsibilities:
- Scan for SC installations (LIVE, PTU, EPTU, TECH-PREVIEW)
- Read BuildId from build_manifest.id for version tracking
- Provide paths to Data.p4k, USER folder, Mappings folder
- Support custom installation paths

Key Data:
- Version (LIVE/PTU/EPTU)
- BuildId (e.g., "9557671") - changes with each patch
- InstallPath
- DataP4kPath
- MappingsPath (USER/Client/0/Controls/Mappings)

Search Locations:
- Program Files\Roberts Space Industries\StarCitizen\
- All drive roots (Games\, Program Files\)
- User-configured custom path
```

#### 1.2 P4K Extractor (`Services/P4kExtractorService.cs`)

Extracts files from Star Citizen's Data.p4k archive.

```
Technical Details:
- P4K is a ZIP archive with PKZip Classic encryption
- Uses Zstandard compression (method 93 or 100)
- Contains defaultProfile.xml at Data/Libs/Config/defaultProfile.xml

Dependencies:
- ICSharpCode.SharpZipLib (NuGet) - ZIP handling
- ZstdSharp (NuGet) - Zstandard decompression

Key Methods:
- Open(p4kPath) - Open archive for reading
- ExtractDefaultProfile() - Get defaultProfile.xml as XmlDocument
- Handles CryXmlB binary format conversion
```

#### 1.3 Profile Cache (`Services/SCProfileCacheService.cs`)

Caches extracted defaultProfile.xml per SC version.

```
Cache Location: %APPDATA%\Asteriq\cache\sc_profiles\

Cache Key Format: {Version}_{BuildId}_defaultProfile.xml
Example: LIVE_9557671_defaultProfile.xml

Purpose:
- Avoid re-extracting from Data.p4k on every startup
- Cache invalidates when BuildId changes (new SC patch)
- Enables schema change detection by comparing old vs new
```

#### 1.4 Schema Change Detection (`Services/SCSchemaService.cs`)

Detects changes in SC's default bindings between versions.

```
Workflow:
1. On startup, check if BuildId has changed since last run
2. If changed, extract new defaultProfile.xml
3. Compare action lists between cached and new profiles
4. Generate change report:
   - New actions added
   - Actions removed
   - Actions renamed (heuristic matching)
5. Alert user if their export might be affected

Storage:
- Last known BuildId per environment in settings
- Optional: Full action list snapshot for detailed diff
```

### Phase 2: Models

#### 2.1 SC Installation Model (`Models/SCInstallation.cs`)

```csharp
public class SCInstallation
{
    public string Version { get; set; }           // LIVE, PTU, EPTU
    public string BuildId { get; set; }           // From build_manifest.id
    public string InstallPath { get; set; }       // Root SC folder
    public string DataP4kPath { get; set; }       // Path to Data.p4k
    public string MappingsPath { get; set; }      // USER/.../Mappings
    public string ActionMapsPath { get; set; }    // USER/.../actionmaps.xml
    public DateTime? LastExportDate { get; set; } // When we last exported
    public string? LastExportBuildId { get; set; } // BuildId at last export
}
```

#### 2.2 SC Action Definition (`Models/SCAction.cs`)

```csharp
public class SCAction
{
    public string ActionMap { get; set; }    // e.g., "spaceship_movement"
    public string ActionName { get; set; }   // e.g., "v_strafe_forward"
    public string Category { get; set; }     // e.g., "Flight - Movement"
    public SCInputType InputType { get; set; } // Axis, Button, etc.
    public List<SCDefaultBinding> DefaultBindings { get; set; }
}

public class SCDefaultBinding
{
    public string DeviceType { get; set; }   // keyboard, mouse, joystick
    public string Input { get; set; }        // e.g., "w", "button1", "x"
    public bool Inverted { get; set; }
    public SCActivationMode ActivationMode { get; set; }
}

public enum SCActivationMode
{
    Press,
    Hold,
    DoubleTap,
    TripleTap,
    DelayedPress
}
```

#### 2.3 SC Export Profile (`Models/SCExportProfile.cs`)

```csharp
public class SCExportProfile
{
    public string ProfileName { get; set; }
    public string TargetVersion { get; set; }     // LIVE, PTU, etc.
    public string TargetBuildId { get; set; }     // BuildId at creation
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }

    // Maps vJoy devices to SC joystick instances
    public Dictionary<uint, int> VJoyToSCInstance { get; set; }
    // e.g., { 1: 1, 2: 2 } means vJoy1=js1, vJoy2=js2

    // Custom bindings (action -> vJoy input)
    public List<SCActionBinding> Bindings { get; set; }
}

public class SCActionBinding
{
    public string ActionMap { get; set; }
    public string ActionName { get; set; }
    public uint VJoyDevice { get; set; }
    public string InputName { get; set; }    // e.g., "button1", "x"
    public bool Inverted { get; set; }
    public SCActivationMode ActivationMode { get; set; }
}
```

### Phase 3: XML Export (`Services/SCXmlExportService.cs`)

Generates Star Citizen actionmaps.xml from Asteriq mappings.

```
Output Format Requirements:
- UTF-8 without BOM
- NO XML declaration (<?xml ...?>) - SC rejects files with this
- Root element: <ActionMaps version="1" optionsVersion="2" rebindVersion="2">

Structure:
<ActionMaps>
  <CustomisationUIHeader label="ProfileName" description="" image="">
    <devices>
      <keyboard instance="1"/>
      <mouse instance="1"/>
      <joystick instance="1"/>  <!-- vJoy Device 1 -->
      <joystick instance="2"/>  <!-- vJoy Device 2 -->
    </devices>
    <categories/>
  </CustomisationUIHeader>
  <actionProfOptions/>
  <modifiers/>
  <options type="joystick" instance="1" Product="vJoy Device {GUID}"/>
  <options type="joystick" instance="2" Product="vJoy Device {GUID}"/>
  <actionmap name="spaceship_movement">
    <action name="v_strafe_forward">
      <rebind input="js1_y"/>
    </action>
  </actionmap>
</ActionMaps>

Key Method:
ExportToFile(profile, outputPath)
- Generates complete actionmaps.xml
- Saves to SC Mappings folder or user-specified location
```

### Phase 4: BINDINGS Tab UI

#### 4.1 Layout

```
+------------------------------------------------------------------+
|  [SC] STAR CITIZEN BINDINGS                                       |
+------------------------------------------------------------------+
|                                                                    |
|  +------------------+  +--------------------------------------+   |
|  | INSTALLATIONS    |  | EXPORT CONFIGURATION                 |   |
|  |                  |  |                                      |   |
|  | [*] LIVE         |  | Profile Name: [__________________]   |   |
|  |     Build 9557671|  |                                      |   |
|  |     Last: 2 days |  | vJoy Device Mapping:                 |   |
|  |                  |  |   vJoy 1 -> js1 [v]                  |   |
|  | [ ] PTU          |  |   vJoy 2 -> js2 [v]                  |   |
|  |     Build 9601234|  |                                      |   |
|  |     Never        |  | [ ] Include keyboard defaults        |   |
|  |                  |  | [ ] Include mouse defaults           |   |
|  +------------------+  |                                      |   |
|                        | [        EXPORT TO SC        ]       |   |
|  +------------------+  |                                      |   |
|  | STATUS           |  +--------------------------------------+   |
|  |                  |                                              |
|  | Schema: Current  |  +--------------------------------------+   |
|  | or               |  | SCHEMA CHANGES                       |   |
|  | [!] 5 new actions|  |                                      |   |
|  |     2 removed    |  | (shown when BuildId changes)         |   |
|  |                  |  | + v_new_action (spaceship_weapons)   |   |
|  | [Refresh]        |  | - v_old_action (removed)             |   |
|  +------------------+  +--------------------------------------+   |
|                                                                    |
+------------------------------------------------------------------+
```

#### 4.2 User Workflow

1. **First Time Setup**
   - App detects SC installations automatically
   - User selects which installation to target (LIVE/PTU)
   - User configures vJoy → SC joystick instance mapping

2. **Export Flow**
   - User clicks "Export to SC"
   - File generated at: `{SC}/USER/Client/0/Controls/Mappings/layout_asteriq_exported.xml`
   - User opens SC, goes to Options > Keybindings > Load Profile

3. **Schema Change Alert**
   - On app startup, check if SC BuildId changed
   - If changed, show warning with list of action changes
   - User can review and re-export if needed

### Phase 5: Settings Integration

Add to ProfileService/Settings:

```csharp
// SC Integration Settings
public string? SCInstallPath { get; set; }           // Custom install path
public string SCTargetEnvironment { get; set; }      // LIVE, PTU, etc.
public string? SCLastKnownBuildId { get; set; }      // For change detection
public DateTime? SCLastExportDate { get; set; }
public Dictionary<uint, int> SCVJoyMapping { get; set; } // vJoy -> js#
public string SCExportProfileName { get; set; }      // Default: "asteriq"
```

## Implementation Order

### Session 1: Foundation
- [ ] Add NuGet packages (SharpZipLib, ZstdSharp)
- [ ] Create `Models/SCInstallation.cs`
- [ ] Create `Services/SCInstallationService.cs`
- [ ] Test installation detection

### Session 2: P4K Extraction
- [ ] Create `Services/P4kExtractorService.cs`
- [ ] Create `Services/CryXmlService.cs` (CryXmlB parsing)
- [ ] Create `Services/SCProfileCacheService.cs`
- [ ] Test defaultProfile.xml extraction

### Session 3: Schema & Export
- [ ] Create `Models/SCAction.cs`, `SCExportProfile.cs`
- [ ] Create `Services/SCSchemaService.cs`
- [ ] Create `Services/SCXmlExportService.cs`
- [ ] Test export generation

### Session 4: UI
- [ ] Update BINDINGS tab placeholder with real UI
- [ ] Installation selector panel
- [ ] Export configuration panel
- [ ] Schema change alert panel

### Session 5: Integration & Polish
- [ ] Wire up settings persistence
- [ ] Add export success/error notifications
- [ ] Test full workflow
- [ ] Handle edge cases (no SC installed, corrupt p4k, etc.)

## Dependencies to Add

```xml
<!-- In Asteriq.csproj -->
<PackageReference Include="SharpZipLib" Version="1.4.2" />
<PackageReference Include="ZstdSharp.Port" Version="0.7.4" />
```

## File Structure

```
src/Asteriq/
├── Models/
│   ├── SCInstallation.cs
│   ├── SCAction.cs
│   └── SCExportProfile.cs
├── Services/
│   ├── SCInstallationService.cs
│   ├── P4kExtractorService.cs
│   ├── CryXmlService.cs
│   ├── SCProfileCacheService.cs
│   ├── SCSchemaService.cs
│   └── SCXmlExportService.cs
└── UI/
    └── MainForm.cs (BINDINGS tab implementation)
```

## Testing Considerations

1. **No SC Installed**: Graceful handling, show "SC not found" message
2. **Multiple Installations**: Let user choose which to target
3. **Corrupt P4K**: Handle extraction failures gracefully
4. **Large P4K**: Extraction is slow (~500k entries), show progress
5. **Schema Changes**: Verify detection works across patch versions
6. **Export Validation**: Ensure generated XML is valid for SC import

## Notes from SCVirtStick

- P4K keys are pre-computed CryEngine PKZip keys (public knowledge)
- Some entries use Zstandard compression (method 93 or 100)
- defaultProfile.xml may be in CryXmlB binary format, needs conversion
- SC rejects XML files with `<?xml?>` declaration - must omit
- SC requires UTF-8 without BOM
- User mappings path changed in SC 3.18+ to include `Client/0/`
