using Dalamud.Plugin.Services;
using Stagehand.Editor.DefinitionEditors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Stagehand.Editor.Services;

public delegate void SelectedEditorsChanged(IReadOnlyList<IDefinitionEditor> selectedEditors, IDefinitionEditor? primarySelectedEditor);

public interface ISelectionManager
{
    /// <summary>
    /// The definition editors that are currently selected.
    /// </summary>
    IReadOnlyList<IDefinitionEditor> SelectedEditors { get; set; }

    /// <summary>
    /// Raised when <see cref="SelectedEditors"/> has changed.
    /// </summary>
    /// <remarks>
    /// This event is raised inside a transaction; use <see cref="ITransactionManager.QueueCompletionAction(Action)"/>
    /// to queue more transactions.
    /// </remarks>
    event SelectedEditorsChanged SelectedEditorsChanged;

    /// <summary>
    /// Gets which of the <see cref="SelectedEditors"/> is considered the primary selection.
    /// </summary>
    IDefinitionEditor? PrimarySelectedEditor { get; }

    /// <summary>
    /// Adds the given editor to the list of selected editors, if it is not already selected.
    /// </summary>
    /// <param name="editor">The definition editor to add to the selection.</param>
    /// <returns>Whether the editor was successfully added to the selection (i.e. was not already selected).</returns>
    bool TryAddSelectedEditor(IDefinitionEditor editor);

    /// <summary>
    /// Removes the given editor from the list of selected editors, if it is selected.
    /// </summary>
    /// <param name="editor">The definition editor to remove from the selection.</param>
    /// <returns>Whether the editor was successfully removed from the selection (i.e. was selected and now isn't).</returns>
    bool TryRemoveSelectedEditor(IDefinitionEditor editor);
}

internal class SelectionManager : ISelectionManager, IDisposable
{
    private readonly ITargetManager _targetManager;
    private readonly IFramework _framework;
    private readonly ITransactionManager _transactionManager;

    private IDefinitionEditor[] _selectedEditors = Array.Empty<IDefinitionEditor>();
    public IReadOnlyList<IDefinitionEditor> SelectedEditors
    {
        get => _selectedEditors;
        set
        {
            if (!value.SequenceEqual(_selectedEditors))
            {
                var newSelection = value.ToArray(); // Create a defensive copy that we know won't be mutated from other code
                _transactionManager.DoTransaction(new SetPropertyTransaction<SelectionManager, IDefinitionEditor[]>(objectName: null, "Selection", this, newSelection, _selectedEditors, (newValue, oldValue) =>
                {
                    foreach (var oldSelection in oldValue)
                    {
                        if (!newValue.Contains(oldSelection))
                        {
                            oldSelection.Deselected();
                        }
                    }
                    _selectedEditors = newValue;
                    foreach (var newSelection in newValue)
                    {
                        if (!oldValue.Contains(newSelection))
                        {
                            newSelection.Selected();
                        }
                    }

                    // When the user selects something, untarget anything targeted
                    if (newValue.Length > 0 && _targetManager.Target != null)
                    {
                        _targetManager.Target = null;
                    }

                    SelectedEditorsChanged?.Invoke(_selectedEditors, PrimarySelectedEditor);
                }, affectsDataModel: false, SelectedEditorArrayToString));
            }
        }
    }

    public IDefinitionEditor? PrimarySelectedEditor => SelectedEditors.LastOrDefault();

    public event SelectedEditorsChanged? SelectedEditorsChanged;

    public SelectionManager(ITargetManager targetManager, IFramework framework, ITransactionManager transactionManager)
    {
        _targetManager = targetManager;
        _framework = framework;
        _transactionManager = transactionManager;

        framework.Update += OnFrameworkUpdate;
    }

    private static string SelectedEditorArrayToString(IDefinitionEditor[] editors)
    {
        if (editors.Length == 0)
        {
            return "nobody"; // heh
        }
        else if (editors.Length == 1)
        {
            return editors[0].DisplayName;
        }
        else
        {
            return $"{editors[0].DisplayName} + {editors.Length - 1} more";
        }
    }

    public bool TryAddSelectedEditor(IDefinitionEditor editor)
    {
        if (!SelectedEditors.Contains(editor))
        {
            var newSelection = SelectedEditors.Append(editor).ToArray();
            var oldSelection = _selectedEditors;
            _transactionManager.DoTransaction(new DelegateTransaction($"Add {editor.DisplayName} to Selection", () =>
            {
                _selectedEditors = newSelection;
                editor.Selected();
                SelectedEditorsChanged?.Invoke(_selectedEditors, PrimarySelectedEditor);
            }, () =>
            {
                editor.Deselected();
                _selectedEditors = oldSelection;
                SelectedEditorsChanged?.Invoke(_selectedEditors, PrimarySelectedEditor);
            }, affectsDataModel: false));
            return true;
        }
        else
        {
            // Was already selected
            return false;
        }
    }

    public bool TryRemoveSelectedEditor(IDefinitionEditor editor)
    {
        if (SelectedEditors.Contains(editor))
        {
            var newSelection = SelectedEditors.Where(selectedEditor => selectedEditor != editor).ToArray();
            var oldSelection = _selectedEditors;
            _transactionManager.DoTransaction(new DelegateTransaction($"Remove {editor.DisplayName} from Selection", () =>
            {
                _selectedEditors = newSelection;
                editor.Deselected();
                SelectedEditorsChanged?.Invoke(_selectedEditors, PrimarySelectedEditor);
            }, () =>
            {
                editor.Selected();
                _selectedEditors = oldSelection;
                SelectedEditorsChanged?.Invoke(_selectedEditors, PrimarySelectedEditor);
            }, affectsDataModel: false));
            return true;
        }
        else
        {
            // Was not selected in the first place
            return false;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // When the user targets something, deselect any selected editor
        if (_targetManager.Target != null && SelectedEditors.Count > 0)
        {
            SelectedEditors = Array.Empty<IDefinitionEditor>();
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
    }
}
