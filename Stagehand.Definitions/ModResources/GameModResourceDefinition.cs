using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Definitions.ModResources;

/// <summary>
/// Provides a resource from a file in the game data.
/// </summary>
public class GameModResourceDefinition : ModResourceDefinition
{
    /// <summary>
    /// The game path of the vanilla resource to use.
    /// </summary>
    public string SourceGamePath { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
    {
        return TVisitor.VisitGameModResourceDefinition(this, ref param);
    }
}
