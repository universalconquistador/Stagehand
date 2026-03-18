using Dalamud.Interface;
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
    protected IServiceProvider ServiceProvider { get; }

    public abstract string DisplayName { get; }
    public abstract DefinitionTypeInfo TypeInfo { get; }

    public bool IsSelected { get; private set; }

    public DefinitionEditorBase(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    protected virtual void SetPropertyValue<TValue>(Action<TValue> setter, TValue value, [CallerMemberName] string? propertyName = null)
    {
        // TODO: Hook into transaction system
        setter.Invoke(value);
    }

    public abstract void DrawProperties();

    public virtual void Selected()
    {
        IsSelected = true;    
    }

    public virtual void Deselected()
    {
        IsSelected = false;
    }

    public virtual void Dispose()
    {
        if (IsSelected)
        {
            Deselected();
        }
    }
}
