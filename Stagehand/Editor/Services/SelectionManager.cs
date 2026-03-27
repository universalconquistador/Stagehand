using Dalamud.Plugin.Services;
using Stagehand.Editor.DefinitionEditors;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Editor.Services;

public interface ISelectionManager
{
    /// <summary>
    /// The definition editor that is currently selected.
    /// </summary>
    IDefinitionEditor? SelectedEditor { get; set; }
}

internal class SelectionManager : ISelectionManager, IDisposable
{
    private readonly ITargetManager _targetManager;
    private readonly IFramework _framework;
    private readonly ITransactionManager _transactionManager;

    public IDefinitionEditor? SelectedEditor
    {
        get;
        set
        {
            if (value != SelectedEditor)
            {
                _transactionManager.DoTransaction(new SetPropertyTransaction<SelectionManager, IDefinitionEditor?>(objectName: null, "selection", this, value, SelectedEditor, (newValue, oldValue) =>
                {
                    oldValue?.Deselected();
                    field = newValue;
                    newValue?.Selected();

                    // When the user selects something, untarget anything targeted
                    if (newValue != null && _targetManager.Target != null)
                    {
                        _targetManager.Target = null;
                    }
                    
                }, affectsDataModel: false));
            }
        }
    }

    public SelectionManager(ITargetManager targetManager, IFramework framework, ITransactionManager transactionManager)
    {
        _targetManager = targetManager;
        _framework = framework;
        _transactionManager = transactionManager;

        framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // When the user targets something, deselect any selected editor
        if (_targetManager.Target != null && SelectedEditor != null)
        {
            SelectedEditor = null;
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
    }
}
