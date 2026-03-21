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

    // TODO: Add transaction support
    public IDefinitionEditor? SelectedEditor
    {
        get;
        set
        {
            if (value != SelectedEditor)
            {
                SelectedEditor?.Deselected();
                field = value;
                SelectedEditor?.Selected();

                // When the user selects something, untarget anything targeted
                if (value != null && _targetManager.Target != null)
                {
                    _targetManager.Target = null;
                }
            }
        }
    }

    public SelectionManager(ITargetManager targetManager, IFramework framework)
    {
        _targetManager = targetManager;
        _framework = framework;

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
