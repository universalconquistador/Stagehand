using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Editor.Services;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors;

public record class DefinitionTypeInfo(string DisplayName, string Description, FontAwesomeIcon Icon);

/// <summary>
/// Wraps a definition with editing logic including UI display and transactions.
/// </summary>
public interface IDefinitionEditor : IDisposable
{
    /// <summary>
    /// The user-facing name of this editor.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets basic information about the kind of definition this editor represents.
    /// </summary>
    DefinitionTypeInfo TypeInfo { get; }

    /// <summary>
    /// Whether this definition is currently selected by the user.
    /// </summary>
    bool IsSelected { get; }

    /// <summary>
    /// Draws the properties of the definition.
    /// </summary>
    void DrawProperties();

    /// <summary>
    /// Notifies this editor that it has been selected.
    /// </summary>
    void Selected();

    /// <summary>
    /// Notifies this editor that it has been deselected.
    /// </summary>
    void Deselected();
}

public abstract class DefinitionEditorBase : IDefinitionEditor
{
    private bool _draggingProperty = false;

    protected IServiceProvider ServiceProvider { get; }
    protected ITransactionManager TransactionManager { get; }

    public abstract string DisplayName { get; }
    public abstract DefinitionTypeInfo TypeInfo { get; }

    public bool IsSelected { get; private set; }

    public DefinitionEditorBase(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        TransactionManager = ServiceProvider.GetRequiredService<ITransactionManager>();
    }

    protected virtual void SetPropertyValue<TValue>(Action<TValue> setter, TValue newValue, TValue oldValue, [CallerMemberName] string? propertyName = null, bool affectsDataModel = true)
    {
        // UBER HACK: Dragging a property should be a transaction group! There's not really a great way to detect this right now, so this is what we've got.
        // Really this does not belong here, and even more importantly this is not necessarily called from an ImGui draw or even the right thread!
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && !_draggingProperty)
        {
            _draggingProperty = true;
            TransactionManager.PushTransactionGroup($"Set {DisplayName}'s {propertyName} to {newValue}");
        }

        TransactionManager.DoTransaction(new SetPropertyTransaction<IDefinitionEditor, TValue>(DisplayName, propertyName ?? string.Empty, this, newValue, oldValue, (@new, old) => setter.Invoke(@new), affectsDataModel));
    }

    public void DrawProperties()
    {
        OnDrawProperties();

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && _draggingProperty)
        {
            TransactionManager.PopTransactionGroup(adoptLastTitle: true);
            _draggingProperty = false;
        }
    }

    protected abstract void OnDrawProperties();

    public virtual void Selected()
    {
        IsSelected = true;    
    }

    public virtual void Deselected()
    {
        // UBER HACK: Backstop to make sure if we stop drawing we still end a property drag transaction group
        if (_draggingProperty)
        {
            TransactionManager.PopTransactionGroup();
            _draggingProperty = false;
        }

        IsSelected = false;
    }

    public virtual void Dispose()
    {
        if (IsSelected)
        {
            Deselected();
        }
    }

    public override string ToString()
    {
        return DisplayName;
    }
}
