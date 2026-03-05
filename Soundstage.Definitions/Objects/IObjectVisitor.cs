using System;
using System.Collections.Generic;
using System.Text;

namespace Soundstage.Definitions.Objects;

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
    static abstract TResult VisitBgObjectDefinition(BgObjectDefinition definition, ref TParam param);
    static abstract TResult VisitLightObjectDefinition(LightDefinition definition, ref TParam param);
    static abstract TResult VisitVfxObjectDefinition(VfxObjectDefinition definition, ref TParam param);
    static abstract TResult VisitWeaponObjectDefinition(WeaponDefinition definition, ref TParam param);
}
