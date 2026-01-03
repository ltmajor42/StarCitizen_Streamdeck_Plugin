using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using BarRaider.SdTools;
using starcitizen.Core;
using TheUser = p4ktest.SC.TheUser;

namespace starcitizen.SC
{
    /// <summary>
    /// Reads and parses Star Citizen default profile and actionmaps XML files.
    /// Merges base bindings with user overrides and provides lookup by function name.
    /// </summary>
    public class DProfileReader
    {
        // ============================================================
        // REGION: Data Classes
        // ============================================================
        public class ActivationMode
        {
            public string Name { get; set; }
            public string OnPress { get; set; }
            public string OnHold { get; set; }
            public string OnRelease { get; set; }
            public string MultiTap { get; set; }
            public string MultiTapBlock { get; set; }
            public string PressTriggerThreshold { get; set; }
            public string ReleaseTriggerThreshold { get; set; }
            public string ReleaseTriggerDelay { get; set; }
            public string Retriggerable { get; set; }
            public string Always { get; set; }
            public string NoModifiers { get; set; }
            public string HoldTriggerDelay { get; set; }
        }

        public class Action
        {
            public string MapName { get; set; }
            public string MapUILabel { get; set; }
            public string MapUICategory { get; set; }
            public string Name { get; set; }
            public string UILabel { get; set; }
            public string UIDescription { get; set; }
            public string Keyboard { get; set; }
            public string Mouse { get; set; }
            public string Joystick { get; set; }
            public string Gamepad { get; set; }
            public bool KeyboardOverRule { get; set; }
            public string JoystickOverRule { get; set; }
            public bool MouseOverRule { get; set; }
            public ActivationMode ActivationMode { get; set; }
        }

        public class ActionMap
        {
            public string Name { get; set; }
            public string UILabel { get; set; }
            public string UICategory { get; set; }
            public Dictionary<string, Action> Actions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        // ============================================================
        // REGION: Internal State
        // ============================================================
        private readonly Dictionary<string, ActionMap> maps = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Action> actions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ActivationMode> activationmodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> joysticks = new(StringComparer.OrdinalIgnoreCase);

        // ============================================================
        // REGION: Binding Normalization
        // ============================================================
        private static string NormalizeKeyboardBinding(string keyboard)
        {
            if (string.IsNullOrWhiteSpace(keyboard)) return null;
            if (keyboard.StartsWith("HMD_", StringComparison.OrdinalIgnoreCase)) return null;
            return keyboard;
        }

        private static string NormalizeMouseBinding(string mouse)
        {
            if (string.IsNullOrWhiteSpace(mouse)) return null;
            return MouseTokenHelper.TryNormalize(mouse, out var normalized) ? normalized : mouse;
        }

        /// <summary>
        /// Merges keyboard and mouse bindings. Prefers keyboard when present.
        /// </summary>
        private static string MergePrimaryBinding(string keyboard, string mouse)
        {
            if (!string.IsNullOrWhiteSpace(keyboard)) return keyboard;
            return mouse; // Fall back to mouse if no keyboard binding
        }

        // ============================================================
        // REGION: Activation Mode Handling
        // ============================================================
        private static ActivationMode CloneActivationMode(ActivationMode source)
        {
            if (source == null) return null;
            return new ActivationMode
            {
                Name = source.Name, OnPress = source.OnPress, OnHold = source.OnHold,
                OnRelease = source.OnRelease, MultiTap = source.MultiTap,
                MultiTapBlock = source.MultiTapBlock, PressTriggerThreshold = source.PressTriggerThreshold,
                ReleaseTriggerThreshold = source.ReleaseTriggerThreshold, ReleaseTriggerDelay = source.ReleaseTriggerDelay,
                Retriggerable = source.Retriggerable, Always = source.Always,
                NoModifiers = source.NoModifiers, HoldTriggerDelay = source.HoldTriggerDelay
            };
        }

        private static void ApplyActivationModeOverrides(XElement action, ActivationMode activationMode)
        {
            if (activationMode == null) return;

            var overrides = new (string attr, Action<string> setter)[]
            {
                ("onPress", v => activationMode.OnPress = v),
                ("onHold", v => activationMode.OnHold = v),
                ("onRelease", v => activationMode.OnRelease = v),
                ("always", v => activationMode.Always = v),
                ("noModifiers", v => activationMode.NoModifiers = v),
                ("holdTriggerDelay", v => activationMode.HoldTriggerDelay = v)
            };

            foreach (var (attr, setter) in overrides)
            {
                var value = (string)action.Attribute(attr);
                if (!string.IsNullOrEmpty(value)) setter(value);
            }
        }

        private ActivationMode ResolveActivationMode(XElement action)
        {
            var activationModeName = (string)action.Attribute("ActivationMode");
            if (string.IsNullOrEmpty(activationModeName) || !activationmodes.TryGetValue(activationModeName, out var mode))
                return null;

            var clonedMode = CloneActivationMode(mode);
            ApplyActivationModeOverrides(action, clonedMode);
            return clonedMode;
        }

        // ============================================================
        // REGION: Action Creation
        // ============================================================
        private Action CreateActionFromElement(XElement action, ActionMap actionMap)
        {
            var name = (string)action.Attribute("name");
            if (string.IsNullOrWhiteSpace(name)) return null;

            var uiLabel = (string)action.Attribute("UILabel") ?? name;
            var uiDescription = (string)action.Attribute("UIDescription") ?? "";

            uiLabel = SCUiText.Instance.Text(uiLabel, uiLabel);
            uiDescription = SCUiText.Instance.Text(uiDescription, uiDescription);

            var keyboard = NormalizeKeyboardBinding((string)action.Attribute("keyboard"));
            var mouse = NormalizeMouseBinding((string)action.Attribute("mouse"));

            return new Action
            {
                MapName = actionMap.Name,
                MapUICategory = actionMap.UICategory,
                MapUILabel = actionMap.UILabel,
                Name = actionMap.Name + "-" + name,
                UILabel = uiLabel,
                UIDescription = uiDescription,
                ActivationMode = ResolveActivationMode(action),
                Keyboard = MergePrimaryBinding(keyboard, mouse),
                Mouse = mouse,
                Joystick = (string)action.Attribute("joystick"),
                Gamepad = (string)action.Attribute("gamepad")
            };
        }

        // ============================================================
        // REGION: Rebind Processing
        // ============================================================
        private void ApplyRebind(XElement action, Action currentAction)
        {
            if (currentAction == null) return;

            var rebind = action.Elements().FirstOrDefault(x => x.Name == "rebind");
            if (rebind == null) return;

            var input = (string)rebind.Attribute("input");
            if (string.IsNullOrWhiteSpace(input)) return;

            ProcessRebindInput(input, currentAction);
        }

        /// <summary>
        /// Processes a rebind input string and updates the action accordingly.
        /// Handles keyboard (kb), joystick (js), and mouse (mo) rebinds.
        /// </summary>
        private void ProcessRebindInput(string input, Action currentAction)
        {
            var underscoreIdx = input.IndexOf('_');
            if (underscoreIdx < 0) return;

            var prefix = input[..2].ToLowerInvariant();
            var value = input[(underscoreIdx + 1)..].Trim();

            switch (prefix)
            {
                case "kb": // Keyboard
                    if (!string.IsNullOrEmpty(value))
                    {
                        var normalized = NormalizeKeyboardBinding(value);
                        currentAction.Keyboard = normalized;
                        currentAction.KeyboardOverRule = !string.IsNullOrEmpty(normalized);
                    }
                    else if (SCPath.TreatBlankRebindAsUnbound)
                    {
                        currentAction.Keyboard = "";
                        currentAction.KeyboardOverRule = true;
                    }
                    break;

                case "js": // Joystick
                    var jsInstance = input[2..underscoreIdx];
                    if (!string.IsNullOrEmpty(value))
                    {
                        currentAction.Joystick = value;
                        currentAction.JoystickOverRule = joysticks.TryGetValue(jsInstance, out var product) ? product : jsInstance;
                    }
                    else if (SCPath.TreatBlankRebindAsUnbound)
                    {
                        currentAction.Joystick = "";
                        currentAction.JoystickOverRule = jsInstance;
                    }
                    break;

                case "mo": // Mouse
                    if (!string.IsNullOrEmpty(value))
                    {
                        var normalized = NormalizeMouseBinding(value);
                        currentAction.Mouse = normalized;
                        // Only set Keyboard field if it's empty or already a mouse token
                        if (string.IsNullOrWhiteSpace(currentAction.Keyboard) || MouseTokenHelper.IsMouseLike(currentAction.Keyboard))
                        {
                            currentAction.Keyboard = normalized;
                        }
                        currentAction.MouseOverRule = true;
                    }
                    else if (SCPath.TreatBlankRebindAsUnbound)
                    {
                        currentAction.Mouse = "";
                        if (string.IsNullOrWhiteSpace(currentAction.Keyboard) || MouseTokenHelper.IsMouseLike(currentAction.Keyboard))
                        {
                            currentAction.Keyboard = "";
                        }
                        currentAction.MouseOverRule = true;
                    }
                    break;
            }
        }

        // ============================================================
        // REGION: ActionMap Reading
        // ============================================================
        private void ReadAction(XElement action, ActionMap actionMap)
        {
            var name = (string)action.Attribute("name");
            var currentAction = CreateActionFromElement(action, actionMap);

            if (!string.IsNullOrWhiteSpace(name) && currentAction != null && !actionMap.Actions.ContainsKey(name))
                actionMap.Actions.Add(name, currentAction);
        }

        private void ReadActionmap(XElement actionmap)
        {
            var mapName = (string)actionmap.Attribute("name");
            if (string.IsNullOrEmpty(mapName)) return;

            if (!maps.TryGetValue(mapName, out var map))
            {
                // New map from defaultProfile
                var uiLabel = SCUiText.Instance.Text((string)actionmap.Attribute("UILabel") ?? mapName, mapName);
                var uiCategory = SCUiText.Instance.Text((string)actionmap.Attribute("UICategory") ?? mapName, mapName);

                var newMap = new ActionMap { Name = mapName, UILabel = uiLabel, UICategory = uiCategory };

                foreach (var action in actionmap.Elements().Where(x => x.Name == "action"))
                {
                    ReadAction(action, newMap);
                }

                maps.Add(mapName, newMap);
            }
            else
            {
                // Existing map - apply actionmaps.xml overrides
                foreach (var action in actionmap.Elements().Where(x => x.Name == "action"))
                {
                    var actionName = (string)action.Attribute("name");

                    // If action doesn't exist, create it
                    if (!map.Actions.TryGetValue(actionName ?? "", out var existingAction))
                    {
                        existingAction = CreateActionFromElement(action, map);
                        if (existingAction != null)
                        {
                            map.Actions[actionName] = existingAction;
                        }
                    }

                    // Apply rebinds
                    ApplyRebind(action, existingAction);
                }
            }
        }

        private void ReadActivationMode(XElement activationModeEl)
        {
            var name = (string)activationModeEl.Attribute("name");
            if (string.IsNullOrEmpty(name) || activationmodes.ContainsKey(name)) return;

            activationmodes.Add(name, new ActivationMode
            {
                Name = name,
                OnPress = (string)activationModeEl.Attribute("onPress"),
                OnHold = (string)activationModeEl.Attribute("onHold"),
                OnRelease = (string)activationModeEl.Attribute("onRelease"),
                MultiTap = (string)activationModeEl.Attribute("multiTap"),
                MultiTapBlock = (string)activationModeEl.Attribute("multiTapBlock"),
                PressTriggerThreshold = (string)activationModeEl.Attribute("pressTriggerThreshold"),
                ReleaseTriggerThreshold = (string)activationModeEl.Attribute("releaseTriggerThreshold"),
                ReleaseTriggerDelay = (string)activationModeEl.Attribute("releaseTriggerDelay"),
                Retriggerable = (string)activationModeEl.Attribute("retriggerable")
            });
        }

        // ============================================================
        // REGION: Public API - XML Parsing
        // ============================================================
        
        /// <summary>
        /// Parses an ActionProfiles XML structure (from actionmaps.xml) and applies the default profile.
        /// </summary>
        public void FromActionProfile(string xml)
        {
            var settings = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = true, IgnoreComments = true };

            using var reader = XmlReader.Create(new StringReader(xml), settings);
            reader.MoveToContent();
            if (XNode.ReadFrom(reader) is XElement el)
            {
                foreach (var actionProfile in el.Elements().Where(x => x.Name == "ActionProfiles"))
                {
                    if ((string)actionProfile.Attribute("profileName") == "default")
                    {
                        FromXML(actionProfile.ToString());
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Parses a profile XML (either defaultProfile.xml or action profile section).
        /// </summary>
        public void FromXML(string xml)
        {
            var settings = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = true, IgnoreComments = true };

            using var reader = XmlReader.Create(new StringReader(xml), settings);
            reader.MoveToContent();
            if (XNode.ReadFrom(reader) is not XElement el) return;

            // Parse activation modes
            var ams = el.Elements().FirstOrDefault(x => x.Name == "ActivationModes");
            if (ams != null)
            {
                foreach (var am in ams.Elements().Where(x => x.Name == "ActivationMode"))
                {
                    ReadActivationMode(am);
                }
            }

            // Parse joystick options
            foreach (var option in el.Elements().Where(x => x.Name == "options"))
            {
                if ((string)option.Attribute("type") == "joystick")
                {
                    var instance = (string)option.Attribute("instance");
                    var product = (string)option.Attribute("Product");
                    if (!string.IsNullOrEmpty(instance) && !joysticks.ContainsKey(instance))
                    {
                        joysticks.Add(instance, product);
                    }
                }
            }

            // Parse action maps
            foreach (var actionmap in el.Elements().Where(x => x.Name == "actionmap"))
            {
                ReadActionmap(actionmap);
            }
        }

        // ============================================================
        // REGION: Public API - Actions Access
        // ============================================================
        
        /// <summary>
        /// Builds the final actions dictionary, filtering to only executable bindings.
        /// Must be called after fromXML and fromActionProfile.
        /// </summary>
        public void Actions()
        {
            // Filter to actions with input bindings, excluding modifier-only bindings
            var modifierKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "lalt", "ralt", "lshift", "rshift", "lctrl", "rctrl" };

            actions = maps
                .SelectMany(x => x.Value.Actions.Values)
                .Where(x => !string.IsNullOrWhiteSpace(x.Keyboard) ||
                            !string.IsNullOrWhiteSpace(x.Mouse) ||
                            !string.IsNullOrWhiteSpace(x.Joystick) ||
                            !string.IsNullOrWhiteSpace(x.Gamepad))
                .Where(x => !modifierKeys.Contains(x.Keyboard ?? ""))
                .ToDictionary(x => x.Name, x => x);

            Logger.Instance.LogMessage(TracingLevel.INFO, $"Loaded {actions.Count} actions with input bindings");
        }

        /// <summary>Gets a binding by its full function name.</summary>
        public Action GetBinding(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return actions.TryGetValue(key, out var action) ? action : null;
        }

        /// <summary>Gets all actions including unbound ones.</summary>
        public Dictionary<string, Action> GetAllActions() =>
            maps.SelectMany(x => x.Value.Actions.Values).ToDictionary(x => x.Name, x => x);

        /// <summary>Gets actions without any input bindings (for display in "Unbound" section).</summary>
        public Dictionary<string, Action> GetUnboundActions() =>
            maps.SelectMany(x => x.Value.Actions.Values)
                .Where(x => string.IsNullOrWhiteSpace(x.Keyboard) &&
                            string.IsNullOrWhiteSpace(x.Mouse) &&
                            string.IsNullOrWhiteSpace(x.Joystick) &&
                            string.IsNullOrWhiteSpace(x.Gamepad))
                .Where(x => !string.IsNullOrWhiteSpace(x.UILabel))
                .ToDictionary(x => x.Name, x => x);

        // ============================================================
        // REGION: CSV Export (Optional)
        // ============================================================
        
        /// <summary>
        /// Exports bindings to CSV files for debugging/analysis.
        /// Only runs when enableCsvExport is true.
        /// </summary>
        public void CreateCsv(bool enableCsvExport)
        {
            if (!enableCsvExport) return;

            try
            {
                ExportKeyboardBindingsCsv();
                ExportMouseBindingsCsv();
                ExportJoystickBindingsCsv();
                ExportUnboundActionsCsv();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"CreateCsv {ex}");
            }
        }

        private void ExportKeyboardBindingsCsv()
        {
            using var outputFile = new StreamWriter(Path.Combine(TheUser.FileStoreDir, "keybindings.csv"));
            outputFile.WriteLine("sep=\t");
            outputFile.WriteLine("map_UICategory\tmap_UILabel\tmap_Name\tUILabel\tUIDescription\tName\tKeyboard\tOverrule\t" +
                "Name\tOnPress\tOnHold\tOnRelease\tMultiTap\tMultiTapBlock\tPressTriggerThreshold\tReleaseTriggerThreshold\tReleaseTriggerDelay\tRetriggerable");

            foreach (var action in actions.Values.OrderBy(x => x.MapUILabel).ThenBy(x => x.UILabel))
            {
                var am = action.ActivationMode;
                outputFile.WriteLine($"{action.MapUICategory}\t{action.MapUILabel}\t{action.MapName}\t" +
                    $"{action.UILabel}\t{action.UIDescription}\t{action.Name}\t{action.Keyboard}\t{(action.KeyboardOverRule ? "YES" : "")}\t" +
                    $"{am?.Name}\t{am?.OnPress}\t{am?.OnHold}\t{am?.OnRelease}\t{am?.MultiTap}\t{am?.MultiTapBlock}\t" +
                    $"{am?.PressTriggerThreshold}\t{am?.ReleaseTriggerThreshold}\t{am?.ReleaseTriggerDelay}\t{am?.Retriggerable}");
            }
        }

        private void ExportMouseBindingsCsv()
        {
            var mouseActions = maps.SelectMany(x => x.Value.Actions.Values)
                .Where(x => !string.IsNullOrWhiteSpace(x.Mouse))
                .ToDictionary(x => x.Name, x => x);

            using var outputFile = new StreamWriter(Path.Combine(TheUser.FileStoreDir, "mousebindings.csv"));
            outputFile.WriteLine("sep=\t");
            outputFile.WriteLine("map_UICategory\tmap_UILabel\tmap_Name\tUILabel\tUIDescription\tName\tMouse\tOverrule");

            foreach (var action in mouseActions.Values.OrderBy(x => x.MapUILabel).ThenBy(x => x.UILabel))
            {
                outputFile.WriteLine($"{action.MapUICategory}\t{action.MapUILabel}\t{action.MapName}\t" +
                    $"{action.UILabel}\t{action.UIDescription}\t{action.Name}\t{action.Mouse}\t{(action.MouseOverRule ? "YES" : "")}");
            }
        }

        private void ExportJoystickBindingsCsv()
        {
            var joystickActions = maps.SelectMany(x => x.Value.Actions.Values)
                .Where(x => !string.IsNullOrWhiteSpace(x.Joystick))
                .ToDictionary(x => x.Name, x => x);

            using var outputFile = new StreamWriter(Path.Combine(TheUser.FileStoreDir, "joystickbindings.csv"));
            outputFile.WriteLine("sep=\t");
            outputFile.WriteLine("map_UICategory\tmap_UILabel\tmap_Name\tUILabel\tUIDescription\tName\tJoystick\tOverrule");

            foreach (var action in joystickActions.Values.OrderBy(x => x.MapUILabel).ThenBy(x => x.UILabel))
            {
                outputFile.WriteLine($"{action.MapUICategory}\t{action.MapUILabel}\t{action.MapName}\t" +
                    $"{action.UILabel}\t{action.UIDescription}\t{action.Name}\t{action.Joystick}\t{action.JoystickOverRule}");
            }
        }

        private void ExportUnboundActionsCsv()
        {
            var unboundActions = GetUnboundActions();

            using var outputFile = new StreamWriter(Path.Combine(TheUser.FileStoreDir, "unboundactions.csv"));
            outputFile.WriteLine("sep=\t");
            outputFile.WriteLine("map_UICategory\tmap_UILabel\tmap_Name\tUILabel\tUIDescription\tName");

            foreach (var action in unboundActions.Values.OrderBy(x => x.MapUILabel).ThenBy(x => x.UILabel))
            {
                outputFile.WriteLine($"{action.MapUICategory}\t{action.MapUILabel}\t{action.MapName}\t" +
                    $"{action.UILabel}\t{action.UIDescription}\t{action.Name}");
            }
        }
    }
}
