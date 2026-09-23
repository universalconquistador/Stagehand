using Dalamud.Interface;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.Services;
using Stagehand.Live;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class GroupDefinitionEditor : ObjectDefinitionEditor<GroupDefinition>
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Group", "A collection that can hold child objects.", FontAwesomeIcon.ObjectGroup);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    public ObjectDefinitionEditorDictionary Objects { get; }

    public override ObjectDefinitionEditorDictionary? ChildObjects => Objects;

    public override ObjectScaleMode ScaleMode => ObjectScaleMode.Uniform;
    public override bool ShowModpackSelector => false; // Until the projection texture is added

    public GroupDefinitionEditor(IServiceProvider serviceProvider, GroupDefinition definition, string key, StageDefinitionEditor stage)
        : base(serviceProvider, definition, key, stage)
    {
        Objects = new(this, definition.Objects, OutlinerNode, CreateEditorForObjectDefinition, TransactionManager, SelectionManager);
    }

    protected override IEnumerable<OutlinerContextMenuItem> GenerateContextMenuItems()
    {
        yield return new KeybindOutlinerContextMenuItem(StagehandKeybinds.EditorCenterGroupPivots, _ => Stage.CenterSelectedGroupPivots());
        yield return new KeybindOutlinerContextMenuItem(StagehandKeybinds.EditorUngroupObjects, _ => Stage.UngroupSelectedObjects());
        foreach (var baseItem in base.GenerateContextMenuItems())
        {
            yield return baseItem;
        }
    }

    private IObjectDefinitionEditor CreateEditorForObjectDefinition(ObjectDefinition objectDefinition, string objectKey)
    {
        var result = Stage.CreateEditorForObjectDefinition(objectDefinition, objectKey);
        PropagateTransform(result);
        return result;
    }

    public override void UpdateIsEnabled()
    {
        base.UpdateIsEnabled();
        foreach (var child in Objects)
        {
            child.Value.UpdateIsEnabled();
        }
    }

    private void PropagateTransform(IObjectDefinitionEditor childEditor)
    {
        childEditor.SetParentTransform(WorldPosition, WorldRotationQuaternion, WorldScale.X);
    }

    private void PropagateTransformToAll()
    {
        foreach (var childObject in Objects.Values)
        {
            PropagateTransform(childObject);
        }
    }

    protected override void SetPositionInternal(Vector3 position)
    {
        base.SetPositionInternal(position);
        PropagateTransformToAll();
    }

    protected override void SetRotationQuaternionInternal(Quaternion rotationQuaternion)
    {
        base.SetRotationQuaternionInternal(rotationQuaternion);
        PropagateTransformToAll();
    }

    protected override void SetRotationPitchYawRollDegreesInternal(Vector3 rotationPYRDegrees)
    {
        base.SetRotationPitchYawRollDegreesInternal(rotationPYRDegrees);
        PropagateTransformToAll();
    }

    protected override void SetScaleInternal(Vector3 scale)
    {
        base.SetScaleInternal(new Vector3(scale.X));
        PropagateTransformToAll();
    }

    public override void SetParentTransform(Vector3 parentTranslation, Quaternion parentRotation, float parentUniformScale)
    {
        base.SetParentTransform(parentTranslation, parentRotation, parentUniformScale);
        PropagateTransformToAll();
    }

    public override void RefreshPreviewObject()
    {
        // We don't want to preview groups because the child editors are already previewing themselves as appropriate.
        PreviewLiveObject = null;
    }

    public override void AddedToStage()
    {
        base.AddedToStage();
        Objects.AddedToStage();
    }

    public override void RemovedFromStage()
    {
        Objects.RemovedFromStage();
        base.RemovedFromStage();
    }

    public override bool TryGetOrientedBounds(out FFXIVClientStructs.FFXIV.Common.Math.OrientedBounds orientedBounds)
    {
        orientedBounds = default;
        return LiveGroup.TryGetGroupOrientedBounds(WorldTransform, Objects.Values.Select<IObjectDefinitionEditor, FFXIVClientStructs.FFXIV.Common.Math.OrientedBounds?>(childEditor => childEditor.TryGetOrientedBounds(out var bounds) ? bounds : null), out orientedBounds);
    }

    protected override void DrawOverlays(IOverlayDrawContext obj)
    {
        // Because we have no live preview object (see comment in RefreshPreviewObject) we need to compute the bounds manually.
        if (IsSelected)
        {
            var color = ComputeOverlayColor();

            if (TryGetOrientedBounds(out var orientedBounds))
            {
                obj.DrawBox(orientedBounds.Transform, orientedBounds.HalfExtents, 2.0f, color);
            }
        }
    }

    public void Dissolve()
    {
        var newOwner = OwnerDictionary ?? Stage.Objects;

        using (TransactionManager.BeginTransactionGroup($"Ungroup {DisplayName}"))
        {
            foreach (var child in Objects.Values.ToArray())
            {
                var worldPosition = child.WorldPosition;
                var worldRotation = child.WorldRotationQuaternion;
                var worldScale = child.WorldScale;

                var definition = Objects.Remove(child);
                if (definition != null)
                {
                    var newEditor = newOwner.Add(definition);
                    newEditor.WorldPosition = worldPosition;
                    newEditor.WorldRotationQuaternion = worldRotation;
                    newEditor.WorldScale = worldScale;
                }
            }

            Delete();
        }
    }

    public void CenterPivot()
    {
        if (LiveGroup.TryGetGroupOrientedBounds(
            WorldTransform,
            Objects.Values.Select<IObjectDefinitionEditor, FFXIVClientStructs.FFXIV.Common.Math.OrientedBounds?>(obj => obj.TryGetOrientedBounds(out var bounds) ? bounds : null),
            out var bounds))
        {
            if (Matrix4x4.Decompose(bounds.Transform, out _, out var boundsRotation, out var boundsTranslation))
            {
                using (TransactionManager.BeginTransactionGroup($"Center Pivot of {DisplayName}"))
                {
                    var deltaTranslation = boundsTranslation - WorldPosition;
                    WorldPosition = boundsTranslation;
                    foreach (var child in Objects.Values)
                    {
                        child.WorldPosition += -deltaTranslation;
                    }
                }
            }
        }
    }

    public override void Dispose()
    {
        Objects.Dispose();

        base.Dispose();
    }
}
