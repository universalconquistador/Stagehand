using Stagehand.Definitions.Objects;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using System;
using System.Collections.Generic;
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
}

/// <summary>
/// Wraps a definition's dictionary of child definitions in a dictionary of corresponding child editors.
/// </summary>
/// <typeparam name="TDefinition"></typeparam>
/// <typeparam name="TEditor"></typeparam>
public class DefinitionEditorDictionary<TDefinition, TEditor> : IDisposable
    where TEditor : class, IChildDefinitionEditor
{
    private readonly ITransactionManager _transactionManager;
    private readonly ISelectionManager _selectionManager;

    private readonly Dictionary<string, TDefinition> _objects;
    private readonly OutlinerNode _outlinerNode;
    private readonly Func<TDefinition, string, TEditor> _editorFactory;

    private readonly Dictionary<string, TEditor> _objectEditors = new();

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
            _outlinerNode.AddChild(newEditor.OutlinerNode);
            newEditor.AddedToStage();
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
                _outlinerNode.AddChild(newEditor.OutlinerNode);
                newEditor.AddedToStage();
            }, () =>
            {
                newEditor.RemovedFromStage();
                _outlinerNode.RemoveChild(newEditor.OutlinerNode);
                _objectEditors.Remove(key);
                _objects.Remove(key);
            }, affectsDataModel: true);
            // If the transaction is permanently undone, dispose the new editor
            transaction.AddDisposable(newEditor, disposeWhenDone: false, disposeWhenUndone: true);
            _transactionManager.DoTransaction(transaction);

            // Select the editor if necessary
            if (select)
            {
                _selectionManager.SelectedEditor = newEditor;
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
                if (_selectionManager.SelectedEditor == foundEditor)
                {
                    _selectionManager.SelectedEditor = null;
                }

                // Remove the object
                var transaction = new DelegateTransaction($"Delete {objectEditor.DisplayName}", () =>
                {
                    foundEditor.RemovedFromStage();
                    _outlinerNode.RemoveChild(foundEditor.OutlinerNode);
                    _objectEditors.Remove(objectEditor.Key);
                    _objects.Remove(objectEditor.Key);
                }, () =>
                {
                    _objects.Add(objectEditor.Key, definition);
                    _objectEditors.Add(objectEditor.Key, foundEditor);
                    _outlinerNode.AddChild(foundEditor.OutlinerNode);
                    foundEditor.AddedToStage();
                }, affectsDataModel: true);
                // If the transaction is permanently done, dispose the editor
                transaction.AddDisposable(foundEditor, disposeWhenDone: true, disposeWhenUndone: false);
                _transactionManager.DoTransaction(transaction);
            }
        }
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
}
