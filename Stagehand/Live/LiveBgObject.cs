using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Live;

internal sealed unsafe class LiveBgObject : LiveDrawObject
{
    private BgObject* BgObjectPtr => (BgObject*)ObjectPtr;

    public string ModelResourceGamePath { get; }

    public float Transparency
    {
        get => BgObjectPtr->GetTransparency();
        set => BgObjectPtr->SetTransparency(value);
    }

    public Vector4 DyeColor
    {
        get => BgObjectPtr->StainBuffer != null ? BgObjectPtr->StainBuffer->LinearFloatColor : Vector4.One;
        set
        {
            var srgbColor = new Vector4(MathF.Sqrt(value.X), MathF.Sqrt(value.Y), MathF.Sqrt(value.Z), value.Z) * byte.MaxValue;
            var byteColor = new ByteColor() { R = (byte)srgbColor.X, G = (byte)srgbColor.Y, B = (byte)srgbColor.Z, A = (byte)srgbColor.W };

            BgObjectPtr->TrySetStainColor(byteColor);
        }
    }

    public LiveBgObject(BgObject* bgObject)
        : base((DrawObject*)bgObject)
    {
        if (bgObject == null)
            throw new ArgumentNullException(nameof(bgObject));

        if (bgObject->ModelResourceHandle != null)
        {
            ModelResourceGamePath = bgObject->ModelResourceHandle->FileName.ToString();
        }
        else
        {
            ModelResourceGamePath = string.Empty;
        }
    }

    public override void Dispose()
    {
        BgObjectPtr->CleanupRender();
        BgObjectPtr->Dtor(DestroyFlagsFree);

        base.Dispose();
    }

    public override bool TryUpdate(ObjectDefinition definition)
    {
        if (definition is BgObjectDefinition bgObjectDefinition)
        {
            if (bgObjectDefinition.ModelGamePath != ModelResourceGamePath)
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

