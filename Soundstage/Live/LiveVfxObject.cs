using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Soundstage.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Soundstage.Live;

internal sealed unsafe class LiveVfxObject : LiveDrawObject
{
    // best guess at namespace, VfxResourceInstanceListenner is related and belongs in ::Graphics::Vfx
    [StructLayout(LayoutKind.Explicit, Size = 0xD0)]
    public unsafe struct VfxResourceInstance__Internal
    {
        [FieldOffset(0x08)] internal VfxResourceUnk__Internal* VfxResourceUnk;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x20)]
    public unsafe struct VfxResourceUnk__Internal
    {
        [FieldOffset(0x18)] public ApricotResourceHandle* ApricotResourceHandle;
    }

    private VfxObject* VfxObjectPtr => (VfxObject*)ObjectPtr;

    public string VfxResourceGamePath { get; }

    public Vector4 Color
    {
        get => VfxObjectPtr->Color;
        set => VfxObjectPtr->Color = value;
    }

    public LiveVfxObject(VfxObject* vfxObject)
        : base((DrawObject*)vfxObject)
    {
        if (vfxObject == null)
            throw new ArgumentNullException(nameof(vfxObject));

        var vfxResource = (VfxResourceInstance__Internal*)vfxObject->VfxResourceInstance;
        if (vfxResource != null)
        {
            var resourceUnk = vfxResource->VfxResourceUnk;
            if (resourceUnk != null)
            {
                var apricotResourceHandle = resourceUnk->ApricotResourceHandle;
                if (apricotResourceHandle != null)
                {
                    VfxResourceGamePath = apricotResourceHandle->FileName.ToString();
                }
                else
                {
                    VfxResourceGamePath = string.Empty;
                }
            }
            else
            {
                VfxResourceGamePath = string.Empty;
            }
        }
        else
        {
            VfxResourceGamePath = string.Empty;
        }

        // Remove flag that sometimes causes vfx to not appear?
        VfxObjectPtr->SomeFlags &= 0xF7;

        VfxObjectPtr->Update(0.0f);
    }

    public override void Dispose()
    {
        VfxObjectPtr->CleanupRender();
        VfxObjectPtr->Dtor(DestroyFlagsFree);

        base.Dispose();
    }

    public override bool TryUpdate(ObjectDefinition definition)
    {
        if (definition is VfxObjectDefinition vfxDefinition)
        {
            // For now, if a new vfx is requested, create a new LiveVfx
            if (vfxDefinition.VfxGamePath != VfxResourceGamePath)
            {
                return false;
            }

            bool transformChanged = !vfxDefinition.Position.Equals(Position)
                || !vfxDefinition.RotationQuaternion.Equals(Rotation)
                || !vfxDefinition.Scale.Equals(Scale);

            Position = vfxDefinition.Position;
            Rotation = vfxDefinition.RotationQuaternion;
            Scale = vfxDefinition.Scale;
            Color = vfxDefinition.Color;

            if (transformChanged)
            {
                // Often, .avfx need to be totally restarted to pick up their new transform
                //VfxObjectPtr->UpdateTransforms(false);
                VfxObjectPtr->Update(0.0f);
            }


            return true;
        }
        else
        {
            return false;
        }
    }
}
