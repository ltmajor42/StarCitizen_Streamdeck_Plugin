# Core Services

This folder contains the core infrastructure and services that power the plugin.

## Files

### KeyBindingService.cs
**Singleton service that manages Star Citizen key binding data.**

Responsibilities:
- Loads bindings from `Data.p4k` (defaultProfile.xml)
- Merges user customizations from `actionmaps.xml`
- Watches for file changes and auto-reloads
- Provides `TryGetBinding()` for all button actions

Usage:
```csharp
var service = KeyBindingService.Instance;
if (service.TryGetBinding("spaceship_flight-v_pitch", out var action))
{
    var keyboard = action.Keyboard; // e.g., "w" or "lalt+f"
}
```

### KeyBindingWatcher.cs
**FileSystemWatcher that monitors actionmaps.xml for changes.**

Features:
- Debounces rapid file changes (200ms delay)
- Uses polling as fallback for missed events
- Compares file hash to detect silent updates

### PluginLog.cs
**Centralized logging utility.**

Writes to both Stream Deck's internal log and a plugin-specific file at:
```
%appdata%\Elgato\StreamDeck\Plugins\com.ltmajor42.starcitizen.sdPlugin\pluginlog.log
```

Usage:
```csharp
PluginLog.Info("Starting key binding load...");
PluginLog.Warn("actionmaps.xml not found");
PluginLog.Error($"Failed to parse: {ex.Message}");
```

### PropertyInspectorMessenger.cs
**Sends function list data to Property Inspector UIs.**

All button actions call this to populate their function dropdowns:
```csharp
PropertyInspectorMessenger.SendFunctionsAsync(Connection);
```

## See Also

- [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md) - Full architecture guide
- [CONTRIBUTING.md](../../CONTRIBUTING.md) - Developer guide
