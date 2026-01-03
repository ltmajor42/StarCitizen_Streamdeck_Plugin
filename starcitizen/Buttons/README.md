# Button Actions

This folder contains all Stream Deck button action implementations.

## Base Classes

### StarCitizenKeypadBase.cs
Base class for standard keypad buttons. Handles common lifecycle, OnTick, and settings persistence.

### StarCitizenDialBase.cs
Base class for Stream Deck+ dial encoders. Handles rotation, press, and touch events.

## Button Actions

| File | Action ID | Description |
|------|-----------|-------------|
| `ActionKey.cs` | `actionkey` | Standard key-down/key-up on press/release |
| `Momentary.cs` | `momentary` | One-shot with temporary visual feedback |
| `StateMemory.cs` | `statememory` | Toggle with long-press resync capability |
| `DualAction.cs` | `dualaction` | Different actions on press vs release |
| `Repeataction.cs` | `holdrepeat` | Repeats while button is held |
| `ActionDelay.cs` | `actiondelay` | Delayed execution with cancel window |
| `HoldMacroAction.cs` | `holdmacro` | Holds key down until release or timer |
| `CosmeticKey.cs` | `cosmetic` | Visual-only, no action sent |
| `Dial.cs` | `dial` | Stream Deck+ dial encoder |

## Utilities

### StreamDeckCommon.cs
Shared input simulation utilities:
- `SendKeypress(macro, delay)` - Full press with delay
- `SendKeypressDown(macro)` - Key down only
- `SendKeypressUp(macro)` - Key up only

### FunctionListBuilder.cs
Builds the function dropdown data for Property Inspector UIs. Caches results by binding version.

### StreamDeckEventArgsExtensions.cs
Extension method to extract JObject payload from Stream Deck events.

## Creating a New Action

See [CONTRIBUTING.md](../../CONTRIBUTING.md#adding-a-new-button-action) for a step-by-step guide.

## See Also

- [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md) - Full architecture guide
- [PropertyInspector/StarCitizen/](../PropertyInspector/StarCitizen/) - HTML settings UIs
