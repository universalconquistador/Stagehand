using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Stagehand.Live;

internal sealed unsafe class LiveBgObject : LiveDrawObject
{
    private readonly IFramework _framework;

    // Atomic dye application:
    //  - We sometimes need to wait to apply dye until after the model has loaded, as only then will the stain buffer exist.
    //  - This involves scheduling a poll task on the Framework update, but we only want to ever have one of these at a time,
    //    without worrying about race conditions if two threads try to set the dye.
    //  - So, we AtomicExchange a bunch to do this atomically.
    private ulong _applyDyeState = 0;
    private const ulong _applyDyeFlag = (1UL << 63);

    private BgObject* BgObjectPtr => (BgObject*)ObjectPtr;

    public string ModelResourceGamePath { get; }

    public float Transparency
    {
        get => BgObjectPtr->GetTransparency();
        set => BgObjectPtr->SetTransparency(value);
    }

    private Vector4 _dyeColor = Vector4.Zero;
    public Vector4 DyeColor
    {
        get => _dyeColor;
        set
        {
            if (_dyeColor != value)
            {
                _dyeColor = value;
                var srgbColor = new Vector4(MathF.Sqrt(value.X), MathF.Sqrt(value.Y), MathF.Sqrt(value.Z), value.Z) * byte.MaxValue;
                var byteColor = new ByteColor() { R = (byte)srgbColor.X, G = (byte)srgbColor.Y, B = (byte)srgbColor.Z, A = (byte)srgbColor.W };

                if (!BgObjectPtr->TrySetStainColor(byteColor))
                {
                    // Atomically place the 32bit color and the apply flag in the ulong
                    var previousApplyDyeState = Interlocked.Exchange(ref _applyDyeState, _applyDyeFlag | byteColor.RGBA);
                    // If the apply flag was already set, don't do anything--the poller is active and will claim our newly assigned color.
                    // If the apply flag was not already set, start the poller.
                    if ((previousApplyDyeState & _applyDyeFlag) == 0)
                    {
                        _framework.Update += ApplyStainTask;
                    }
                }
            }
        }
    }

    private void ApplyStainTask(IFramework framework)
    {
        var initialApplyDyeState = Interlocked.Read(ref _applyDyeState);

        if ((initialApplyDyeState & _applyDyeFlag) != 0)
        {
            if (BgObjectPtr->StainBuffer != null)
            {
                // Pretty sure we're going to succeed (the stain buffer doesn't usually go non-null to null), so claim the dye state
                initialApplyDyeState = Interlocked.Exchange(ref _applyDyeState, 0);
                if ((initialApplyDyeState & _applyDyeFlag) != 0)
                {
                    var color = new ByteColor() { RGBA = (uint)(initialApplyDyeState & uint.MaxValue) };
                    var success = BgObjectPtr->TrySetStainColor(color);

                    if (success)
                    {
                        // Mission accomplished! No longer need this apply task
                        _framework.Update -= ApplyStainTask;
                    }
                    else
                    {
                        // We need to try again by putting the task back in, if nothing else has started another task (it is still zero)
                        var previous = Interlocked.CompareExchange(ref _applyDyeState, initialApplyDyeState, 0);
                        if (previous == 0)
                        {
                            // Great! We are once again the only running apply task, so don't do anything else and wait for the next tick
                        }
                        else
                        {
                            // Uh oh, another apply task was already started. Unregister this one.
                            _framework.Update -= ApplyStainTask;
                        }
                    }
                }
                else
                {
                    // The flag was set to zero by something else, probably dispose--stop the task.
                    _framework.Update -= ApplyStainTask;
                }
            }
            else
            {
                // Can't set the stain buffer yet, so don't do anything and wait for the next Framework tick
            }
        }
        else
        {
            // The flag was set to zero by something else, probably dispose--stop the task.
            _framework.Update -= ApplyStainTask;
        }
    }

    public LiveBgObject(IFramework framework, BgObject* bgObject, string modelResourceGamePath, ILiveModpack? modpack)
        : base((DrawObject*)bgObject, modpack)
    {
        if (bgObject == null)
            throw new ArgumentNullException(nameof(bgObject));

        _framework = framework;

        ModelResourceGamePath = modelResourceGamePath;
    }

    public override void Dispose()
    {
        // Make sure the apply stain task does not try to use this bgobject anymore
        Interlocked.Exchange(ref _applyDyeState, 0);

        BgObjectPtr->CleanupRender();
        BgObjectPtr->Dtor(DestroyFlagsFree);

        base.Dispose();
    }

    public override bool TryUpdate(ObjectDefinition definition, ILiveModpack? modpack)
    {
        // If we are adding or removing a modpack or making a material change to the modpack, we can't update in place
        if ((modpack == null) != (Modpack == null)
            || (Modpack != null && modpack != null && Modpack.EffectsHash != modpack.EffectsHash))
        {
            return false;
        }

        if (definition is BgObjectDefinition bgObjectDefinition)
        {
            var finalGamePath = bgObjectDefinition.ModelGamePath;
            if (modpack != null)
            {
                finalGamePath = ResourceRedirectionHelpers.MakeModpackPath(finalGamePath, modpack);
            }

            if (finalGamePath != ModelResourceGamePath)
            {
                return false;
            }
            else
            {
                Position = bgObjectDefinition.Position;
                Rotation = bgObjectDefinition.RotationQuaternion;
                Scale = bgObjectDefinition.Scale;
                DyeColor = bgObjectDefinition.DyeColor;
                Transparency = 1.0f - bgObjectDefinition.Opacity;
                if (BgObjectPtr->ModelResourceHandle != null && BgObjectPtr->ModelResourceHandle->LoadState >= 7)
                {
                    BgObjectPtr->UpdateTransforms(false);
                }
                return true;
            }
        }
        else
        {
            return false;
        }
    }

    public override bool TryGetOrientedBounds(out FFXIVClientStructs.FFXIV.Common.Math.OrientedBounds orientedBounds)
    {
        // Attempting to query the bounds of a BgObject whose model is loading results in an access violation
        if (BgObjectPtr->ModelResourceHandle == null || BgObjectPtr->ModelResourceHandle->LoadState < 7)
        {
            orientedBounds = default;
            return false;
        }
        else
        {
            return base.TryGetOrientedBounds(out orientedBounds);
        }
    }
}

