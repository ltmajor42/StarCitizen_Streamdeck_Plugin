# Star Citizen Stream Deck Plugin - Architecture Guide

This document provides a comprehensive overview of the plugin's architecture, components, and data flow.

---

## Table of Contents

1. [High-Level Overview](#high-level-overview)
2. [Project Structure](#project-structure)
3. [Core Components](#core-components)
4. [Button Actions](#button-actions)
5. [Star Citizen Integration](#star-citizen-integration)
6. [Data Flow](#data-flow)
7. [Key Concepts](#key-concepts)
8. [Design Patterns](#design-patterns)

---

## High-Level Overview

```
???????????????????????????????????????????????????????????????????
?                      Stream Deck Software                        ?
?  (Elgato application that communicates via WebSocket)           ?
???????????????????????????????????????????????????????????????????
                          ? WebSocket (JSON messages)
                          ?
???????????????????????????????????????????????????????????????????
?                    Plugin Entry Point                            ?
?  Program.cs ? KeyBindingService.Initialize() ? SDWrapper.Run()  ?
???????????????????????????????????????????????????????????????????
                          ?
        ?????????????????????????????????????
        ?                 ?                 ?
????????????????? ????????????????? ?????????????????
?  ActionKey    ? ?   Momentary   ? ?     Dial      ?
?  StateMemory  ? ?  DualAction   ? ?  (encoders)   ?
?  Repeataction ? ?  HoldMacro    ? ?               ?
?  ActionDelay  ? ?  CosmeticKey  ? ?               ?
????????????????? ????????????????? ?????????????????
        ?                 ?                 ?
        ?????????????????????????????????????
                          ?
                          ?
???????????????????????????????????????????????????????????????????
?                StarCitizenKeypadBase (Shared Infrastructure)     ?
?  - Property Inspector event wiring                               ?
?  - Sound loading/playback utilities                              ?
?  - Key binding lookup helpers                                    ?
?  - Common lifecycle management                                   ?
???????????????????????????????????????????????????????????????????
                          ?
                          ?
???????????????????????????????????????????????????????????????????
?                    KeyBindingService                             ?
?  - Loads bindings from Data.p4k (defaultProfile.xml)            ?
?  - Merges user overrides from actionmaps.xml                    ?
?  - Watches for file changes and auto-reloads                    ?
?  - Provides TryGetBinding() for all actions                     ?
???????????????????????????????????????????????????????????????????
                          ?
                          ?
???????????????????????????????????????????????????????????????????
?                    Input Simulation                              ?
?  StreamDeckCommon ? CommandTools ? InputSimulator               ?
?  - Converts SC tokens to DirectInput keycodes                   ?
?  - Sends keyboard/mouse events to Windows                       ?
???????????????????????????????????????????????????????????????????
```

---

## Project Structure

```
starcitizen/
??? Program.cs              # Entry point - initializes KeyBindingService
?
??? Core/                   # Core services and infrastructure
?   ??? KeyBindingService.cs      # Singleton - manages binding lifecycle
?   ??? KeyBindingWatcher.cs      # FileSystemWatcher for actionmaps.xml
?   ??? PluginLog.cs              # Centralized logging to pluginlog.log
?   ??? PropertyInspectorMessenger.cs  # Sends function list to PI
?   ??? CommandTools.cs           # SC key token ? DirectInput conversion
?   ??? MouseTokenHelper.cs       # Mouse token normalization
?   ??? KeyboardLayouts.cs        # Keyboard locale detection
?   ??? FifoExecution.cs          # Thread-safe work queue
?
??? Buttons/                # Stream Deck button action implementations
?   ??? StarCitizenKeypadBase.cs  # Base class with shared infrastructure
?   ??? StarCitizenDialBase.cs    # Base class for SD+ dial encoders
?   ??? ActionKey.cs              # Simple key-down/key-up on press/release
?   ??? Momentary.cs              # One-shot with visual feedback
?   ??? StateMemory.cs            # Toggle with soft-sync long-press
?   ??? DualAction.cs             # Different actions on press vs release
?   ??? Repeataction.cs           # Repeats while held
?   ??? ActionDelay.cs            # Delayed execution with cancel
?   ??? HoldMacroAction.cs        # Hold behavior (key stays down)
?   ??? CosmeticKey.cs            # Visual-only, no action
?   ??? Dial.cs                   # Stream Deck+ dial encoder
?   ??? LegacyActionKey.cs        # Backward compatibility wrapper
?   ??? StreamDeckCommon.cs       # Input simulation utilities
?   ??? StreamDeckEventArgsExtensions.cs  # Helper for PI payload extraction
?   ??? FunctionListBuilder.cs    # Builds dropdown data for PI
?
??? SC/                     # Star Citizen file parsing
?   ??? SCPath.cs                 # Auto-detects SC installation paths
?   ??? SCDefaultProfile.cs       # Reads defaultProfile.xml & actionmaps.xml
?   ??? DProfileReader.cs         # Parses XML into Action dictionaries
?   ??? SCFiles.cs                # Caches extracted p4k files locally
?   ??? SCFile.cs                 # File metadata container
?   ??? SCUiText.cs               # Localization text lookup
?   ??? SCLocale.cs               # Language-specific string dictionary
?   ??? TheUser.cs                # User preferences (PTU toggle, etc.)
?
??? Audio/                  # Sound playback
?   ??? AudioPlaybackEngine.cs    # Singleton audio mixer (NAudio)
?   ??? CachedSound.cs            # Pre-loaded audio buffer
?   ??? CachedSoundSampleProvider.cs  # Playback provider
?   ??? AutoDisposeFileReader.cs  # Auto-cleanup file reader
?
??? p4kFile/                # P4K archive extraction (from Data.p4k)
?   ??? p4kDirectory.cs           # Scans archive for files
?   ??? p4kFile.cs                # File entry representation
?   ??? (other p4k utilities)
?
??? CryXMLlib/              # CryEngine binary XML parsing
?   ??? CryXmlBinReader.cs        # Reads binary XML format
?   ??? XmlTree.cs                # Converts to standard XML string
?   ??? (other CryXML utilities)
?
??? PropertyInspector/      # Web-based settings UI (HTML/CSS/JS)
?   ??? StarCitizen/              # Per-action HTML pages
?   ??? sdpi.css                  # Stream Deck PI stylesheet
?   ??? sdtools.common.js         # Shared PI JavaScript
?
??? Images/                 # Button icons and plugin branding
```

---

## Core Components

### KeyBindingService (Singleton)

The central hub for all key binding operations.

```csharp
// Get the singleton instance
KeyBindingService.Instance.Initialize();  // Called once at startup

// Check if bindings are loaded
if (bindingService.Reader != null) { ... }

// Get a binding by function name
if (bindingService.TryGetBinding("spaceship_flight-v_pitch", out var action))
{
    var keyboard = action.Keyboard;  // e.g., "w" or "lalt+f"
}

// Subscribe to binding updates
bindingService.KeyBindingsLoaded += (s, e) => UpdatePropertyInspector();
```

**Responsibilities:**
- Loads `defaultProfile.xml` from `Data.p4k` (base layer)
- Merges `actionmaps.xml` overrides (user customizations)
- Watches for file changes and auto-reloads
- Raises `KeyBindingsLoaded` event when bindings refresh

### PluginLog

Centralized logging that writes to both Stream Deck's log and a plugin-specific file.

```csharp
PluginLog.Info("Starting key binding load...");
PluginLog.Warn("actionmaps.xml not found, using defaults");
PluginLog.Error($"Failed to parse: {ex.Message}");
PluginLog.Debug("Detailed trace information");
```

**Log file location:**
`%appdata%\Elgato\StreamDeck\Plugins\com.ltmajor42.starcitizen.sdPlugin\pluginlog.log`

### CommandTools

Converts Star Citizen key tokens to DirectInput keycodes for input simulation.

```csharp
// Convert "lalt+f" ? "{DikLalt}{DikF}" macro format
string macro = CommandTools.ConvertKeyString(action.Keyboard);

// Check if a token is valid
if (CommandTools.TryFromSCKeyboardCmd("np_enter", out var dikCode)) { ... }
```

### StreamDeckCommon

Sends keyboard and mouse input to Windows.

```csharp
// Full keypress (down + delay + up)
StreamDeckCommon.SendKeypress(keyInfo, delayMs);

// Separate down/up for hold behaviors
StreamDeckCommon.SendKeypressDown(keyInfo);
StreamDeckCommon.SendKeypressUp(keyInfo);
```

---

## Button Actions

### Base Class Hierarchy

All button actions derive from shared base classes that provide common infrastructure:

```
KeypadBase (BarRaider SDK)
    ??? StarCitizenKeypadBase
            ??? ActionKey
            ??? Momentary
            ??? StateMemory
            ??? DualAction
            ??? Repeataction
            ??? ActionDelay
            ??? HoldMacroAction

EncoderBase (BarRaider SDK)
    ??? StarCitizenDialBase
            ??? Dial
```

### StarCitizenKeypadBase Features

The base class provides shared infrastructure to reduce code duplication:

```csharp
public abstract class StarCitizenKeypadBase : KeypadBase
{
    // Services
    protected readonly KeyBindingService BindingService;

    // Property Inspector Infrastructure
    protected void WirePropertyInspectorEvents();
    protected void UnwirePropertyInspectorEvents();
    protected virtual void SendFunctionsToPropertyInspector();

    // Sound Playback Infrastructure
    protected bool LoadClickSound(string soundPath);
    protected void LoadClickSoundFromSettings<T>(T settings) where T : ISoundSettings;
    protected void PlayClickSound();
    protected void PlaySound(CachedSound sound);
    protected CachedSound TryLoadSound(string filePath, out string normalizedPath);

    // Key Binding Helpers
    protected bool TryGetKeyBinding(string functionName, out string keyInfo);
    protected bool EnsureBindingsReady();
}
```

### Settings Interfaces

Button settings implement these interfaces for consistent behavior:

```csharp
// For actions with a single function binding
public interface IFunctionSettings
{
    string Function { get; set; }
}

// For actions with click sound support
public interface ISoundSettings
{
    string ClickSoundFilename { get; set; }
}

// Base class combining common settings
public abstract class PluginSettingsBase : IFunctionSettings, ISoundSettings
{
    public virtual string Function { get; set; }
    public virtual string ClickSoundFilename { get; set; }
}
```

### Settings Best Practices

**Important:** All numeric settings from Property Inspector should be stored as `string` and parsed manually to handle empty values gracefully:

```csharp
protected class PluginSettings : PluginSettingsBase
{
    // ? Correct: Use string for PI numeric values
    [JsonProperty(PropertyName = "delay")]
    public string Delay { get; set; } = "1000";

    // ? Avoid: int can crash on empty string from PI
    // public int Delay { get; set; } = 1000;
}

// Parse in settings handler
private void ParseSettings()
{
    if (int.TryParse(settings.Delay, out var parsed) && parsed >= 0)
        currentDelay = parsed;
    else
        currentDelay = DefaultDelay;
}
```

### Common Action Pattern

```csharp
[PluginActionId("com.ltmajor42.starcitizen.myaction")]
public class MyAction : StarCitizenKeypadBase
{
    #region Settings
    protected class PluginSettings : PluginSettingsBase
    {
        public static PluginSettings CreateDefaultSettings() => new();
    }
    #endregion

    #region State
    private readonly PluginSettings settings;
    #endregion

    #region Initialization
    public MyAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
    {
        settings = PluginSettings.CreateDefaultSettings();
        if (payload.Settings?.Count > 0)
        {
            Tools.AutoPopulateSettings(settings, payload.Settings);
            LoadClickSoundFromSettings(settings);
        }

        WirePropertyInspectorEvents();
        SendFunctionsToPropertyInspector();
    }
    #endregion

    #region Key Events
    public override void KeyPressed(KeyPayload payload)
    {
        if (!EnsureBindingsReady()) return;

        if (TryGetKeyBinding(settings.Function, out var keyInfo))
        {
            StreamDeckCommon.SendKeypressDown(keyInfo);
        }
        PlayClickSound();
    }

    public override void KeyReleased(KeyPayload payload)
    {
        if (!EnsureBindingsReady()) return;

        if (TryGetKeyBinding(settings.Function, out var keyInfo))
        {
            StreamDeckCommon.SendKeypressUp(keyInfo);
        }
    }
    #endregion

    #region Settings Management
    public override void ReceivedSettings(ReceivedSettingsPayload payload)
    {
        Tools.AutoPopulateSettings(settings, payload.Settings);
        LoadClickSoundFromSettings(settings);
    }
    #endregion

    #region Disposal
    public override void Dispose()
    {
        base.Dispose();
    }
    #endregion
}
```

### Action Types Summary

| Action | Behavior | Use Case |
|--------|----------|----------|
| **ActionKey** | Key down on press, up on release | Standard hold-to-activate |
| **Momentary** | Quick press with visual feedback | One-shot triggers |
| **StateMemory** | Toggle with long-press resync | Lights, gear, VTOL |
| **DualAction** | Action A on press, Action B on release | Two-stage workflows |
| **Repeataction** | Repeats while held | Power management |
| **ActionDelay** | Timer with cancel window | Safer actions |
| **HoldMacro** | Key stays down until release/timer | Charge-up actions |
| **Dial** | Rotate/press/touch for SD+ | Increment controls |
| **CosmeticKey** | Visual only | Separators, labels |

---

## Star Citizen Integration

### Path Detection (SCPath.cs)

The plugin automatically finds Star Citizen using multiple methods:

1. **RSI Launcher config** - `%APPDATA%\rsilauncher\library_folder.json`
2. **Registry keys** - Various launcher installation keys
3. **Common paths** - Standard install directories
4. **Steam libraries** - Steam library folders
5. **Manual config** - `appsettings.config` override

### Binding Loading Flow

```
Data.p4k (Game Archive)
    ?
    ? Extract via p4kFile/
?????????????????????????????
?  defaultProfile.xml       ?  Base layer (all default bindings)
?  (binary CryXML format)   ?
?????????????????????????????
              ? Parse via DProfileReader
              ?
?????????????????????????????
?  In-memory ActionMap      ?  Dictionary<string, Action>
?  Dictionary               ?
?????????????????????????????
              ?
              ? Merge with
?????????????????????????????
?  actionmaps.xml           ?  User customizations (override layer)
?  (USER/Client/.../Profiles)?
?????????????????????????????
              ?
              ?
?????????????????????????????
?  Final Binding Dictionary ?  Ready for button lookups
?  actions["flight-v_pitch"]?
?????????????????????????????
```

### Binding Structure

```csharp
public class Action
{
    public string Name { get; set; }          // "spaceship_flight-v_pitch"
    public string UILabel { get; set; }       // "Pitch"
    public string Keyboard { get; set; }      // "w" or "lalt+f"
    public string Mouse { get; set; }         // "mouse1" or null
    public string Joystick { get; set; }      // Joystick binding (not used)
    public bool KeyboardOverRule { get; set; } // True if user customized
}
```

---

## Data Flow

### Button Press ? Game Input

```
1. User presses Stream Deck button
   ?
2. Stream Deck software sends WebSocket message
   ?
3. KeyPressed() called on button action
   ?
4. EnsureBindingsReady() + TryGetKeyBinding()
   ?
5. CommandTools.ConvertKeyString() ? "{DikW}"
   ?
6. StreamDeckCommon.SendKeypressDown("{DikW}")
   ?
7. InputSimulator sends Windows keyboard event
   ?
8. Star Citizen receives key input
```

### Binding Reload (File Change)

```
1. User changes binding in SC Options
   ?
2. SC writes to actionmaps.xml
   ?
3. KeyBindingWatcher detects file change
   ?
4. Debounce timer (200ms) fires
   ?
5. KeyBindingService.QueueReload()
   ?
6. FifoExecution queues LoadBindings() on background thread
   ?
7. DProfileReader re-parses XML
   ?
8. KeyBindingsLoaded event fires
   ?
9. All buttons refresh their dropdowns
```

---

## Key Concepts

### Macro Format

Internal representation of keypresses:

```
"lalt+f"   ? "{DikLalt}{DikF}"
"np_enter" ? "{DikNumpadenter}"
"mouse1"   ? "{mouse1}"
```

### DirectInput Keycodes

The plugin uses DirectInput scancodes (not virtual keys) for reliable game input:

```csharp
public enum DirectInputKeyCode
{
    DikA = 0x1E,
    DikW = 0x11,
    DikLalt = 0x38,
    DikNumpad0 = 0x52,
    // ...
}
```

### Property Inspector Communication

WebSocket JSON messages between plugin and HTML settings UI:

```javascript
// Plugin ? PI (sending function list)
{
  "functionsLoaded": true,
  "functions": [
    {
      "label": "Flight - Movement",
      "options": [
        { "value": "spaceship_flight-v_pitch", "text": "Pitch [W]" }
      ]
    }
  ]
}

// PI ? Plugin (settings changed)
{
  "property_inspector": "propertyInspectorConnected"
}
```

---

## Design Patterns

### Singleton Pattern

Used for services that should have exactly one instance:

- `KeyBindingService.Instance` - Binding management
- `AudioPlaybackEngine.Instance` - Sound playback

### Template Method Pattern

`StarCitizenKeypadBase` provides the skeleton with hooks:

- `WirePropertyInspectorEvents()` - Call in constructor
- `SendFunctionsToPropertyInspector()` - Override for custom behavior
- `Dispose()` - Cleanup hooks

### Strategy Pattern

Different button actions implement the same interface with different behaviors:

- `KeyPressed()` / `KeyReleased()` - Customized per action type

### Observer Pattern

Event-based notifications for state changes:

- `KeyBindingsLoaded` - Bindings refreshed
- `OnPropertyInspectorDidAppear` - PI opened
- `OnSendToPlugin` - Message from PI

---

## Troubleshooting

### Log File

Check `%appdata%\Elgato\StreamDeck\Plugins\com.ltmajor42.starcitizen.sdPlugin\pluginlog.log`

### Common Issues

| Symptom | Cause | Solution |
|---------|-------|----------|
| No functions in dropdown | Bindings failed to load | Check pluginlog.log for path errors |
| Keys don't work in-game | SC not focused | Click game window first |
| Wrong keys sent | Locale mismatch | Check keyboard layout |
| Bindings stale | actionmaps.xml locked | Restart plugin after SC closes |
| Button crashes on load | Empty string in settings | Use string types for PI numeric fields |

### Debug CSV Files

When `EnableCsvExport=true` in appsettings.config:
- `keybindings.csv` - All keyboard bindings
- `mousebindings.csv` - All mouse bindings
- `unboundactions.csv` - Actions without bindings

---

## See Also

- [README.md](../README.md) - User guide and button descriptions
- [CHANGELOG.md](../CHANGELOG.md) - Version history
- [CONTRIBUTING.md](../CONTRIBUTING.md) - Developer guide
