using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Definitions.ModResources;

/// <summary>
/// The interface for a class that statically implements the visitor pattern to visit mod resource definitions.
/// </summary>
/// <remarks>
/// The visitor pattern lets developers handle each individual kind of <see cref="ModResourceDefinition"/> without
/// manually comparing types or using reflection, and visiting code will fail to compile if new subclasses are added
/// without being handled.
/// </remarks>
/// <typeparam name="TParam">The type of parameter to pass through to the visitor.</typeparam>
/// <typeparam name="TResult">The type of value being returned from the visitor.</typeparam>
public interface IModResourceDefinitionVisitor<TParam, TResult>
    where TParam : allows ref struct
{
    /// <summary>
    /// Visits a <see cref="GameModResourceDefinition"/>.
    /// </summary>
    /// <param name="definition">The game mod resource definition to visit.</param>
    /// <param name="param">The parameter passed to the visitor.</param>
    /// <returns>The result of visiting the game mod resource definition.</returns>
    static abstract TResult VisitGameModResourceDefinition(GameModResourceDefinition definition, ref TParam param);

    /// <summary>
    /// Visits a <see cref="EmbeddedModResourceDefinition"/>.
    /// </summary>
    /// <param name="definition">The embedded mod resource definition to visit.</param>
    /// <param name="param">The parameter passed to the visitor.</param>
    /// <returns>The result of visiting the embedded mod resource definition.</returns>
    static abstract TResult VisitEmbeddedModResourceDefinition(EmbeddedModResourceDefinition definition, ref TParam param);

    /// <summary>
    /// Visits a <see cref="DiskModResourceDefinition"/>.
    /// </summary>
    /// <param name="definition">The disk mod resource definition to visit.</param>
    /// <param name="param">The parameter passed to the visitor.</param>
    /// <returns>THe result of visiting the disk mod resource definition.</returns>
    static abstract TResult VisitDiskModResourceDefinition(DiskModResourceDefinition definition, ref TParam param);
}
