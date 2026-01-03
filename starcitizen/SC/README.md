# Star Citizen Integration

This folder contains all code for reading and parsing Star Citizen game files.

## Overview

The plugin reads key bindings in two layers:

1. **Base Layer** - `defaultProfile.xml` extracted from `Data.p4k`
   - Contains all default bindings for every action
   - Read-only game data

2. **Override Layer** - `actionmaps.xml` in user's profile folder
   - Contains only user-customized bindings
   - Watched for changes and auto-reloaded

## Files

### SCPath.cs
**Auto-detects Star Citizen installation paths.**

Detection methods (in order):
1. RSI Launcher config (`%APPDATA%\rsilauncher\`)
2. Windows Registry keys
3. Common installation directories
4. Steam library folders
5. Manual `appsettings.config` override

Key properties:
- `SCClientPath` - Path to LIVE or PTU folder
- `SCData_p4k` - Path to Data.p4k file
- `SCClientProfilePath` - Path to user profile folder

### SCDefaultProfile.cs
**Reads defaultProfile.xml and actionmaps.xml content.**

- `DefaultProfile()` - Returns cached defaultProfile.xml content
- `ActionMaps()` - Reads actionmaps.xml with retry/stabilization logic

### DProfileReader.cs
**Parses XML into Action dictionaries.**

Main classes:
- `Action` - Single binding with Keyboard, Mouse, Joystick properties
- `ActionMap` - Group of related actions (e.g., "spaceship_flight")
- `ActivationMode` - Press/hold/release behavior settings

Key methods:
- `fromXML(xml)` - Parse defaultProfile.xml
- `fromActionProfile(xml)` - Parse actionmaps.xml and merge overrides
- `GetBinding(key)` - Lookup action by full name

### SCFiles.cs
**Caches extracted p4k files locally.**

- Extracts defaultProfile.xml and language files from Data.p4k
- Stores compressed (.scj) cache files in plugin directory
- Checks file dates to detect when p4k has been updated

### SCUiText.cs
**Localization text lookup.**

Loads UI strings from `Data\Localization\english\global.ini` to display human-readable action names.

### SCLocale.cs
**Language-specific string dictionary.** One instance per supported language.

### SCFile.cs
**Metadata container for cached files.** Tracks file type, path, date, and content.

### TheUser.cs
**User preferences.** Contains `UsePTU` flag and file store directory.

## Data Flow

```
Data.p4k
    ?
    ? (p4kFile/ extracts)
defaultProfile.xml (binary CryXML)
    ?
    ? (CryXMLlib/ converts)
XML string
    ?
    ? (DProfileReader parses)
Dictionary<string, Action>
    ?
    ? (merged with)
actionmaps.xml overrides
    ?
    ?
Final bindings available via KeyBindingService
```

## See Also

- [p4kFile/](../p4kFile/) - P4K archive extraction
- [CryXMLlib/](../CryXMLlib/) - CryEngine XML parsing
- [Core/KeyBindingService.cs](../Core/KeyBindingService.cs) - Main service
