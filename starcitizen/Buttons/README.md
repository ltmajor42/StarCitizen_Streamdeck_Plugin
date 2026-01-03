# Button Actions

This folder contains all Stream Deck button action implementations.

---

## Class Hierarchy

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
            ??? CosmeticKey
            ??? LegacyActionKey (backward compatibility)

EncoderBase (BarRaider SDK)
    ??? StarCitizenDialBase
            ??? Dial
```

---

## Base Classes

### StarCitizenKeypadBase.cs

Base class for standard keypad buttons providing shared infrastructure:

**Services:**
- `BindingService` - Reference to KeyBindingService singleton

**Property Inspector Infrastructure:**
- `WirePropertyInspectorEvents()` - Sets up PI event handlers
- `UnwirePropertyInspectorEvents()` - Cleans up PI handlers
- `SendFunctionsToPropertyInspector()` - Override for custom behavior

**Sound Playback:**
- `LoadClickSound(string path)` - Loads a sound file
- `LoadClickSoundFromSettings<T>(T settings)` - Loads from ISoundSettings
- `PlayClickSound()` - Plays the loaded click sound
- `PlaySound(CachedSound sound)` - Plays any cached sound
- `TryLoadSound(string path, out string normalized)` - Safe sound loading

**Key Binding Helpers:**
- `TryGetKeyBinding(string function, out string keyInfo)` - Gets key macro
- `EnsureBindingsReady()` - Checks if bindings are loaded

### StarCitizenDialBase.cs

Base class for Stream Deck+ dial encoders. Handles rotation, press, and touch events.

---

## Settings Interfaces

All button settings can implement these interfaces for consistent behavior:

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

---

## Button Actions

| File | Action ID | Description |
|------|-----------|-------------|
| `ActionKey.cs` | `static` | Standard key-down/key-up on press/release |
| `Momentary.cs` | `momentary` | One-shot with temporary visual feedback |
| `StateMemory.cs` | `statememory` | Toggle with long-press resync capability |
| `DualAction.cs` | `dualaction` | Different actions on press vs release |
| `Repeataction.cs` | `holdrepeat` | Repeats while button is held |
| `ActionDelay.cs` | `actiondelay` | Delayed execution with cancel window |
| `HoldMacroAction.cs` | `holdmacro` | Holds key down until release or timer |
| `CosmeticKey.cs` | `cosmetickey` | Visual-only, no action sent |
| `Dial.cs` | `dial` | Stream Deck+ dial encoder |
| `LegacyActionKey.cs` | `static` (mhwlng) | Backward compatibility wrapper |

---

## Utilities

### StreamDeckCommon.cs

Shared input simulation utilities:
- `SendKeypress(macro, delay)` - Full press with delay
- `SendKeypressDown(macro)` - Key down only
- `SendKeypressUp(macro)` - Key up only

### FunctionListBuilder.cs

Builds the function dropdown data for Property Inspector UIs. Features:
- Caches results by binding version
- Groups functions by category
- Shows bound keys in display text

### StreamDeckEventArgsExtensions.cs

Extension method to extract JObject payload from Stream Deck events:
```csharp
var payload = e.ExtractPayload();
```

---

## Common Patterns

### Action Constructor Pattern

```csharp
public MyAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
{
    settings = PluginSettings.CreateDefaultSettings();
    
    if (payload.Settings?.Count > 0)
    {
        Tools.AutoPopulateSettings(settings, payload.Settings);
        ParseSettings();  // For string ? int conversion
        LoadClickSoundFromSettings(settings);
    }
    
    WirePropertyInspectorEvents();
    SendFunctionsToPropertyInspector();
}
```

### Settings with Numeric Values

**Important:** Use `string` for numeric settings from Property Inspector to handle empty values:

```csharp
protected class PluginSettings : PluginSettingsBase
{
    // ? Correct: string prevents crash on empty value
    [JsonProperty(PropertyName = "delay")]
    public string Delay { get; set; } = "1000";
}

private void ParseSettings()
{
    if (int.TryParse(settings.Delay, out var parsed) && parsed >= 0)
        currentDelay = parsed;
    else
        currentDelay = DefaultDelay;
}
```

### Key Event Pattern

```csharp
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
```

---

## Creating a New Action

See [CONTRIBUTING.md](../../CONTRIBUTING.md#adding-a-new-button-action) for a step-by-step guide.

---

## See Also

- [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md) - Full architecture guide
- [Core/README.md](../Core/README.md) - Core services documentation
- [PropertyInspector/StarCitizen/](../PropertyInspector/StarCitizen/) - HTML settings UIs
