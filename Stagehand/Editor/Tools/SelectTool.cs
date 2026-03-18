using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Editor.Tools;

internal class SelectTool : EditorToolBase
{
    public SelectTool()
        : base("Select Tool", "Select objects in the game by clicking on them.", FontAwesomeIcon.MousePointer, sortPriority: 0.0f)
    {
    }
}
