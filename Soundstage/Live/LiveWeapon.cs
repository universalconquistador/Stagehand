using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Soundstage.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Text;

namespace Soundstage.Live;

internal sealed unsafe class LiveWeapon : LiveDrawObject
{
    private Weapon* WeaponPtr => (Weapon*)ObjectPtr;

    public ushort ModelSetId { get => WeaponPtr->ModelSetId; set => WeaponPtr->ModelSetId = value; }
    public ushort SecondaryId { get => WeaponPtr->SecondaryId; set => WeaponPtr->SecondaryId = value; }
    public ushort Variant { get => WeaponPtr->Variant; set => WeaponPtr->Variant = value; }
    public byte PrimaryDye { get => WeaponPtr->Stain0; set => WeaponPtr->Stain0 = value; }
    public byte SecondaryDye { get => WeaponPtr->Stain1; set => WeaponPtr->Stain1 = value; }

    public LiveWeapon(Weapon* weaponPtr) : base((DrawObject*)weaponPtr)
    {
    }

    public override void Dispose()
    {
        WeaponPtr->CleanupRender();
        WeaponPtr->Dtor(DestroyMode.FreeMemory);

        base.Dispose();
    }

    public override bool TryUpdate(ObjectDefinition definition)
    {
        if (definition is WeaponDefinition weaponDefinition)
        {
            if (weaponDefinition.ModelSetId != ModelSetId
                || weaponDefinition.SecondaryId != SecondaryId
                || weaponDefinition.Variant != Variant
                || weaponDefinition.PrimaryDye != PrimaryDye
                || weaponDefinition.SecondaryDye != SecondaryDye)
            {
                WeaponPtr->CleanupRender();
                WeaponModelId newModel = new WeaponModelId()
                {
                    Id = (ushort)weaponDefinition.ModelSetId,
                    Type = (ushort)weaponDefinition.SecondaryId,
                    Variant = (ushort)weaponDefinition.Variant,
                    Stain0 = (byte)weaponDefinition.PrimaryDye,
                    Stain1 = (byte)weaponDefinition.SecondaryDye,
                };
                WeaponPtr->Initialize(&newModel);

                World.Instance()->AddChild((FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object*)WeaponPtr);
                WeaponPtr->OnAddedToWorld();
            }

            Position = weaponDefinition.Position;
            Rotation = weaponDefinition.RotationQuaternion;
            Scale = weaponDefinition.Scale;
            WeaponPtr->UpdateTransforms(false);

            return true;
        }
        else
        {
            return false;
        }
    }
}
