using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Services;

/// <summary>
/// A user-facing category of keybindable actions.
/// </summary>
public interface IKeybindGroup
{
    /// <summary>
    /// The user-facing name of this group.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// The actions in this group, sorted by display name.
    /// </summary>
    /// <remarks>
    /// Don't use this list outside of the <see cref="IKeybindService.KeybindGroupLock"/>.
    /// </remarks>
    IReadOnlyList<IKeybindAction> Actions { get; }
}

partial class KeybindService
{
    private sealed record class KeybindGroup(string DisplayName) : IKeybindGroup, IComparable<KeybindGroup>
    {
        private readonly List<KeybindAction> _sortedKeybindActions = new();

        IReadOnlyList<IKeybindAction> IKeybindGroup.Actions => _sortedKeybindActions;

        public int CompareTo(KeybindGroup? other)
        {
            return DisplayName.CompareTo(other?.DisplayName);
        }

        public void AddAction(KeybindAction action)
        {
            var index = _sortedKeybindActions.BinarySearch(action, KeybindActionDisplayNameComparer.Default);
            if (index < 0)
            {
                _sortedKeybindActions.Insert(~index, action);
            }
        }

        public void RemoveAction(KeybindAction action)
        {
            var index = _sortedKeybindActions.BinarySearch(action, KeybindActionDisplayNameComparer.Default);
            if (index >= 0)
            {
                _sortedKeybindActions.RemoveAt(index);
            }
        }
    }
}
