using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Stagehand.Services;

/// <summary>
/// A sink of input events for the game viewport.
/// </summary>
public interface IViewportInputHandler
{
    /// <summary>
    /// Process the given input data.
    /// </summary>
    /// <param name="inputData">The input data to process.</param>
    /// <returns>True to suppress the lower-priority handlers (including the native game viewport) or false to continue passing the input down the chain of handlers.</returns>
    bool HandleMouseInput(ref readonly UIInputData inputData);
}

/// <summary>
/// Handles processing of viewport input; that is, input that the native game UI does not consume.
/// </summary>
public interface IViewportInputService
{
    /// <summary>
    /// Adds the given input handler with the given priority.
    /// </summary>
    /// <remarks>
    /// This does not check whether the given input handler has already been added.
    /// </remarks>
    /// <param name="inputHandler">The input handler to add.</param>
    /// <param name="priority">The priority of the input handler, where handlers with higher priorities handle input before those with lower priorities.</param>
    void AddInputHandler(IViewportInputHandler inputHandler, float priority);

    /// <summary>
    /// Removes the given input handler, if it has been added.
    /// </summary>
    /// <param name="inputHandler">The input handler to remove.</param>
    void RemoveInputHandler(IViewportInputHandler inputHandler);
}

// TODO: Implement mouse capture for dragging?
// TODO: Split into individual events for down/drag/up/move?
// TODO: Split into events per mouse button?
// TODO: Let handlers only consume specific mouse buttons?

internal unsafe class ViewportInputService : IViewportInputService, IDisposable
{
    private record class RegisteredInputHandler(IViewportInputHandler InputHandler, float Priority);

    private Hook<AtkModule.Delegates.HandleInput> _atkModuleHandleUpdateHook;
    private LinkedList<RegisteredInputHandler> _inputHandlers = new();
    private SpinLock _handlerListLock = new();

    public ViewportInputService(IGameInteropProvider gameInteropProvider)
    {
        _atkModuleHandleUpdateHook = gameInteropProvider.HookFromAddress<AtkModule.Delegates.HandleInput>(AtkModule.MemberFunctionPointers.HandleInput, AtkModuleHandleInput);
        _atkModuleHandleUpdateHook.Enable();
    }

    public void AddInputHandler(IViewportInputHandler inputHandler, float priority)
    {
        var newNode = new LinkedListNode<RegisteredInputHandler>(new RegisteredInputHandler(inputHandler, priority));

        bool lockHeld = false;
        while (!lockHeld)
        {
            _handlerListLock.Enter(ref lockHeld);
        }

        try
        {
            var next = _inputHandlers.First;

            while (next != null && next.Value.Priority > priority)
            {
                next = next.Next;
            }

            if (next != null)
            {
                _inputHandlers.AddBefore(next, newNode);
            }
            else
            {
                _inputHandlers.AddLast(newNode);
            }
        }
        finally
        {
            _handlerListLock.Exit();
        }
    }

    public void RemoveInputHandler(IViewportInputHandler inputHandler)
    {
        bool lockHeld = false;
        while (!lockHeld)
        {
            _handlerListLock.Enter(ref lockHeld);
        }

        try
        {
            var node = _inputHandlers.First;
            while (node != null)
            {
                if (node.Value.InputHandler == inputHandler)
                {
                    _inputHandlers.Remove(node);
                    break;
                }
                node = node.Next;
            }
        }
        finally
        {
            _handlerListLock.Exit();
        }
    }

    private byte AtkModuleHandleInput(AtkModule* thisPtr, UIInputData* inputData, bool isPadMouseModeEnabled)
    {
        // Call original input handler to let the native UI handle input
        byte result = _atkModuleHandleUpdateHook.Original(thisPtr, inputData, isPadMouseModeEnabled);

        bool lockHeld = false;
        while (!lockHeld)
        {
            _handlerListLock.Enter(ref lockHeld);
        }

        bool inputHandled = false;

        try
        {
            var node = _inputHandlers.First;
            while (!inputHandled && node != null)
            {
                inputHandled = node.Value.InputHandler.HandleMouseInput(ref *inputData);
                node = node.Next;
            }
        }
        finally
        {
            _handlerListLock.Exit();
        }

        if (inputHandled)
        {
            // This is a replica of what Original AtkModule.HandleInput does when the mouse is over a collision node
            inputData->FilterUICursorInputs(MouseButtonFlags.LBUTTON | MouseButtonFlags.RBUTTON);
            inputData->FilterDragInputs();
            if (isPadMouseModeEnabled)
            {
                inputData->FilterGamepadInputs();
            }
            result = 1;
        }

        return result;
    }

    public void Dispose()
    {
        _atkModuleHandleUpdateHook.Dispose();
    }
}
