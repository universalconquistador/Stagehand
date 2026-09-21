using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Definitions.Objects;

/// <summary>
/// The definition of a group in a Stage definition.
/// </summary>
public class GroupDefinition : ObjectDefinition
{
    /// <summary>
    /// The objects within this group, identified by unique string identifiers.
    /// </summary>
    public Dictionary<string, ObjectDefinition> Objects { get; set; } = new();

    /// <inheritdoc/>
    public override ObjectDefinition Clone()
    {
        var result = new GroupDefinition();
        CopyTo(result);
        return result;
    }

    /// <inheritdoc/>
    public override void CopyTo(ObjectDefinition other)
    {
        base.CopyTo(other);

        if (other is GroupDefinition otherGroup)
        {
            otherGroup.Objects = new(Objects.Select(pair => new KeyValuePair<string, ObjectDefinition>(pair.Key, pair.Value.Clone())));
        }
    }

    /// <inheritdoc/>
    public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
    {
        return TVisitor.VisitGroupDefinition(this, ref param);
    }
}
