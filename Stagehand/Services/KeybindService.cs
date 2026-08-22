using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Stagehand.Services;

/// <summary>
/// Which of the Control, Alt, and/or Shift keys must be held to trigger a keybind.
/// </summary>
[Flags]
public enum KeybindModifierKeys : byte
{
    /// <summary>
    /// Neither the Control, Alt, nor Shift keys may be held to trigger the keybind.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Ctrl key must be held to trigger the keybind.
    /// </summary>
    Control = 1 << 0,

    /// <summary>
    /// The Alt key must be held to trigger the keybind.
    /// </summary>
    Alt = 1 << 1,

    /// <summary>
    /// The Shift key must be held to trigger the keybind.
    /// </summary>
    Shift = 1 << 2,
}

/// <summary>
/// A combination of root key and optional modifier keys that can be pressed to trigger an <see cref="IKeybindAction"/>.
/// </summary>
/// <param name="RootKey">The key that triggers the keybind when pressed while this keybind's <see cref="ModifierKeys"/> are held.</param>
/// <param name="ModifierKeys">The Control, Alt, and/or Shift modifier keys that must be held when the <see cref="RootKey"/> is pressed for the keybind to be triggered.</param>
public record struct Keybind(VirtualKey RootKey, KeybindModifierKeys ModifierKeys)
{
    /// <summary>
    /// A keybind that represents no bound key combination, and will never be triggered.
    /// </summary>
    public static Keybind Unassigned { get; } = new() { RootKey = VirtualKey.NO_KEY, ModifierKeys = KeybindModifierKeys.None };

    /// <summary>
    /// Whether this keybind is unassigned and will never be triggered.
    /// </summary>
    public bool IsUnassigned => RootKey == VirtualKey.NO_KEY;

    /// <summary>
    /// Returns a user-friendly string representation of this keybind.
    /// </summary>
    public override string ToString()
    {
        if (IsUnassigned)
        {
            return "(None)";
        }
        else
        {
            return $"{(ModifierKeys.HasFlag(KeybindModifierKeys.Control) ? "Ctrl + " : "")}{(ModifierKeys.HasFlag(KeybindModifierKeys.Alt) ? "Alt + " : "")}{(ModifierKeys.HasFlag(KeybindModifierKeys.Shift) ? "Shift + " : "")}{RootKey.GetFancyName()}";
        }
    }
}

/// <summary>
/// The plugin-defined configuration for a keybindable action.
/// </summary>
/// <param name="Id">A unique string ID used to store the assigned keybind in the user's configuration.</param>
/// <param name="DisplayName">The user-facing name of this keybind.</param>
/// <param name="GroupDisplayName">The user-facing name of the group to show this keybind under.</param>
/// <param name="Description">A more thorough description of this keybind.</param>
/// <param name="DefaultKeybind">The default keybind to assign.</param>
public sealed record class KeybindInfo(string Id, string DisplayName, string GroupDisplayName, string Description, Keybind DefaultKeybind);

// Usage: Register action at plugin startup, add Pressed handler at scope start, remove Pressed handler at scope stop, unregister action at plugin shutdown
public interface IKeybindService
{
    /// <summary>
    /// The modifier keys that are currently being held down by the player.
    /// </summary>
    KeybindModifierKeys CurrentlyHeldModifierKeys { get; }

    /// <summary>
    /// The keybind groups that contain the keybindable actions that have been registered, sorted alphabetically.
    /// </summary>
    /// <remarks>
    /// Please hold <see cref="KeybindGroupLock"/> while accessing.
    /// </remarks>
    IReadOnlyList<IKeybindGroup> KeybindGroups { get; }

    /// <summary>
    /// The lock used to synchronize access to <see cref="KeybindGroups"/>.
    /// </summary>
    Lock KeybindGroupLock { get; }

    /// <summary>
    /// Registers a new keybindable action with the given parameters.
    /// </summary>
    /// <param name="keybindInfo">The information about the keybind to register.</param>
    /// <returns>The keybindable action whose <see cref="IKeybindAction.Pressed"/> event will be raised when invoked by the user pressing its keybind.</returns>
    IKeybindAction RegisterAction(KeybindInfo keybindInfo);

    /// <summary>
    /// Attempts to set the keybind of the given keybindable action.
    /// </summary>
    /// <remarks>
    /// This can fail if <paramref name="action"/> was registered with a different instance of <see cref="IKeybindService"/>, or if the keybind specified by
    /// <paramref name="newKeybind"/> is already in use.
    /// </remarks>
    /// <param name="action">The keybindable action to set the keybind of.</param>
    /// <param name="newKeybind">The new key combination to use.</param>
    /// <returns>True if the action's keybind was successfully set, or false otherwise.</returns>
    bool TrySetActionKeybind(IKeybindAction action, Keybind newKeybind);

    /// <summary>
    /// Unregisters the given keybindable action from this keybind service.
    /// </summary>
    /// <param name="action">The keybindable action to unregister.</param>
    void UnregisterAction(IKeybindAction action);

    /// <summary>
    /// Begins listening for the next keybind that is pressed, which will raise the <see cref="KeybindPressed"/> event
    /// and not trigger any bound <see cref="IKeybindAction"/>.
    /// </summary>
    void StartListeningForKeybind();

    /// <summary>
    /// Raised the first time a keybind is pressed after <see cref="StartListeningForKeybind"/> is called, regardless of
    /// whether the keybind is bound to a <see cref="IKeybindAction"/>.
    /// </summary>
    event Action<Keybind> KeybindPressed;

    /// <summary>
    /// Cancels any pending calls to <see cref="StartListeningForKeybind"/>, so that <see cref="KeybindPressed"/> is not
    /// raised for the next keybind that is pressed, and any bound <see cref="IKeybindAction"/> is triggered like normal.
    /// </summary>
    void CancelListeningForKeybind();
}

internal partial class KeybindService : IKeybindService, IDisposable
{
    private const int KeyMapCount = 8; // Number of possible combinations of the 3 KeybindModifierKeys

    private readonly ILogger _logger;
    private readonly IFramework _framework;
    private readonly IKeyState _keyState;
    private readonly StagehandConfiguration _stagehandConfiguration;

    private readonly KeyMap[] _keyMaps = new KeyMap[KeyMapCount];
    private readonly List<KeybindGroup> _sortedKeybindGroups = new();
    private bool _isListeningForKeybind = false;

    public event Action<Keybind>? KeybindPressed;

    public KeybindModifierKeys CurrentlyHeldModifierKeys
    {
        get
        {
            KeybindModifierKeys result = KeybindModifierKeys.None;

            if (_keyState[VirtualKey.CONTROL])
            {
                result |= KeybindModifierKeys.Control;
            }

            if (_keyState[VirtualKey.MENU])
            {
                result |= KeybindModifierKeys.Alt;
            }

            if (_keyState[VirtualKey.SHIFT])
            {
                result |= KeybindModifierKeys.Shift;
            }

            return result;
        }
    }

    public IReadOnlyList<IKeybindGroup> KeybindGroups => _sortedKeybindGroups;
    public Lock KeybindGroupLock { get; } = new();

    public KeybindService(ILogger<KeybindService> logger, IFramework framework, IKeyState keyState, StagehandConfiguration stagehandConfiguration)
    {
        _logger = logger;
        _framework = framework;
        _keyState = keyState;
        _stagehandConfiguration = stagehandConfiguration;

        for (int i = 0; i < _keyMaps.Length; i++)
        {
            _keyMaps[i] = new();
        }

        _framework.Update += OnFrameworkUpdate;
    }

    public IKeybindAction RegisterAction(KeybindInfo keybindInfo)
    {
        var newAction = new KeybindAction(keybindInfo, _stagehandConfiguration.AssignedKeybinds.GetValueOrDefault(keybindInfo.Id, keybindInfo.DefaultKeybind));

        // Add to keymap if a keybind is assigned
        if (!newAction.CurrentKeybind.IsUnassigned)
        {
            var keyMapIndex = (byte)newAction.CurrentKeybind.ModifierKeys;
            Debug.Assert(keyMapIndex < _keyMaps.Length);
            if (!_keyMaps[keyMapIndex].TryAddAction(newAction, out var conflictingAction))
            {
                _logger.LogWarning("Keybind action {displayName} conflicts with existing bound action {existing}! Removing keybind {keybind} from {action}.", keybindInfo.DisplayName, conflictingAction.Info.DisplayName, newAction.CurrentKeybind.ToString(), keybindInfo.DisplayName);
                newAction.CurrentKeybind = Keybind.Unassigned;
            }
        }

        // Add to group
        lock (KeybindGroupLock)
        {
            var groupIndex = _sortedKeybindGroups.BinarySearch(new(keybindInfo.GroupDisplayName));
            if (groupIndex >= 0)
            {
                var group = _sortedKeybindGroups[groupIndex];
                group.AddAction(newAction);
            }
            else
            {
                var group = new KeybindGroup(keybindInfo.GroupDisplayName);
                _sortedKeybindGroups.Insert(~groupIndex, group);
                group.AddAction(newAction);
            }
        }

        return newAction;
    }

    public bool TrySetActionKeybind(IKeybindAction action, Keybind newKeybind)
    {
        if (action is KeybindAction keybindAction)
        {
            if (action.CurrentKeybind == newKeybind)
            {
                return true;
            }
            bool success;
            if (!action.CurrentKeybind.IsUnassigned)
            {
                var oldKeyMapIndex = (byte)action.CurrentKeybind.ModifierKeys;
                Debug.Assert(oldKeyMapIndex < _keyMaps.Length);
                var oldKeyMap = _keyMaps[oldKeyMapIndex];
                if (oldKeyMap.TryRemoveAction(keybindAction))
                {
                    keybindAction.CurrentKeybind = Keybind.Unassigned;
                }
            }

            if (!newKeybind.IsUnassigned)
            {
                var newKeyMapIndex = (byte)newKeybind.ModifierKeys;
                Debug.Assert(newKeyMapIndex < _keyMaps.Length);
                var newKeyMap = _keyMaps[newKeyMapIndex];
                keybindAction.CurrentKeybind = newKeybind;
                if (!newKeyMap.TryAddAction(keybindAction, out var conflictingAction))
                {
                    keybindAction.CurrentKeybind = Keybind.Unassigned;
                    _logger.LogWarning("Keybind action {displayName} conflicts with existing bound action {existing}! Removing keybind {keybind} from {action}.", keybindAction.Info.DisplayName, conflictingAction.Info.DisplayName, newKeybind.ToString(), keybindAction.Info.DisplayName);
                    success = false;
                }
                else
                {
                    success = true;
                }
            }
            else
            {
                success = true;
            }
            _stagehandConfiguration.AssignedKeybinds[action.Info.Id] = keybindAction.CurrentKeybind;
            _stagehandConfiguration.Save();
            return success;
        }
        else
        {
            return false;
        }
    }

    public void UnregisterAction(IKeybindAction action)
    {
        if (action is KeybindAction keybindAction)
        {
            // Remove from group
            lock (KeybindGroupLock)
            {
                var groupIndex = _sortedKeybindGroups.BinarySearch(new KeybindGroup(action.Info.DisplayName));
                if (groupIndex >= 0)
                {
                    _sortedKeybindGroups[groupIndex].RemoveAction(keybindAction);
                }
            }

            // Remove from keymap if a keybind is assigned
            if (!keybindAction.CurrentKeybind.IsUnassigned)
            {
                var keyMapIndex = (byte)keybindAction.CurrentKeybind.ModifierKeys;
                Debug.Assert(keyMapIndex < _keyMaps.Length);
                bool removed = _keyMaps[keyMapIndex].TryRemoveAction(keybindAction);
                Debug.Assert(removed);
            }
        }
    }

    public void StartListeningForKeybind()
    {
        _isListeningForKeybind = true;
    }

    public void CancelListeningForKeybind()
    {
        _isListeningForKeybind = false;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        const int pressedValue = 3;

        // If the game's native text input is focused, ignore keyboard input
        unsafe
        {
            var uiModule = UIModule.Instance();
            if (uiModule != null)
            {
                var atkModule = uiModule->GetRaptureAtkModule();
                if (atkModule != null && atkModule->IsTextInputActive())
                {
                    return;
                }
            }
        }

        var currentModifierKeys = CurrentlyHeldModifierKeys;

        foreach (var rootKey in _keyState.GetValidVirtualKeys())
        {
            if (_keyState.GetRawValue(rootKey) == pressedValue && rootKey != VirtualKey.SHIFT && rootKey != VirtualKey.MENU && rootKey != VirtualKey.CONTROL)
            {
                if (_isListeningForKeybind)
                {
                    KeybindPressed?.Invoke(new() { RootKey = rootKey, ModifierKeys = currentModifierKeys });
                    _isListeningForKeybind = false;
                    _keyState[rootKey] = false;
                }
                else
                {
                    var keyMapIndex = (byte)currentModifierKeys;
                    Debug.Assert(keyMapIndex < _keyMaps.Length);
                    var keyMap = _keyMaps[keyMapIndex];
                    if (keyMap.TryGetAction(rootKey, out var action))
                    {
                        if (action.RaisePressed())
                        {
                            // If the event was handled, swallow the key so the game doesn't respond to it
                            _keyState[rootKey] = false;
                        }
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
    }
}
