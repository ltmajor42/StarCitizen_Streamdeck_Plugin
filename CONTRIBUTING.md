# Contributing to Star Citizen Stream Deck Plugin

Thank you for your interest in contributing! This guide will help you get started with development.

---

## Table of Contents

1. [Development Setup](#development-setup)
2. [Building the Plugin](#building-the-plugin)
3. [Testing Changes](#testing-changes)
4. [Adding a New Button Action](#adding-a-new-button-action)
5. [Code Style Guidelines](#code-style-guidelines)
6. [Submitting Changes](#submitting-changes)

---

## Development Setup

### Prerequisites

- **Visual Studio 2022** (Community edition is fine)
- **.NET Framework 4.8 SDK**
- **Elgato Stream Deck Software** (v6.0 or later)
- **Star Citizen** installed (for testing)

### Clone and Open

```bash
git clone https://github.com/ltmajor42/StarCitizen_Streamdeck_Plugin.git
cd StarCitizen_Streamdeck_Plugin
```

Open `starcitizen.sln` in Visual Studio.

### Solution Structure

| Project | Purpose |
|---------|---------|
| `starcitizen` | Main plugin project |
| `WindowsInput` | Input simulation library (InputSimulator fork) |
| `ICSharpCode.SharpZipLib` | ZIP/compression for p4k extraction |
| `Zstd.Net` | Zstandard decompression for p4k |

---

## Building the Plugin

### Debug Build

1. Set configuration to **Debug** and platform to **Any CPU**
2. Press **F5** or click **Start**
3. Output goes to: `starcitizen\bin\Debug\com.ltmajor42.starcitizen.sdPlugin\`

### Release Build

1. Set configuration to **Release** and platform to **x64**
2. Build ? Build Solution
3. Output goes to: `starcitizen\bin\Release\com.ltmajor42.starcitizen.sdPlugin\`

### Installing for Testing

1. Close Stream Deck software
2. Delete existing plugin folder:
   ```
   %appdata%\Elgato\StreamDeck\Plugins\com.ltmajor42.starcitizen.sdPlugin
   ```
3. Copy your build output to that location
4. Restart Stream Deck software

### Quick Test Cycle

For rapid iteration, you can use symbolic links:

```powershell
# Run as Administrator
$src = "C:\path\to\repo\starcitizen\bin\Debug\com.ltmajor42.starcitizen.sdPlugin"
$dest = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.ltmajor42.starcitizen.sdPlugin"
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
cmd /c mklink /D "$dest" "$src"
```

---

## Testing Changes

### Viewing Logs

Plugin logs are written to:
```
%appdata%\Elgato\StreamDeck\Plugins\com.ltmajor42.starcitizen.sdPlugin\pluginlog.log
```

Use PowerShell to tail the log:
```powershell
Get-Content "$env:APPDATA\Elgato\StreamDeck\Plugins\com.ltmajor42.starcitizen.sdPlugin\pluginlog.log" -Wait -Tail 50
```

### Debugging with Visual Studio

1. Build in Debug mode
2. Install the plugin (copy to StreamDeck folder)
3. Start Stream Deck software
4. In Visual Studio: **Debug ? Attach to Process**
5. Find `com.ltmajor42.starcitizen.exe` and attach
6. Set breakpoints and test

### Testing Binding Reload

1. Open Star Citizen Options ? Keybindings
2. Change a binding
3. Watch `pluginlog.log` for "KeyBindingsLoaded" message
4. Verify the button dropdown updates

---

## Adding a New Button Action

Follow this step-by-step guide to add a new action type.

### 1. Create the Action Class

Create a new file in `Buttons/` folder:

```csharp
using System;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using starcitizen.Core;

namespace starcitizen.Buttons
{
    /// <summary>
    /// Description of what this action does.
    /// </summary>
    [PluginActionId("com.ltmajor42.starcitizen.myaction")]
    public class MyAction : StarCitizenKeypadBase
    {
        // ============================================================
        // REGION: Settings
        // ============================================================
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings() => new PluginSettings
            {
                Function = string.Empty
            };

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }
        }

        // ============================================================
        // REGION: State
        // ============================================================
        private PluginSettings settings;
        private readonly KeyBindingService bindingService = KeyBindingService.Instance;

        // ============================================================
        // REGION: Initialization
        // ============================================================
        public MyAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();
            }

            Connection.OnPropertyInspectorDidAppear += OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += OnSendToPlugin;
            bindingService.KeyBindingsLoaded += OnKeyBindingsLoaded;

            UpdatePropertyInspector();
        }

        // ============================================================
        // REGION: Key Events
        // ============================================================
        public override void KeyPressed(KeyPayload payload)
        {
            if (bindingService.Reader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            if (!bindingService.TryGetBinding(settings.Function, out var action)) return;

            var keyString = CommandTools.ConvertKeyString(action.Keyboard);
            if (!string.IsNullOrWhiteSpace(keyString))
            {
                StreamDeckCommon.SendKeypressDown(keyString);
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!bindingService.TryGetBinding(settings.Function, out var action)) return;

            var keyString = CommandTools.ConvertKeyString(action.Keyboard);
            if (!string.IsNullOrWhiteSpace(keyString))
            {
                StreamDeckCommon.SendKeypressUp(keyString);
            }
        }

        // ============================================================
        // REGION: Settings
        // ============================================================
        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(settings, payload.Settings);
        }

        // ============================================================
        // REGION: Property Inspector
        // ============================================================
        private void OnPropertyInspectorDidAppear(object sender, EventArgs e) => UpdatePropertyInspector();
        private void OnKeyBindingsLoaded(object sender, EventArgs e) => UpdatePropertyInspector();

        private void OnSendToPlugin(object sender, EventArgs e)
        {
            try
            {
                var payload = e.ExtractPayload();
                if (payload?["property_inspector"]?.ToString() == "propertyInspectorConnected")
                    UpdatePropertyInspector();
            }
            catch (Exception ex)
            {
                PluginLog.Warn($"Failed processing PI payload: {ex.Message}");
            }
        }

        private void UpdatePropertyInspector()
        {
            if (bindingService.Reader == null) return;
            PropertyInspectorMessenger.SendFunctionsAsync(Connection);
        }

        // ============================================================
        // REGION: Disposal
        // ============================================================
        public override void Dispose()
        {
            Connection.OnPropertyInspectorDidAppear -= OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= OnSendToPlugin;
            bindingService.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }
    }
}
```

### 2. Create the Property Inspector HTML

Create `PropertyInspector/StarCitizen/MyAction.html`:

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>My Action</title>
    <link rel="stylesheet" href="../sdpi.css">
    <script src="../sdtools.common.js"></script>
</head>
<body>
    <div class="sdpi-wrapper">
        <div type="none" class="sdpi-item">
            <div class="sdpi-item-label">My Action</div>
            <div class="sdpi-item-value"><span class="sdpi-item-value">Configure your action</span></div>
        </div>

        <div type="select" class="sdpi-item">
            <div class="sdpi-item-label">Function</div>
            <select class="sdpi-item-value select" id="function" onchange="setSettings()">
                <option value="" disabled selected>Loading functions...</option>
            </select>
        </div>
    </div>

    <script>
        // Populate function dropdown when data arrives
        function populateFunctions(functionsData) {
            const select = document.getElementById('function');
            const savedValue = select.value;
            
            select.innerHTML = '';
            
            functionsData.forEach(group => {
                const optgroup = document.createElement('optgroup');
                optgroup.label = group.label;
                
                group.options.forEach(opt => {
                    const option = document.createElement('option');
                    option.value = opt.value;
                    option.textContent = opt.text;
                    optgroup.appendChild(option);
                });
                
                select.appendChild(optgroup);
            });
            
            if (savedValue) select.value = savedValue;
        }

        // Handle messages from plugin
        window.addEventListener('message', function(event) {
            if (event.data && event.data.functions) {
                populateFunctions(event.data.functions);
            }
        });
    </script>
</body>
</html>
```

### 3. Add to manifest.json

Add your action to the `Actions` array:

```json
{
    "Icon": "Images/MyAction",
    "Name": "My Action",
    "States": [
        { "Image": "Images/MyAction0" },
        { "Image": "Images/MyAction1" }
    ],
    "SupportedInMultiActions": true,
    "Tooltip": "Description of what this action does",
    "UUID": "com.ltmajor42.starcitizen.myaction",
    "PropertyInspectorPath": "PropertyInspector/StarCitizen/MyAction.html"
}
```

### 4. Add Icon Images

Add to `Images/` folder:
- `MyAction.png` - Category icon (144x144)
- `MyAction0.png` - Idle state (144x144)
- `MyAction1.png` - Active state (144x144)

### 5. Update .csproj

Add compile entries if not using SDK-style project:

```xml
<Compile Include="Buttons\MyAction.cs" />
<Content Include="PropertyInspector\StarCitizen\MyAction.html">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
<Content Include="Images\MyAction.png">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

---

## Code Style Guidelines

### Regions

Use consistent region markers for organization:

```csharp
// ============================================================
// REGION: Settings
// ============================================================

// ============================================================
// REGION: Key Events
// ============================================================
```

### Documentation

Add XML doc comments to public classes and important methods:

```csharp
/// <summary>
/// Converts Star Citizen key tokens to DirectInput macro format.
/// </summary>
/// <param name="keyboard">SC token like "lalt+f"</param>
/// <returns>Macro format like "{DikLalt}{DikF}"</returns>
public static string ConvertKeyString(string keyboard)
```

### Naming Conventions

- **Classes**: PascalCase (`ActionKey`, `KeyBindingService`)
- **Methods**: PascalCase (`TryGetBinding`, `SendKeypress`)
- **Private fields**: camelCase with underscore prefix for backing fields (`_clickSound`)
- **Local variables**: camelCase (`keyString`, `currentAction`)

### Error Handling

- Use `try/catch` around external operations (file I/O, input simulation)
- Log errors with `PluginLog.Error()` or `PluginLog.Warn()`
- Don't let exceptions propagate to Stream Deck SDK

### Null Checks

Use null-conditional and null-coalescing operators:

```csharp
// Preferred
if (bindingService.Reader == null) return;
var value = payload?.Settings ?? new JObject();

// Avoid verbose null checks
if (bindingService != null && bindingService.Reader != null) ...
```

---

## Submitting Changes

### Before Submitting

1. **Build in Release mode** - Ensure no warnings
2. **Test with Stream Deck** - Verify functionality works
3. **Update CHANGELOG.md** - Document your changes
4. **Format code** - Run Visual Studio's Format Document (Ctrl+K, Ctrl+D)

### Pull Request Process

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes with clear commit messages
4. Push to your fork: `git push origin feature/my-feature`
5. Open a Pull Request against `main` branch
6. Describe what you changed and why

### Commit Message Format

```
Type: Brief description (50 chars or less)

More detailed explanation if needed. Wrap at 72 characters.

Fixes #123
```

Types:
- `Add` - New feature
- `Fix` - Bug fix
- `Refactor` - Code cleanup without behavior change
- `Docs` - Documentation only
- `Style` - Formatting, whitespace

---

## Getting Help

- **Issues**: Open a GitHub issue for bugs or feature requests
- **Discussions**: Use GitHub Discussions for questions
- **Log file**: Always include `pluginlog.log` when reporting issues

Thank you for contributing! ??
