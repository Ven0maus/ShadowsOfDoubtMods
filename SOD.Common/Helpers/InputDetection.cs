using Rewired;
using SOD.Common.Custom;
using SOD.Common.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using UnityEngine;

namespace SOD.Common.Helpers
{
    public sealed class AxisInputDetectionEventArgs : EventArgs
    {
        public string ActionName { get; }
        public float AxisValue { get; }
        public InteractablePreset.InteractionKey Key { get; }

        internal AxisInputDetectionEventArgs(string actionName, InteractablePreset.InteractionKey key, float axisValue)
        {
            ActionName = actionName;
            Key = key;
            AxisValue = axisValue;
        }
    }

    public sealed class InputDetection
    {
        /// <summary>
        /// Raised when an axis's state changes in value. Is <b>not</b> raised for suppressed inputs, which are inputs that the vanilla game is currently being forced to ignore.
        /// </summary>
        public event EventHandler<AxisInputDetectionEventArgs> OnAxisStateChanged;

        /// <summary>
        /// Raised when a button's state changes from up/released to down/pressed and vice versa. Is <b>not</b> raised for suppressed inputs, which are inputs that the vanilla game is currently being forced to ignore.
        /// </summary>
        public event EventHandler<InputDetectionEventArgs> OnButtonStateChanged;

        /// <summary>
        /// Raised when an axis's state changes in value. Is <b>only</b> raised for suppressed inputs, which are inputs that the vanilla game is currently being forced to ignore.
        /// </summary>
        public event EventHandler<SuppressedAxisInputDetectionEventArgs> OnSuppressedAxisStateChanged;

        /// <summary>
        /// Raised when a button's state changes from up/released to down/pressed and vice versa. Is <b>only</b> raised for suppressed inputs, which are inputs that the vanilla game is currently being forced to ignore.
        /// </summary>
        public event EventHandler<SuppressedInputDetectionEventArgs> OnSuppressedButtonStateChanged;

        private const int KEYBOARD_AND_MOUSE_CONTROLLER_ID = 0;


        /// <summary>
        /// Contains all keyboard and mouse Rewired ActionElementMap mappings.
        /// </summary>
        public List<ActionElementMap> KeyboardAndMouseMappings
        {
            get
            {
                if (keyboardAndMouseMappings != null)
                {
                    return keyboardAndMouseMappings;
                }
                keyboardAndMouseMappings = new List<ActionElementMap>();
                var controllers = new List<Rewired.Controller>();
                Rewired.Controller keyboardController = RewiredPlayer.controllers.GetController(ControllerType.Keyboard, KEYBOARD_AND_MOUSE_CONTROLLER_ID);
                controllers.Add(keyboardController);
                Rewired.Controller mouseController = RewiredPlayer.controllers.GetController(ControllerType.Mouse, KEYBOARD_AND_MOUSE_CONTROLLER_ID);
                controllers.Add(mouseController);
                foreach (var controller in controllers)
                {
                    foreach (string category in CONTROLLER_MAP_CATEGORIES)
                    {
                        ControllerMap map = RewiredPlayer.controllers.maps.GetMap(controller.type, controller.id, category, "Default");
                        keyboardAndMouseMappings.AddRange(map.AllMaps.ToList());
                    }
                }
                return keyboardAndMouseMappings;
            }
        }

        public Rewired.Player RewiredPlayer => InputController.Instance.player;
        internal Dictionary<string, InputSuppressionEntry> InputSuppressionDictionary { get; set; } = new();

        private readonly List<string> CONTROLLER_MAP_CATEGORIES = ["Interaction", "Movement", "Menu", "CityEdit"];
        private List<ActionElementMap> keyboardAndMouseMappings;

        internal InputDetection() { }


        /// <summary>
        /// Gets the KeyCode associated with a Rewired ActionElementMap binding. Required for certain Button-type bindings such as mouse buttons.
        /// </summary>
        /// <param name="binding"></param>
        /// <returns></returns>
        public KeyCode GetApproximateKeyCode(ActionElementMap binding)
        {
            if (binding.keyCode != KeyCode.None)
            {
                return binding.keyCode;
            }
            switch (binding.elementIdentifierName)
            {
                case "Left Mouse Button":
                    return KeyCode.Mouse0;
                case "Right Mouse Button":
                    return KeyCode.Mouse1;
                case "Mouse Button 3":
                    return KeyCode.Mouse2;
                case "Mouse Button 4":
                    return KeyCode.Mouse3;
                case "Mouse Button 5":
                    return KeyCode.Mouse4;
                // Below are guesses
                case "Mouse Button 6":
                    return KeyCode.Mouse5;
                case "Mouse Button 7":
                    return KeyCode.Mouse6;
            }
            return KeyCode.None;
        }

        /// <summary>
        /// Get the Rewired controller binding currently bound to an InteractionKey's Rewired InputAction.
        /// </summary>
        /// <param name="interactionKey"></param>
        /// <returns></returns>
        public ActionElementMap GetBinding(InteractablePreset.InteractionKey interactionKey)
        {
            List<ActionElementMap> controllerMappings = KeyboardAndMouseMappings;
            try
            {
                return controllerMappings.First(x => x.actionId == GetRewiredAction(interactionKey)?.id);
            }
            catch (InvalidOperationException)
            {
                Plugin.Log.LogDebug($"Could not find action controller binding for interactionKey: {interactionKey}");
                return null;
            }
        }

        /// <summary>
        /// Get the Rewired InputAction associated with an InteractionKey.
        /// </summary>
        /// <param name="interactionKey"></param>
        /// <returns></returns>
        public InputAction GetRewiredAction(InteractablePreset.InteractionKey interactionKey)
        {
            return ReInput.MappingHelper.Instance.GetAction(GetRewiredActionName(interactionKey));
        }

        /// <summary>
        /// Get the action name associated with an InteractionKey in a format that is useable with Rewired GetButton methods.
        /// </summary>
        /// <param name="interactionKey"></param>
        /// <returns></returns>
        public string GetRewiredActionName(InteractablePreset.InteractionKey interactionKey)
        {
            var gameMappedKey = Enum.GetName(typeof(InteractablePreset.InteractionKey), interactionKey);
            var capitalizedKey = $"{gameMappedKey.Substring(0, 1).ToUpper()}{gameMappedKey.Substring(1)}";
            return capitalizedKey;
        }

        /// <summary>
        /// Determines if an input is being suppressed by a specified plugin.
        /// </summary>
        /// <param name="pluginGuid">The guid of the plugin.</param>
        /// <param name="keyCode">The KeyCode of the input.</param>
        /// <param name="maxTimeLeft">The maximum time that the input will be suppressed for, across all suppression entries.</param>
        /// <returns></returns>
        public bool IsInputSuppressed(string pluginGuid, KeyCode keyCode, out TimeSpan? maxTimeLeft)
        {
            if (keyCode == KeyCode.None)
                throw new ArgumentException("KeyCode cannot be none.", nameof(keyCode));

            maxTimeLeft = null;
            var isSuppressed = false;
            var entries = FindInputSuppressionEntries(entry => entry.CallerGuid == pluginGuid && entry.KeyCode == keyCode);
            foreach (var (key, value) in entries)
            {
                isSuppressed = true;
                maxTimeLeft ??= TimeSpan.Zero;
                if (value.TimeRemainingSec > 0f && value.TimeRemainingSec > maxTimeLeft.Value.TotalSeconds)
                    maxTimeLeft = TimeSpan.FromSeconds(value.TimeRemainingSec);
            }
            return isSuppressed;
        }

        /// <summary>
        /// Determines if an input is being suppressed by a specified plugin.
        /// </summary>
        /// <param name="pluginGuid">The guid of the plugin.</param>
        /// <param name="interactionKey">The interaction (virtual action name) associated with the input.</param>
        /// <param name="maxTimeLeft">The maximum time that the input will be suppressed for, across all suppression entries.</param>
        /// <returns></returns>
        public bool IsInputSuppressed(string pluginGuid, InteractablePreset.InteractionKey interactionKey, out TimeSpan? maxTimeLeft)
        {
            if (interactionKey == InteractablePreset.InteractionKey.none)
                throw new ArgumentException("InteractionKey cannot be none.", nameof(interactionKey));

            maxTimeLeft = null;
            var isSuppressed = false;
            var predicate = new Func<InputSuppressionEntry, bool>(entry => entry.CallerGuid == pluginGuid && (entry.InteractionKey == interactionKey || IsOverlappingEntryForInteractionKey(entry, interactionKey)));
            var entries = FindInputSuppressionEntries(predicate);
            foreach (var (key, value) in entries)
            {
                isSuppressed = true;
                maxTimeLeft ??= TimeSpan.Zero;
                if (value.TimeRemainingSec > 0f && value.TimeRemainingSec > maxTimeLeft.Value.TotalSeconds)
                    maxTimeLeft = TimeSpan.FromSeconds(value.TimeRemainingSec);
            }
            return isSuppressed;
        }

        /// <summary>
        /// Determines if an input is being suppressed by any plugin.
        /// </summary>
        /// <param name="keyCode">The KeyCode of the input.</param>
        /// <param name="suppressedBy">The guids of all plugins involved in suppressing the KeyCode.</param>
        /// <param name="maxTimeLeft">The maximum time that the input will be suppressed for, across all suppression entries.</param>
        /// <returns></returns>
        public bool IsInputSuppressedByAnyPlugin(KeyCode keyCode, out List<string> suppressedBy, out TimeSpan? maxTimeLeft)
        {
            if (keyCode == KeyCode.None)
                throw new ArgumentException("KeyCode cannot be none.", nameof(keyCode));

            suppressedBy = new();
            maxTimeLeft = null;

            var isSuppressed = false;
            foreach (var (key, value) in InputSuppressionDictionary)
            {
                if (value.KeyCode != keyCode)
                {
                    continue;
                }
                isSuppressed = true;
                suppressedBy.Add(value.CallerGuid);
                maxTimeLeft ??= TimeSpan.Zero;
                if (value.TimeRemainingSec > 0f && value.TimeRemainingSec > maxTimeLeft.Value.TotalSeconds)
                    maxTimeLeft = TimeSpan.FromSeconds(value.TimeRemainingSec);
            }
            return isSuppressed;
        }

        /// <summary>
        /// Determines if an input is being suppressed by any plugin.
        /// </summary>
        /// <param name="interactionKey">The interaction (virtual action name) associated with the input.</param>
        /// <param name="suppressedBy">The guids of all plugins involved in suppressing the KeyCode.</param>
        /// <param name="maxTimeLeft">The maximum time that the input will be suppressed for, across all suppression entries.</param>
        /// <returns></returns>
        public bool IsInputSuppressedByAnyPlugin(InteractablePreset.InteractionKey interactionKey, out List<string> suppressedBy, out TimeSpan? maxTimeLeft)
        {
            if (interactionKey == InteractablePreset.InteractionKey.none)
                throw new ArgumentException("InteractionKey cannot be none.", nameof(interactionKey));

            suppressedBy = new();
            maxTimeLeft = null;

            var isSuppressed = false;
            foreach (var (key, value) in InputSuppressionDictionary)
            {
                bool isOverlappingEntry = IsOverlappingEntryForInteractionKey(value, interactionKey);
                if (value.InteractionKey != interactionKey && !isOverlappingEntry)
                {
                    continue;
                }
                isSuppressed = true;
                suppressedBy.Add(value.CallerGuid);
                maxTimeLeft ??= TimeSpan.Zero;
                if (value.TimeRemainingSec > 0f && value.TimeRemainingSec > maxTimeLeft.Value.TotalSeconds)
                    maxTimeLeft = TimeSpan.FromSeconds(value.TimeRemainingSec);
            }
            return isSuppressed;
        }

        /// <summary>
        /// Stops and removes matching input suppression entries, if present.
        /// </summary>
        /// <param name="keyCode">The KeyCode to search for.</param>
        public void RemoveInputSuppression(KeyCode keyCode)
        {
            if (keyCode == KeyCode.None)
                throw new ArgumentException("KeyCode cannot be none.", nameof(keyCode));

            RemoveInputSuppression(entry => entry.KeyCode == keyCode);
        }

        /// <summary>
        /// Stops and removes matching input suppression entries, if present.
        /// </summary>
        /// <param name="interactionKey">The interaction to search for.</param>
        public void RemoveInputSuppression(InteractablePreset.InteractionKey interactionKey)
        {
            if (interactionKey == InteractablePreset.InteractionKey.none)
                throw new ArgumentException("InteractionKey cannot be none.", nameof(interactionKey));

            RemoveInputSuppression(entry => entry.InteractionKey == interactionKey || IsOverlappingEntryForInteractionKey(entry, interactionKey));
        }

        /// <summary>
        /// Stops and removes matching input suppression entries, if present. Limits removal to entries added by the plugin with the matching guid.
        /// </summary>
        /// <param name="pluginGuid">The guid of the plugin.</param>
        /// <param name="keyCode">The KeyCode to search for.</param>
        public void RemoveInputSuppression(string pluginGuid, KeyCode keyCode)
        {
            if (keyCode == KeyCode.None)
                throw new ArgumentException("KeyCode cannot be none.", nameof(keyCode));

            RemoveInputSuppression(entry => entry.KeyCode == keyCode && entry.CallerGuid == pluginGuid);
        }

        /// <summary>
        /// Stops and removes matching input suppression entries, if present. Limits removal to entries added by the plugin with the matching guid.
        /// </summary>
        /// <param name="pluginGuid">The guid of the plugin.</param>
        /// <param name="interactionKey">The interaction to search for.</param>
        public void RemoveInputSuppression(string pluginGuid, InteractablePreset.InteractionKey interactionKey)
        {
            if (interactionKey == InteractablePreset.InteractionKey.none)
                throw new ArgumentException("InteractionKey cannot be none.", nameof(interactionKey));

            RemoveInputSuppression(entry => (entry.InteractionKey == interactionKey || IsOverlappingEntryForInteractionKey(entry, interactionKey)) && entry.CallerGuid == pluginGuid);
        }

        /// <summary>
        /// Suppress vanilla game responses to an input, with an optional duration. Adds an input suppression entry.
        /// <br>The input suppression entry will suppress vanilla game responses to the input while it or any entry tied to that input is present.</br>
        /// <br>At the end of the duration (if provided), the input suppression entry will be automatically removed.</br>
        /// </summary>
        /// <param name="callerGuid">The guid associated with the plugin calling this method.</param>
        /// <param name="keyCode">The KeyCode to be suppressed.</param>
        /// <param name="duration">The duration that the input suppression lasts (does not progress while the game is paused).</param>
        /// <param name="overwrite">Whether to overwrite any existing entry with this plugin guid and keycode combination, applying the new duration.</param>
        public void SetInputSuppressed(string callerGuid, KeyCode keyCode, TimeSpan? duration = null, bool overwrite = false)
        {
            if (keyCode == KeyCode.None)
                throw new ArgumentException("KeyCode cannot be none.", nameof(keyCode));

            var key = ConvertToDictionaryKey(callerGuid, InteractablePreset.InteractionKey.none, keyCode);

            if (InputSuppressionDictionary.TryGetValue(key, out var entry))
            {
                if (overwrite)
                {
                    entry.Stop();
                    InputSuppressionDictionary.Remove(key);
                }
                else
                {
                    return;
                }
            }

            // Add modifier to dictionary to track
            entry = new InputSuppressionEntry(callerGuid, keyCode, duration);
            InputSuppressionDictionary.Add(key, entry);

            // Start timer
            entry.Start();
        }

        /// <summary>
        /// Suppress vanilla game responses to an input, with an optional duration. Adds an input suppression entry.
        /// <br>The input suppression entry will suppress vanilla game responses to the input while it or any entry tied to that input is present.</br>
        /// <br>At the end of the duration (if provided), the input suppression entry will be automatically removed.</br>
        /// </summary>
        /// <param name="callerGuid">The guid associated with the plugin calling this method.</param>
        /// <param name="interactionKey">The interaction (virtual action name) to be suppressed.</param>
        /// <param name="duration">The duration that the input suppression lasts (does not progress while the game is paused).</param>
        /// <param name="overwrite">Whether to overwrite any existing entry with this plugin guid and keycode combination, applying the new duration.</param>
        public void SetInputSuppressed(string callerGuid, InteractablePreset.InteractionKey interactionKey, TimeSpan? duration = null, bool overwrite = false)
        {
            if (interactionKey == InteractablePreset.InteractionKey.none)
                throw new ArgumentException("InteractionKey cannot be none.", nameof(interactionKey));

            var key = ConvertToDictionaryKey(callerGuid, interactionKey, KeyCode.None);

            if (InputSuppressionDictionary.TryGetValue(key, out var entry))
            {
                if (overwrite)
                {
                    entry.Stop();
                    InputSuppressionDictionary.Remove(key);
                }
                else
                {
                    return;
                }
            }

            // Add modifier to dictionary to track
            entry = new InputSuppressionEntry(callerGuid, interactionKey, duration);
            InputSuppressionDictionary.Add(key, entry);

            // Start timer
            entry.Start();
        }

        internal string ConvertToDictionaryKey(string callerGuid, InteractablePreset.InteractionKey interactionKey, KeyCode keyCode)
        {
            var keyCodeToUse = interactionKey == InteractablePreset.InteractionKey.none ? keyCode : KeyCode.None;
            return $"{callerGuid}_{interactionKey}_{keyCodeToUse}";
        }

        internal bool IsOverlappingEntryForInteractionKey(InputSuppressionEntry entry, InteractablePreset.InteractionKey interactionKey)
        {
            var binding = GetBinding(interactionKey);
            if (binding == null)
            {
                return false;
            }
            var boundKeyCode = GetApproximateKeyCode(binding);
            var elementIdentifierName = binding.elementIdentifierName;
            var elementIdentifierId = binding.elementIdentifierId;
            bool isOverlappingEntry = (boundKeyCode != KeyCode.None && entry.KeyCode == boundKeyCode) || (entry.ElementIdentifierName != string.Empty && entry.ElementIdentifierName == elementIdentifierName && entry.ElementIdentifierId == elementIdentifierId);
            if (!isOverlappingEntry)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Called when a savegame is loaded.
        /// </summary>
        /// <param name="path"></param>
        internal void Load(string path)
        {
            var hash = Lib.SaveGame.GetUniqueString(path);
            var storePath = Lib.SaveGame.GetSavestoreDirectoryPath(Assembly.GetExecutingAssembly(), $"inputsuppression_{hash}.json");

            if (File.Exists(storePath))
            {
                var json = File.ReadAllText(storePath);
                var jsonData = JsonSerializer.Deserialize<List<InputSuppressionEntry.JsonData>>(json);

                if (jsonData.Count > 0)
                {
                    InputSuppressionDictionary = new();
                    foreach (var data in jsonData)
                    {
                        InputSuppressionEntry entry;
                        if (data.IsSetOnInteractionKey)
                        {
                            entry = new InputSuppressionEntry(data.CallerGuid, data.InteractionKey, data.Time == 0f ? null : TimeSpan.FromSeconds(data.Time));
                        }
                        else
                        {
                            entry = new InputSuppressionEntry(data.CallerGuid, data.KeyCode, data.Time == 0f ? null : TimeSpan.FromSeconds(data.Time));
                        }
                        InputSuppressionDictionary[entry.DictionaryKey] = entry;
                        entry.Start();
                    }
                }
            }
        }

        internal void ReportAxisStateChange(string actionName, InteractablePreset.InteractionKey interactionKey, float axisValue, List<string> suppressedBy)
        {
            if (suppressedBy.Count == 0)
                OnAxisStateChanged?.Invoke(this, new AxisInputDetectionEventArgs(actionName, interactionKey, axisValue));
            else
                OnSuppressedAxisStateChanged?.Invoke(this, new SuppressedAxisInputDetectionEventArgs(actionName, interactionKey, axisValue, suppressedBy));
        }

        internal void ReportButtonStateChange(string actionName, InteractablePreset.InteractionKey interactionKey, bool isDown, List<string> suppressedBy)
        {
            if (suppressedBy.Count == 0)
                OnButtonStateChanged?.Invoke(this, new InputDetectionEventArgs(actionName, interactionKey, isDown));
            else
                OnSuppressedButtonStateChanged?.Invoke(this, new SuppressedInputDetectionEventArgs(actionName, interactionKey, isDown, suppressedBy));
        }

        /// <summary>
        /// Method is used to reset the player status data tracked
        /// </summary>
        internal void ResetSuppressionTracking()
        {
            // Clear out the modifiers and the dictionary for a new game
            foreach (var entry in InputSuppressionDictionary.Values)
                entry.Stop();
            InputSuppressionDictionary.Clear();
        }

        /// <summary>
        /// Called when a savegame is saved.
        /// </summary>
        /// <param name="path"></param>
        internal void Save(string path)
        {
            var hash = Lib.SaveGame.GetUniqueString(path);
            var storePath = Lib.SaveGame.GetSavestoreDirectoryPath(Assembly.GetExecutingAssembly(), $"inputsuppression_{hash}.json");

            // Clean-up
            if (InputSuppressionDictionary.Count == 0)
            {
                if (File.Exists(storePath))
                    File.Delete(storePath);
                return;
            }

            var data = InputSuppressionDictionary
                .Select(a => new InputSuppressionEntry.JsonData
                {
                    CallerGuid = a.Value.CallerGuid,
                    KeyCode = a.Value.IsSetOnInteractionKey ? KeyCode.None : a.Value.KeyCode,
                    InteractionKey = a.Value.IsSetOnInteractionKey ? a.Value.InteractionKey : InteractablePreset.InteractionKey.none,
                    Time = a.Value.TimeRemainingSec,
                    IsSetOnInteractionKey = a.Value.IsSetOnInteractionKey
                })
                .ToList();
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(storePath, json);
        }

        /// <summary>
        /// Finds active input suppression entries matching the provided predicate.
        /// </summary>
        /// <param name="matcherPredicate"></param>
        private List<KeyValuePair<string, InputSuppressionEntry>> FindInputSuppressionEntries(Func<InputSuppressionEntry, bool> matcherPredicate)
        {
            List<KeyValuePair<string, InputSuppressionEntry>> matches = new();
            foreach (var (key, entry) in InputSuppressionDictionary)
            {
                if (!matcherPredicate(entry))
                {
                    continue;
                }
                matches.Add(new(key, entry));
            }
            return matches;
        }

        /// <summary>
        /// Stops and removes any input suppression entries matching the provided predicate.
        /// </summary>
        /// <param name="matcherPredicate"></param>
        private void RemoveInputSuppression(Func<InputSuppressionEntry, bool> matcherPredicate)
        {
            var entriesToRemove = FindInputSuppressionEntries(matcherPredicate);
            foreach (var (key, value) in entriesToRemove)
            {
                value.Stop();
                InputSuppressionDictionary.Remove(key);
            }
        }

    }

    public sealed class InputDetectionEventArgs : EventArgs
    {
        public string ActionName { get; }
        public bool IsDown { get; }
        public InteractablePreset.InteractionKey Key { get; }

        internal InputDetectionEventArgs(string actionName, InteractablePreset.InteractionKey key, bool isDown)
        {
            ActionName = actionName;
            Key = key;
            IsDown = isDown;
        }
    }

    public sealed class SuppressedAxisInputDetectionEventArgs : EventArgs
    {
        public string ActionName { get; }
        public float AxisValue { get; }
        public InteractablePreset.InteractionKey Key { get; }
        public List<string> SuppressedBy { get; }

        internal SuppressedAxisInputDetectionEventArgs(string actionName, InteractablePreset.InteractionKey key, float axisValue, List<string> suppressedBy)
        {
            ActionName = actionName;
            Key = key;
            AxisValue = axisValue;
            SuppressedBy = suppressedBy;
        }
    }

    public sealed class SuppressedInputDetectionEventArgs : EventArgs
    {
        public string ActionName { get; }
        public bool IsDown { get; }
        public InteractablePreset.InteractionKey Key { get; }
        public List<string> SuppressedBy { get; }

        internal SuppressedInputDetectionEventArgs(string actionName, InteractablePreset.InteractionKey key, bool isDown, List<string> suppressedBy)
        {
            ActionName = actionName;
            Key = key;
            IsDown = isDown;
            SuppressedBy = suppressedBy;
        }
    }
}