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

internal class SelectionManager : ISelectionManager
{
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
            }
        }
    }
}
