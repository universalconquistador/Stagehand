using Dalamud.Game.ClientState.Keys;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Stagehand.Services;

internal partial class KeybindService
{
    /// <summary>
    /// A lookup structure for finding which action corresponds to which root key for a given combination of modifier keys.
    /// </summary>
    private class KeyMap
    {
        // Sorted by their CurrentKeybind's RootKey
        private readonly List<KeybindAction> _sortedActions = new();

        // We don't really care about atomicity at all for general keybind usage, we just need to be absolutely 100% sure we aren't
        // going to corrupt anything in each individual operation. We expect overwhelmingly negligible contention but might as well be sure.
        private readonly Lock _lock = new();

        public bool TryAddAction(KeybindAction action, [NotNullWhen(false)] out KeybindAction? conflictingAction)
        {
            lock (_lock)
            {
                var index = _sortedActions.BinarySearch(action, KeybindActionRootKeyComparer.Default);
                if (index < 0)
                {
                    _sortedActions.Insert(~index, action);
                    conflictingAction = null;
                    return true;
                }
                else
                {
                    // Already an action with the given keybind! (perhaps this one!)
                    conflictingAction = _sortedActions[index];
                    return false;
                }
            }
        }

        public bool TryRemoveAction(KeybindAction action)
        {
            lock (_lock)
            {
                var index = _sortedActions.BinarySearch(action, KeybindActionRootKeyComparer.Default);
                if (index >= 0)
                {
                    _sortedActions.RemoveAt(index);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        // Unfortunately there's no way to BinarySearch a List<T> without providing an actual T instance, so each thread reuses this dummy KeybindAction
        // to search for actions with a given root key.
        private readonly ThreadLocal<KeybindAction> _dummyQueryAction = new(trackAllValues: false);
        public bool TryGetAction(VirtualKey rootKey, [NotNullWhen(true)] out KeybindAction? action)
        {
            if (!_dummyQueryAction.IsValueCreated || _dummyQueryAction.Value == null)
            {
                _dummyQueryAction.Value = new KeybindAction(new KeybindInfo(string.Empty, string.Empty, string.Empty, string.Empty, Keybind.Unassigned), Keybind.Unassigned);
            }
            var searchAction = _dummyQueryAction.Value;
            searchAction.CurrentKeybind = new() { RootKey = rootKey, ModifierKeys = KeybindModifierKeys.None };
            lock (_lock)
            {
                var index = _sortedActions.BinarySearch(searchAction, KeybindActionRootKeyComparer.Default);
                if (index >= 0)
                {
                    action = _sortedActions[index];
                    return true;
                }
                else
                {
                    action = null;
                    return false;
                }
            }
        }
    }
}
