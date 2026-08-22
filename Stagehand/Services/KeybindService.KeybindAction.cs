using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Services;

/// <summary>
/// A bindable action that has been registered with an <see cref="IKeybindService"/>.
/// </summary>
public interface IKeybindAction
{
    /// <summary>
    /// The user-facing description of this action.
    /// </summary>
    KeybindInfo Info { get; }

    /// <summary>
    /// Raised when the keybind assigned to this action is pressed.
    /// </summary>
    /// <remarks>
    /// If there are any handlers attached to this event, then key presses that trigger it
    /// are suppressed from being handled by the game.
    /// </remarks>
    event Action Pressed;

    /// <summary>
    /// The keybind currently assigned to this action.
    /// </summary>
    /// <remarks>
    /// To change this, call <see cref="IKeybindService.TrySetActionKeybind(IKeybindAction, Keybind)"/>.
    /// </remarks>
    Keybind CurrentKeybind { get; }
}

partial class KeybindService
{
    private sealed record class KeybindAction(KeybindInfo Info, Keybind CurrentKeybind) : IKeybindAction
    {
        public event Action? Pressed;

        public Keybind CurrentKeybind { get; set; } = CurrentKeybind;

        public bool RaisePressed()
        {
            var pressed = Pressed;
            if (pressed != null)
            {
                pressed.Invoke();
            }

            return pressed != null;
        }
    }

    /// <summary>
    /// Compares <see cref="KeybindAction"/>s by using their <see cref="Keybind.RootKey"/>.
    /// </summary>
    private class KeybindActionRootKeyComparer : IComparer<KeybindAction>
    {
        public static readonly KeybindActionRootKeyComparer Default = new();

        public int Compare(KeybindAction? x, KeybindAction? y)
        {
            if (x == null)
            {
                return y == null ? 0 : -y.CurrentKeybind.RootKey.CompareTo(x);
            }
            else
            {
                return x.CurrentKeybind.RootKey.CompareTo(y?.CurrentKeybind.RootKey);
            }
        }
    }

    private class KeybindActionDisplayNameComparer : IComparer<KeybindAction>
    {
        public static readonly KeybindActionDisplayNameComparer Default = new();

        public int Compare(KeybindAction? x, KeybindAction? y)
        {
            if (x == null)
            {
                return y == null ? 0 : -y.Info.DisplayName.CompareTo(x);
            }
            else
            {
                return x.Info.DisplayName.CompareTo(y?.Info.DisplayName);
            }
        }
    }
}
