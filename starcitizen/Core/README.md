# Core Services

This folder contains the core infrastructure and services that power the plugin.

---

## Architecture Overview

```
???????????????????????????????????????????????????????????????
?                    Button Actions                            ?
?  (ActionKey, Momentary, StateMemory, etc.)                  ?
???????????????????????????????????????????????????????????????
                          ?
                          ?
???????????????????????????????????????????????????????????????
?                    Core Services                             ?
?  KeyBindingService ?? CommandTools ?? MouseTokenHelper      ?
?  PluginLog           FifoExecution    KeyboardLayouts       ?
?  PropertyInspectorMessenger                                  ?
???????????????????????????????????????????????????????????????
```

---

## Files

### KeyBindingService.cs

**Singleton service that manages Star Citizen key binding data.**

Responsibilities:
- Loads bindings from `Data.p4k` (defaultProfile.xml)
- Merges user customizations from `actionmaps.xml`
- Watches for file changes and auto-reloads
- Provides `TryGetBinding()` for all button actions
- Raises `KeyBindingsLoaded` event when bindings refresh

Usage:
```csharp
// Initialize at startup
KeyBindingService.Instance.Initialize();

// Get a binding
var service = KeyBindingService.Instance;
if (service.TryGetBinding("spaceship_flight-v_pitch", out var action))
{
    var keyboard = action.Keyboard; // e.g., "w" or "lalt+f"
}

// Subscribe to reload events
service.KeyBindingsLoaded += (s, e) => RefreshUI();
```

### KeyBindingWatcher.cs

**FileSystemWatcher that monitors actionmaps.xml for changes.**

Features:
- Debounces rapid file changes (200ms delay)
- Uses polling as fallback for missed events
- Compares file hash to detect silent updates
- Handles Star Citizen's write timing with retry logic

### PluginLog.cs

**Centralized logging utility with file rotation.**

Writes to both Stream Deck's internal log and a plugin-specific file at:
```
%appdata%\Elgato\StreamDeck\Plugins\com.ltmajor42.starcitizen.sdPlugin\pluginlog.log
```

Features:
- Automatic log rotation at 5MB
- Thread-safe file writes
- Multiple log levels (Debug, Info, Warn, Error, Fatal)

Usage:
```csharp
PluginLog.Info("Starting key binding load...");
PluginLog.Warn("actionmaps.xml not found");
PluginLog.Error($"Failed to parse: {ex.Message}");
PluginLog.Debug("Detailed trace (enabled in debug builds)");
```

### PropertyInspectorMessenger.cs

**Sends function list data to Property Inspector UIs.**

All button actions call this to populate their function dropdowns:
```csharp
PropertyInspectorMessenger.SendFunctionsAsync(Connection);
```

Handles loading state automatically - if bindings aren't ready yet, sends a "loading" message to the PI.

### CommandTools.cs

**Converts Star Citizen key tokens to DirectInput keycodes.**

Features:
- Fast dictionary-based token lookup
- Conversion caching for performance
- Mouse token handling
- Locale-specific display formatting

Usage:
```csharp
// Convert SC binding to macro format
string macro = CommandTools.ConvertKeyString("lalt+f");
// Returns: "{DikLalt}{DikF}"

// Validate a key token
if (CommandTools.TryFromSCKeyboardCmd("np_enter", out var dikCode))
{
    // dikCode = DirectInputKeyCode.DikNumpadenter
}
```

### MouseTokenHelper.cs

**Normalizes and validates mouse token strings.**

Handles various formats from Star Citizen bindings:
- `mouse1`, `mousebutton1`, `Button 1` ? `mouse1`
- `mwheelup`, `mousewheelup`, `wheelup` ? `mwheelup`
- `mouse1+mouse2` ? composite handling

### KeyboardLayouts.cs

**Detects keyboard layout for localized key display.**

Used by FunctionListBuilder to show proper key names in different locales (e.g., QWERTZ for German keyboards).

### FifoExecution.cs

**Modern async work queue using System.Threading.Channels.**

Features:
- Lock-free, async-friendly processing
- Single reader for FIFO guarantee
- Proper execution context capture

Used by KeyBindingService to queue binding reloads without blocking.

---

## Usage in Button Actions

Button actions use core services through the `StarCitizenKeypadBase` base class:

```csharp
public class MyAction : StarCitizenKeypadBase
{
    public MyAction(SDConnection connection, InitialPayload payload) 
        : base(connection, payload)
    {
        // BindingService is available from base class
        // Wire up PI events using base class helper
        WirePropertyInspectorEvents();
    }

    public override void KeyPressed(KeyPayload payload)
    {
        // Use base class helpers
        if (!EnsureBindingsReady()) return;

        if (TryGetKeyBinding(settings.Function, out var keyInfo))
        {
            StreamDeckCommon.SendKeypressDown(keyInfo);
        }
    }
}
```

---

## See Also

- [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md) - Full architecture guide
- [CONTRIBUTING.md](../../CONTRIBUTING.md) - Developer guide
- [Buttons/README.md](../Buttons/README.md) - Button action documentation
