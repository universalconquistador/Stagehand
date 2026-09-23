using Dalamud.Interface;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors;

/// <summary>
/// An editor for a definition that is a child of another definition with a string key.
/// </summary>
public interface IChildDefinitionEditor : IDefinitionEditor
{
    /// <summary>
    /// The key of this editor's definition in its owning definition's container.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// The outliner node that represents this object.
    /// </summary>
    OutlinerNode OutlinerNode { get; }

    /// <summary>
    /// Notifies this object definition editor that it was added to the Stage being edited.
    /// </summary>
    void AddedToStage();

    /// <summary>
    /// Notifies this object definition editor that it was removed from the Stage being edited.
    /// </summary>
    void RemovedFromStage();

    /// <summary>
    /// Removes this definition from its owner definition in a transaction.
    /// </summary>
    void Delete();

    /// <summary>
    /// Creates a copy of this definition and adds it to the owner, in a transaction.
    /// </summary>
    void Duplicate();
}

public static class ChildDefinitionEditorExtensions
{
    /// <summary>
    /// Filters out any editors that have an ancestor also present in the input enumerable.
    /// </summary>
    /// <param name="editors"></param>
    /// <returns></returns>
    public static IEnumerable<IChildDefinitionEditor> WithoutDescendants(this IEnumerable<IChildDefinitionEditor> editors)
    {
        return editors.Where(editor => !(editor is IObjectDefinitionEditor objectEditor) || !objectEditor.GetAncestors().Any(editors.Contains));
    }
}

/// <summary>
/// An editor that is a child in a <see cref="DefinitionEditorDictionary{TDefinition, TEditor}"/>.
/// </summary>
/// <typeparam name="TCollectionItemDefinition">The base definition type of the owning dictionary.</typeparam>
/// <typeparam name="TCollectionItemEditor">The base editor type of the owning dictionary.</typeparam>
public interface IChildDefinitionEditor<TCollectionItemDefinition, TCollectionItemEditor> : IChildDefinitionEditor
    where TCollectionItemEditor : class, IChildDefinitionEditor<TCollectionItemDefinition, TCollectionItemEditor>
{
    DefinitionEditorDictionary<TCollectionItemDefinition, TCollectionItemEditor>? OwnerDictionary { get; set; }
}

/// <summary>
/// Wraps a definition's dictionary of child definitions in a dictionary of corresponding child editors.
/// </summary>
/// <typeparam name="TDefinition"></typeparam>
/// <typeparam name="TEditor"></typeparam>
public class DefinitionEditorDictionary<TDefinition, TEditor> : IDisposable, IReadOnlyDictionary<string, TEditor>
    where TEditor : class, IChildDefinitionEditor<TDefinition, TEditor>
{
    private readonly ITransactionManager _transactionManager;
    private readonly ISelectionManager _selectionManager;

    private readonly Dictionary<string, TDefinition> _objects;
    private readonly OutlinerNode _outlinerNode;
    private readonly Func<TDefinition, string, TEditor> _editorFactory;

    private readonly Dictionary<string, TEditor> _objectEditors = new();

    public IEnumerable<string> Keys => _objectEditors.Keys;
    public IEnumerable<TEditor> Values => _objectEditors.Values;
    public int Count => _objectEditors.Count;
    public TEditor this[string key] => _objectEditors[key];

    public DefinitionEditorDictionary(Dictionary<string, TDefinition> objects, OutlinerNode outlinerNode, Func<TDefinition, string, TEditor> editorFactory,
        ITransactionManager transactionManager, ISelectionManager selectionManager)
    {
        _transactionManager = transactionManager;
        _selectionManager = selectionManager;

        _objects = objects;
        _outlinerNode = outlinerNode;
        _editorFactory = editorFactory;

        foreach (var objectDefinitionPair in objects)
        {
            var newEditor = _editorFactory.Invoke(objectDefinitionPair.Value, objectDefinitionPair.Key);
            _objectEditors[objectDefinitionPair.Key] = newEditor;
            OnEditorAdded(newEditor);
        }
    }

    public TEditor Add(TDefinition newObject, bool select = true)
    {
        var key = Guid.NewGuid().ToString();
        var newEditor = _editorFactory.Invoke(newObject, key);

        using (_transactionManager.BeginTransactionGroup($"Create new {newEditor.TypeInfo.DisplayName}"))
        {
            // Add the new editor
            var transaction = new DelegateTransaction($"Create new {newEditor.TypeInfo.DisplayName}", () =>
            {
                _objects.Add(key, newObject);
                _objectEditors.Add(key, newEditor);
                OnEditorAdded(newEditor);
            }, () =>
            {
                OnEditorRemoved(newEditor);
                _objectEditors.Remove(key);
                _objects.Remove(key);
            }, affectsDataModel: true);
            // If the transaction is permanently undone, dispose the new editor
            transaction.AddDisposable(newEditor, disposeWhenDone: false, disposeWhenUndone: true);
            _transactionManager.DoTransaction(transaction);

            // Select the editor if necessary
            if (select)
            {
                _selectionManager.SelectedEditors = [newEditor];
            }
        }

        return newEditor;
    }

    public void Remove(TEditor objectEditor)
    {
        if (_objectEditors.TryGetValue(objectEditor.Key, out var foundEditor) && foundEditor == objectEditor)
        {
            var definition = _objects[objectEditor.Key];
            using (var transactionGroup = _transactionManager.BeginTransactionGroup($"Delete {objectEditor.DisplayName}"))
            {
                // Deselect the editor if necessary
                _selectionManager.TryRemoveSelectedEditor(foundEditor);

                // Remove the object
                var transaction = new DelegateTransaction($"Delete {objectEditor.DisplayName}", () =>
                {
                    OnEditorRemoved(foundEditor);
                    _objectEditors.Remove(foundEditor.Key);
                    _objects.Remove(foundEditor.Key);
                }, () =>
                {
                    _objects.Add(objectEditor.Key, definition);
                    _objectEditors.Add(objectEditor.Key, foundEditor);
                    OnEditorAdded(foundEditor);
                }, affectsDataModel: true);
                // If the transaction is permanently done, dispose the editor
                transaction.AddDisposable(foundEditor, disposeWhenDone: true, disposeWhenUndone: false);
                _transactionManager.DoTransaction(transaction);
            }
        }
    }

    protected virtual void OnEditorAdded(TEditor editor)
    {
        _outlinerNode.AddChild(editor.OutlinerNode);
        editor.OwnerDictionary = this;
        editor.AddedToStage();
    }

    protected virtual void OnEditorRemoved(TEditor editor)
    {
        editor.RemovedFromStage();
        editor.OwnerDictionary = null;
        _outlinerNode.RemoveChild(editor.OutlinerNode);
    }

    public bool Contains(string key)
    {
        return _objectEditors.ContainsKey(key);
    }

    public void Dispose()
    {
        foreach (var obj in _objectEditors)
        {
            obj.Value.RemovedFromStage();
            obj.Value.Dispose();
            _outlinerNode.RemoveChild(obj.Value.OutlinerNode);
        }
        _objectEditors.Clear();
    }

    public bool ContainsKey(string key)
    {
        return _objectEditors.ContainsKey(key);
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out TEditor value)
    {
        return _objectEditors.TryGetValue(key, out value);
    }

    public IEnumerator<KeyValuePair<string, TEditor>> GetEnumerator()
    {
        return _objectEditors.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)_objectEditors).GetEnumerator();
    }
}
