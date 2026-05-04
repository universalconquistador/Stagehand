using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Definitions.Objects;

/// <summary>
/// The interface for a class that statically implements the visitor pattern to visit object definitions.
/// </summary>
/// <remarks>
/// The visitor pattern lets developers handle a <see cref="ObjectDefinition"/> as its concrete type without
/// manually comparing types or using reflection, and visiting code will fail to compile if new subclasses are added
/// without being handled.
/// </remarks>
/// <typeparam name="TParam">The type of parameter to pass through to the visitor.</typeparam>
/// <typeparam name="TResult">The type of value being returned from the visitor.</typeparam>
public interface IObjectVisitor<TParam, TResult>
{
    /// <summary>
    /// Visits a <see cref="BgObjectDefinition"/>.
    /// </summary>
    /// <param name="definition">The background object definition to visit.</param>
    /// <param name="param">The parameter passed to the visitor.</param>
    /// <returns>The result of visiting the background object definition.</returns>
    static abstract TResult VisitBgObjectDefinition(BgObjectDefinition definition, ref TParam param);

    /// <summary>
    /// Visits a <see cref="LightDefinition"/>.
    /// </summary>
    /// <param name="definition">The light definition to visit.</param>
    /// <param name="param">The parameter passed to the visitor.</param>
    /// <returns>The result of visiting the light definition.</returns>
    static abstract TResult VisitLightDefinition(LightDefinition definition, ref TParam param);

    /// <summary>
    /// Visits a <see cref="VfxObjectDefinition"/>.
    /// </summary>
    /// <param name="definition">The VFX object definition to visit.</param>
    /// <param name="param">The parameter passed to the visitor.</param>
    /// <returns>The result of visiting the VFX object definition.</returns>
    static abstract TResult VisitVfxObjectDefinition(VfxObjectDefinition definition, ref TParam param);

    /// <summary>
    /// Visits a <see cref="WeaponDefinition"/>.
    /// </summary>
    /// <param name="definition">The weapon definition to visit.</param>
    /// <param name="param">The parameter passed to the visitor.</param>
    /// <returns>The result of visiting the weapon definition.</returns>
    static abstract TResult VisitWeaponDefinition(WeaponDefinition definition, ref TParam param);
}
