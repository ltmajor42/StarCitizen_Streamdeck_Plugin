# Changelog

All notable changes to this repository are documented below. Unreleased changes appear at the top.


## [2.0.6]

### Changes
- Refactored Property Inspector (PI) pages so each inspector is self-contained and resilient.
- Added per-PI JavaScript to reliably receive and render the `functions` payload from the plugin, populate dropdowns (optgroups/options), and preserve saved selections when payloads arrive out-of-order.
- Implemented a shared, debounced search/filter UI across PIs that hides non-matching optgroups and shows a concise "Found X group(s)" summary.
- Hardened file-picker behavior: filename labels and Clear buttons update correctly even if shared init is skipped.
- Consolidated and compacted `PropertyInspector/sdpi.css` to reduce vertical space and minimize PI scrolling while keeping readability.
- Normalized text/encoding (replaced en-dash with ASCII hyphen) in `ActionKey.html` and standardized section titles/subtitles.

Files changed (high level)
- Modified: `PropertyInspector/sdpi.css` — compacting, spacing, file picker and details styles.
- Modified: `PropertyInspector/sdtools.common.js` — ensure PI init and file-picker helpers are robust.
- Modified: `PropertyInspector/StarCitizen/*` — moved search input into configuration sections, added per-PI JS for function population and search/filter, preserved saved selections, restored Function subtitles where missing, normalized titles.

## [2.0.5]

### Changes
- Improved actionmaps refresh and path resolution.
- Handled missing variables during actionmaps read; added `lastReadContent` cache.
- Improved key binding watcher reliability and hashing of actionmaps to detect silent changes.
- Improved search layout and alignment of search inputs with dropdown heights.
- Normalized mouse rebinds as primary actions.

## [2.0.4]

### Changes
- Added Hold Macro action (press-and-hold behavior) and improved repeat/scroll behavior for hold macros.
- Repeat mouse inputs during hold macros; allow repeating mouse wheel scrolls.

## [2.0.3]

### Changes
- Enabled mouse output by default and improved mouse macro support.

## [2.0.2]

### Changes
- Optimized keybinding retrieval and prioritized keyboard bindings.
- Fixes for keybind building and duplicate detection; refined mouse binding handling and wheel aliases.
- Deduplicated FunctionListBuilder helpers and included MouseTokenHelper in build.

## [2.0.1]

### Fixes
- Improved Stream Deck+ dial rotation responsiveness by sending a discrete keypress (down+up) per tick.
- Added queue-based tick processing to handle fast/slow rotation reliably.
- Prevented catch-up lag when reversing direction by clearing stale queued ticks.
- `Delay` controls keypress duration (ms) with sensible bounds.
- Unknown or unsupported key tokens remain marked as unknown and surface in the action dropdown.

## [2.0.0]

### Major Refactor
- Centralized key binding loading, caching, and file watching into `Core/KeyBindingService` with consistent logging via `PluginLog`.
- Standardized Property Inspector updates through `Core/PropertyInspectorMessenger`.
- Removed legacy template artifacts to simplify packaging.

---

## [1.2.0]

### Fixes
- Dial action now explicitly targets the Stream Deck+ encoders and uses a dedicated dial icon, so it shows up correctly in the Stream Deck action list.

## [1.1.8]

### New Features
- **Repeat Action (Hold) Button:**
  - Fires a selected Star Citizen function immediately on press, then repeats it at a configurable rate while held.
  - Includes Idle/Active images that snap back to idle the moment you release.
  - Optional start/stop sounds for tactile audio feedback.
  - Property Inspector exposes function selection, repeat rate (ms), idle/active image pickers, and start/stop sound pickers.

## [1.1.7]

### New Features
- **Dual Action Button (Hold/Release):**
  - Sends one binding on press (held while the button is down) and a second on release.
  - Uses a two-state icon so the key visually shifts while held.
  - Includes optional click sound on press.

## [1.1.6]

### New
- **Cosmetic Key** action (visual-only tile):
  - Adds a new Stream Deck action under the Star Citizen category.
  - Does not send any keybinds or events (purely cosmetic).
  - Uses a custom action icon in the Stream Deck actions list.

## [1.1.5]

### New Features
- **State Memory (Soft Sync Toggle)**:
  - Adds a persistent ON/OFF memory toggle for Star Citizen functions
  - **Short press:** sends keybind + toggles button indicator
  - **Long press:** toggles indicator only (no key sent) for manual soft sync
  - Includes optional **Short Press Sound** and **Long Press Sound**
  - Helps keep Stream Deck button state in sync with in-game systems (landing gear, lights, VTOL, doors, etc.)

### UI Improvements
- Compact Property Inspector layout (less scrolling)
- Hint text integrated directly below Memory toggle
- Two separate file pickers for short and long press sounds

### Technical Changes
- Cleaned up visual state handling and race conditions on rapid presses

## [1.1.4]

### New Features
- **Momentary Button (New Action)**:
  - One-shot Star Citizen keybind execution (non-toggle)
  - Temporary visual state with automatic revert
  - User-configurable delay (milliseconds)
  - Supports two images via Stream Deck UI (idle / active)
  - Function selector with full search support

### Technical Changes
- Added `Momentary.cs` action based on Action Key architecture
- Added `Momentary.html` Property Inspector
- Reused dynamic function loading and WebSocket communication system
- Improved Property Inspector persistence handling for numeric inputs

## [1.1.3a]

### Bug Fixes
- **Buttons**: Corrected an issue where the plugin failed to save a new assigned Function to an Action Key after changing it in the Property Inspector.

### Technical Changes 
- **Simplified Action Key**: Removed obsolete template generation system. The Action Key Property Inspector (`ActionKey.html`) is now maintained directly 
  instead of being generated from a template file, making future updates easier.
  - statictemplate.html is still inside the Plugin for now, will be removed in future versions.

## [1.1.3]

### New Features
- **RSI Launcher Auto-Detection**: The plugin now automatically reads the RSI Launcher configuration files from `%APPDATA%\rsilauncher\` to find your Star Citizen installation path
  - Reads `library_folder.json` for the game library location
  - Reads `settings.json` for installation directories
  - Parses launcher log files as a fallback


- **Currently only available for the Action Key:**
  - **Search Functionality**: Added a search box to find Keybindings faster in the dropdown list
  - **Dynamic Function Loading**: Functions are now loaded dynamically via WebSocket communication instead of hardcoded HTML Option Values

### Technical Changes
- `ActionKey.cs`: Added SDK event handlers (`OnPropertyInspectorDidAppear`, `OnSendToPlugin`) for proper Property Inspector communication
- `ActionKey.html` / `statictemplate.html`: Implemented dynamic dropdown population via JSON WebSocket messages
- `SCPath.cs`: Added `FindInstallationFromRSILauncher()` method and improved `IsValidStarCitizenInstallation()` to support multiple directory structures
- `DProfileReader.cs`: Simplified `CreateStaticHtml()` to just copy template (dropdown now populated dynamically)
