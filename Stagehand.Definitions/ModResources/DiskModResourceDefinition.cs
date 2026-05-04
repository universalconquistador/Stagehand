using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Stagehand.Definitions.ModResources;

/// <summary>
/// Provides a resource from a file on disk.
/// </summary>
public class DiskModResourceDefinition : ModResourceDefinition
{
    /// <summary>
    /// The full path to the file on disk to load the resource from.
    /// </summary>
    public string SourceDiskPath { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
    {
        return TVisitor.VisitDiskModResourceDefinition(this, ref param);
    }
}
