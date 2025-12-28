# streamdeck-starcitizen (Updated Fork)

**Elgato Stream Deck button plugin for Star Citizen**

> 🔗 **Updated fork of [mhwlng/streamdeck-starcitizen](https://github.com/mhwlng/streamdeck-starcitizen)**  
> Maintained by **Ltmajor42**. Original code by **mhwlng** with in-game binding discovery provided by **SCJMapper** assets. This fork adds auto-detection, search, refactors, and stability fixes.

## What's New in This Fork

- **Automatic RSI Launcher Detection** - (Hopefully) No more manual path configuration! The plugin reads your RSI Launcher settings automatically.
- **Search Functionality** - Quickly find keybindings with the new search box in the Property Inspector.
- **Simplified Configuration** - Only need to set `SCBasePath` if auto-detection fails (instead of SCData_p4k and SCClientProfilePath).
- **Bug Fixes & Improvements** - Various fixes to improve stability and usability.

## Ownership, Credits, and Thanks

- **Maintainer/Author:** Ltmajor42  
- **Upstream inspiration:** [mhwlng/streamdeck-starcitizen](https://github.com/mhwlng/streamdeck-starcitizen)  
- **Binding loader:** [SCJMapper resources](https://github.com/SCToolsfactory/SCJMapper-V2) power the Star Citizen keybind parsing.

## V2 Full Release

V2 is a full release with a **major code refactor** focused on stability, maintainability, and a cleaner Property Inspector experience.

When the plugin starts, it reads Star Citizen bindings and text resources so it can populate the function list. Depending on your install and drive speed, the first load can take a few seconds.

The plugin logs useful startup and detection details in:

`%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin\pluginlog.log`

---

## What’s improved in V2

**This release includes a major internal refactor:**

1. **Stability improvements** - Better behavior under fast tapping / repeated presses
2. **Cleaner structure** - Easier to maintain and evolve long-term
3. **Improved Property Inspector experience** - Faster function selection and more consistent UI
4. **More consistent action behavior** - Actions now follow clearer rules across buttons

### Architecture highlights (v2)

- **Centralized key binding service** – `Core/KeyBindingService` loads bindings, watches `actionmaps.xml`, and caches versioned results so actions can refresh safely.
- **Shared Property Inspector messaging** – `Core/PropertyInspectorMessenger` sends the current function list to any action without copy/paste code.
- **Single logging entry point** – `Core/PluginLog` wraps `BarRaider.SdTools.Logger` to keep troubleshooting messages consistent in `pluginlog.log`.
- **Lean surface** – Legacy template generators have been removed; Property Inspectors live directly under `PropertyInspector/StarCitizen/`.

### Adding a new action (quick guide)

1. **Create the action class** – Derive from `StarCitizenKeypadBase` or `StarCitizenDialBase` and keep a private `KeyBindingService bindingService = KeyBindingService.Instance;`.
2. **Wire key binding lookups** – Use `bindingService.TryGetBinding(settings.Function, out var action)` before sending any keypress.
3. **Send PI data** – Subscribe to `bindingService.KeyBindingsLoaded`, `Connection.OnPropertyInspectorDidAppear`, and `OnSendToPlugin`, then call `PropertyInspectorMessenger.SendFunctionsAsync(Connection)` to populate dropdowns.
4. **Log consistently** – Use `PluginLog.Info/Warn/Error/Fatal` for any runtime issues; they land in `pluginlog.log` for easy troubleshooting.
5. **Keep settings minimal** – Prefer simple `PluginSettings` classes with `Tools.AutoPopulateSettings` to minimize boilerplate.

### Troubleshooting

- Live log path: `%appdata%\\Elgato\\StreamDeck\\Plugins\\com.mhwlng.starcitizen.sdPlugin\\pluginlog.log`
- Ensure the **RSI Launcher** is installed and has launched at least once so bindings can be read.
- If functions are missing, close and reopen the Stream Deck app to trigger a fresh `actionmaps.xml` load, or delete stale profiles inside `%appdata%\\Elgato\\StreamDeck\\Plugins\\...`.

---

## Buttons (Actions) — What they do

### ActionKey
<img width="100" height="100" alt="ActionKey" src="https://github.com/user-attachments/assets/fafae77c-add4-4a1c-bb84-808b9ef9f7ef" />

Sends the selected Star Citizen function (keyboard bind).

**Use cases:**

1. Standard “press to trigger” commands
2. Any binding you want to behave like a normal key press

---

### State Memory (Soft Sync Toggle)
<img width="100" height="100" alt="Statememory1" src="https://github.com/user-attachments/assets/13b05164-f0cb-4273-bab5-ed49c1e4f3b7" />
<img width="100" height="100" alt="Statememory0" src="https://github.com/user-attachments/assets/82f290bc-6eac-4cf1-a1df-3c9022b01f12" />

A dedicated toggle-style action designed for Star Citizen systems with **no reliable state feedback**.

**Behavior:**

1. **Short press** - Sends the selected Star Citizen keybind and flips the internal ON/OFF state
2. **Long press** - Flips the internal ON/OFF state only (manual “resync” without sending a key)

**Use cases:**

1. Lights, landing gear, VTOL, doors, power toggles
2. Situations where the real in-game state can change outside Stream Deck and you want a quick manual resync

---

### Momentary
<img width="100" height="100" alt="Momentary1" src="https://github.com/user-attachments/assets/be64760f-0e84-4a54-87d7-75cc7c33c1d1" />
<img width="100" height="100" alt="Momentary0" src="https://github.com/user-attachments/assets/b4a27ad1-23d9-4dd6-8d83-43d4f323de38" />

A one-shot action with temporary visual feedback.

**Behavior:**

1. Press sends the selected function once
2. Switches to an active image state briefly
3. Returns to idle image state after the configured delay

**Use cases:**

1. Engine start / system triggers
2. Confirmation actions
3. Immersion feedback without a persistent toggle

---

### Dual Action
<img width="100" height="100" alt="Dualaction1" src="https://github.com/user-attachments/assets/41fb4c7d-7b87-445d-baf2-3ab460528db0" />
<img width="100" height="100" alt="Dualaction0" src="https://github.com/user-attachments/assets/971f4f90-dea1-42bb-82a4-774335997d02" />

Two-stage behavior tied to press and release.

**Behavior:**

1. **Press (key down)** triggers Action A
2. **Release (key up)** triggers Action B
3. Optional 2-state image behavior (pressed vs released)

**Use cases:**

1. Hold-to-engage / release-to-disengage patterns
2. Temporary modes
3. Spool / charge style workflows (depending on your binds)

---

### Action Delay
<img width="100" height="100" alt="ActionDelay1" src="https://github.com/user-attachments/assets/17bf3045-8aa5-4034-a2f4-2fb993e1c3f5" />
<img width="100" height="100" alt="ActionDelay0" src="https://github.com/user-attachments/assets/fcf0cc43-9754-4d52-a64f-d2b45aef9c57" />

A timed action with a cancel window.

**Behavior:**

1. Tap starts a timer and shows an “armed/pending” state
2. When the delay ends, the selected function triggers and the button shows an “executed” state briefly
3. Then it returns to idle
4. Tap again while pending cancels the timer

**Use cases:**

1. Safer actions (gives you time to cancel before it fires)
2. Timed sequences where you want a clear “armed → executed → idle” flow
3. Any workflow where a delayed trigger feels better than an instant press

---

### RepeatAction
<img width="100" height="100" alt="Repeataction1" src="https://github.com/user-attachments/assets/e39a4a3a-6cce-4ebf-adcc-b2acac57d9a5" />
<img width="100" height="100" alt="Repeataction0" src="https://github.com/user-attachments/assets/691bb7d0-4104-4cfe-bd02-ced60fa86cef" />

Press-and-hold repeat behavior.

**Behavior:**

1. Press triggers immediately
2. While held, repeats the selected function at the configured interval
3. Stops instantly on release and returns to idle image

**Use cases:**

1. Power / cooling / shield management adjustments
2. Increment/decrement controls where holding feels better than tapping repeatedly

---

### Cosmetic
<img width="100" height="100" alt="Cosmetic" src="https://github.com/user-attachments/assets/6891af8c-352b-4c2c-81d4-335df945e9b8" />

Visual-only tile (no keybind, no action).

**Use cases:**

1. Section headers and separators
2. Organizing pages and profiles
3. Aesthetic/branding tiles

---

## Reporting issues (please include this)

If something is wrong or inconsistent, open an Issue and include:

1. Plugin version (V2.x) and Stream Deck version
2. Which button (ActionKey / State Memory / Momentary / Dual Action / Action Delay / Hold Repeat / Cosmetic)
3. Exact steps to reproduce (what you pressed, how fast, expected vs actual)
4. The log file:

`%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin\pluginlog.log`

If the report includes clear steps + the log, I can reproduce it and fix it much faster.


---
# Original README:

**At least streamdeck software version 6 is required.**

This plugin gets the key bindings from the Star Citizen game files.

The bindings in the streamdeck plugin are automatically updated when changing bindings in Star Citizen options screen.

The bound key is shown in the dropdown, localised for the current keyboard language (So: US keyboard shows WSAD, French keyboard shows ZSQD)

**The plugin does not contain any button images or ready made streamdeck profiles.**

Credit goes to https://github.com/SCToolsfactory/SCJMapper-V2 and https://github.com/dolkensp/unp4k for all the code to get the `defaultProfile.xml` from the p4k file etc.

The static button works in a similar way, to the streamdeck 'Hotkey' button type.
So, there is only one image and there is no game state feedback for these buttons.
The differences with the 'Hotkey' buttons are, that it gets the keyboard binding from the game.
When the stream deck button is pushed, the 'key down' event is sent to the keyboard
and only after the stream deck button is released, the 'key up' event is sent to the keyboard.

The plugin's multi-action button behaviour is different : when the stream deck button is pushed, the 'key down' event is sent to the keyboard.
After a user-definable delay (default = 40 ms) the 'key up' event is sent to the keyboard. 
Nothing happens when the streamdeck button is released.

Both the static- and multi-action buttons can be used inside the streamdeck built-in multi-action button's action list.

The multi-action button can also be used as a regular streamdeck button, in case a fixed user-definable delay is required between the key down and key up events.

A sound can be played when pressing a button.

**You can clear the sound path, by clicking on the label in front of the file picker edit box.**

You can, for example, use the 'multi-action switch' function, that is built into the streamdeck software, to set up a toggle function.
You can add the relevant function of this plugin to both the ON-and OFF-action of the 'multi-action switch' function.
You can then set up different images for each toggle state.
The disadvantage is: that if you would press e.g. the gear up/down toggle button while the ship is still on the ground/powered off, 
then the button image would be out of sync.

The plugin also has a Dial button for use with the 4 dials on the Streamdeck+ model. You’ll find it under the **Dials** section of the Stream Deck+ action list (look for the Star Citizen category with the dial icon).

There are 5 bindings (They must be keyboard bindings, you can't bind the mouse wheel!) :

- Dial Clockwise
- Dial Counter-Clockwise
- Dial Press
- Touch screen press
- Touch screen long press

![Dial Image](https://i.imgur.com/MjcGque.png)

When a dial is rotated, the 'key down' event is sent to the keyboard once. 
When you let go of the dial for at least 100ms : the 'key up' event is sent to the keyboard. 

When a dial button is pushed, the 'key down' event is sent to the keyboard. 
When a dial button is released, the 'key up' event is sent to the keyboard. 

When the touch screen is pressed or long-pressed, the behaviour is like the multi-action button :
The 'key down' event is sent to the keyboard. After a user-definable delay (default = 40 ms) the 'key up' event is sent to the keyboard. 

After you install the plugin in the streamdeck software, then there will be new button types in the streamdeck software.

Choose a button in the streamdeck software (drag and drop), then choose a Star Citizen function for that button 
(that must have a keyboard binding in Star Citizen. **A mouse, gamepad or joystick binding won't work!**) 
and then choose any picture for that button.

Add an image to a button in this way:

![Button Image](https://i.imgur.com/xkgy7uZ.png)

Animated gif files are supported. Dial images are 200x50

When the plugin is first started, it finds and opens the game file :

`C:\Program Files\Roberts Space Industries\StarCitizen\LIVE\Data.p4k`

and extracts `defaultProfile.xml` and also english text resources. This could take more than 10 seconds.

**The plugin automatically detects Star Citizen installations using multiple methods:**

1. **Registry-based detection** - Scans Windows registry for launcher installation paths
2. **Common path scanning** - Checks standard installation directories
3. **Steam integration** - Detects Steam installations
4. **Manual configuration** - User can specify paths in config file

The plugin logs its path detection process in `%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin\pluginlog.log`.

## Manual Configuration

If automatic detection fails, edit `%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin\appsettings.config`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<appSettings>
  <!-- Base Star Citizen installation directory -->
  <add key="SCBasePath" value="C:\Program Files\Roberts Space Industries\StarCitizen" />

  <!-- Path to Data.p4k file (contains game assets and default keybindings) -->
  <add key="SCData_p4k" value="C:\Program Files\Roberts Space Industries\StarCitizen\StarCitizen\LIVE\Data.p4k" />

  <!-- Path to user profile directory (contains custom keybindings) -->
  <add key="SCClientProfilePath" value="C:\Program Files\Roberts Space Industries\StarCitizen\StarCitizen\LIVE\USER\Client\0\Profiles\default" />

  <!-- Set to true for PTU (Public Test Universe) instead of LIVE -->
  <add key="UsePTU" value="false" />
</appSettings>
```

Compressed versions (files ending in .scj) are cached in the plugin directory and should be automatically refreshed, the next time Star Citizen is updated to a new version AND the plugin is also restarted.

You can also delete the .scj files and restart the plugin, to extract the files from the p4k file again.

For easier debugging, installation and testing, `defaultProfile.xml`, `keybindings.csv`, `joystickbindings.csv`, `mousebindings.csv`, `unboundactions.csv` and `PropertyInspector\StarCitizen\ActionKey.html` files are created in the plugin directory.

The plugin uses all the active keyboard bindings from `defaultProfile.xml` and then overrules some of the bindings, with any custom keyboard bindings from this file :

`C:\Program Files\Roberts Space Industries\StarCitizen\LIVE\USER\Client\0\Profiles\default\actionmaps.xml`

The `PropertyInspector\StarCitizen\ActionKey.html` file is dynamically updated, in case more custom keyboard bindings were added to `actionmaps.xml`, 
that didn't have any corresponding keyboard bindings in `defaultProfile.xml`.

If nothing happens, when pressing streamdeck buttons: you could try to start streamdeck.exe as administrator.

The plugin installer is here: https://github.com/mhwlng/streamdeck-starcitizen/releases

To install the plugin, double click the file `com.mhwlng.starcitizen.streamDeckPlugin` which should install the plugin.

(This only works, if the plugin not already installed. Otherwise you will need to uninstall or remove the plugin first.)

This .streamDeckPlugin file is a zip file and the contents are simply copied to :

`%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin`

To update to a new version :

Stop the Stream Deck application:

`c:\Program Files\Elgato\StreamDeck\StreamDeck.exe`

Then delete the `%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin` directory. (make a backup copy first)

Then start the streamdeck software again.

Then double click the file `com.mhwlng.starcitizen.streamDeckPlugin` as usual.

MAKE SURE that you save any images, profiles etc. that you put in these directories yourself, BEFORE deleting the directory.
And put them back after the installation.
The plugin installer doesn't come with button images.

Also, the plugin can be uninstalled and the directory completely deleted by right-clicking on any button and selecting uninstall (make a backup copy first) :

![Button Image](https://i.imgur.com/BAkEuEq.png)

The button configurations are not stored in the plugin directory.

After uninstalling and re-installing the plugin, all the button definition should still be there.

The com.mhwlng.starcitizen.sdPlugin directory contains a pluginlog.log file, which may be useful for troubleshooting.

Thanks to :

https://github.com/BarRaider/streamdeck-tools

https://github.com/SCToolsfactory/SCJMapper-V2

https://github.com/dolkensp/unp4k

https://github.com/ishaaniMittal/inputsimulator

https://nerdordie.com/product/stream-deck-key-icons/
