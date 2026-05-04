using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Stagehand.Definitions.ModResources;

/// <summary>
/// The base class for modded resources, which override the data accessed by the game for a specific game path.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(GameModResourceDefinition), "Game")]
[JsonDerivedType(typeof(EmbeddedModResourceDefinition), "Embedded")]
[JsonDerivedType(typeof(DiskModResourceDefinition), "Disk")]
public abstract class ModResourceDefinition
{
    /// <summary>
    /// Visits this mod resource definition with the given visitor type by invoking the corresponding <c>Visit???ModResourceDefinition</c>
    /// function on it.
    /// </summary>
    /// <typeparam name="TVisitor">The visitor type to visit this mod resource definition with.</typeparam>
    /// <typeparam name="TParam">The type of parameter <typeparamref name="TVisitor"/> accepts.</typeparam>
    /// <typeparam name="TResult">The type of result <typeparamref name="TVisitor"/> produces.</typeparam>
    /// <param name="param">The parameter to pass to the visitor type by reference.</param>
    /// <returns>The result produced by the given visitor type visiting this mod resource definition.</returns>
    public abstract TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
        where TVisitor : IModResourceDefinitionVisitor<TParam, TResult>;
}
