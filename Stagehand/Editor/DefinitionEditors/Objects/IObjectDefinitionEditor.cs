using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.Services;
using Stagehand.Live;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

/// <summary>
/// A definition editor that edits an object definition.
/// </summary>
public interface IObjectDefinitionEditor : IDefinitionEditor
{
    /// <summary>
    /// The object key in the stage definition.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// The outliner node that represents this object.
    /// </summary>
    OutlinerNode OutlinerNode { get; }

    /// <summary>
    /// This object's position in world space.
    /// </summary>
    Vector3 Position { get; set; }

    /// <summary>
    /// This object's rotation in world space as pitch, yaw, and roll angles in degrees.
    /// </summary>
    Vector3 RotationPitchYawRollDegrees { get; set; }

    /// <summary>
    /// This object's rotation in world space as a quaternion.
    /// </summary>
    Quaternion RotationQuaternion { get; set; }

    /// <summary>
    /// This object's scale in world space.
    /// </summary>
    Vector3 Scale { get; set; }

    /// <summary>
    /// Notifies this object definition editor that it was added to the Stage being edited.
    /// </summary>
    void AddedToStage();

    /// <summary>
    /// Notifies this object definition editor that it was removed from the Stage being edited.
    /// </summary>
    void RemovedFromStage();
}

internal abstract class IObjectDefinitionEditor<TDefinition> : DefinitionEditorBase, IObjectDefinitionEditor
    where TDefinition : ObjectDefinition
{
    protected ISelectionManager SelectionManager { get; }
    protected IOverlayService OverlayService { get; }
    protected ILiveObjectService LiveObjectService { get; }

    protected TDefinition Definition { get; }
    public string Key { get; }
    public StageDefinitionEditor Stage { get; }
    public OutlinerNode OutlinerNode { get; }
    public ILiveObject? PreviewLiveObject { get; protected set; }

    public override string DisplayName => Definition.DisplayName;

    public Vector3 Position
    {
        get => Definition.Position;
        set => SetPropertyValue(value => Definition.Position = value, value);
    }

    public Vector3 RotationPitchYawRollDegrees
    {
        get => Definition.RotationPitchYawRollDegrees;
        set => SetPropertyValue(value => Definition.RotationPitchYawRollDegrees = value, value);
    }

    public Quaternion RotationQuaternion
    {
        get => Definition.RotationQuaternion;
        set => SetPropertyValue(value => Definition.RotationQuaternion = value, value);
    }

    public Vector3 Scale
    {
        get => Definition.Scale;
        set => SetPropertyValue(value => Definition.Scale = value, value);
    }

    protected IObjectDefinitionEditor(IServiceProvider serviceProvider, TDefinition definition, string key, StageDefinitionEditor stage) : base(serviceProvider)
    {
        SelectionManager = serviceProvider.GetRequiredService<ISelectionManager>();
        OverlayService = serviceProvider.GetRequiredService<IOverlayService>();
        LiveObjectService = serviceProvider.GetRequiredService<ILiveObjectService>();

        Definition = definition;
        Key = key;
        Stage = stage;

        OutlinerNode = new OutlinerNode(definition.DisplayName, TypeInfo.Icon, TypeInfo.DisplayName, TypeInfo.Description);
        OutlinerNode.Clicked += OnOutlinerNodeClicked;
        OutlinerNode.ContextMenuItems = GenerateContextMenuItems();
    }

    private void OnOutlinerNodeClicked(OutlinerNode obj)
    {
        SelectionManager.SelectedEditor = this;
    }

    public void SetDisplayName(string displayName)
    {
        Definition.DisplayName = displayName;
        OutlinerNode.DisplayName = displayName;
    }

    public override void DrawProperties()
    {
        string displayName = DisplayName;
        if (ImGui.InputText("Display Name", ref displayName, 512, flags: ImGuiInputTextFlags.EnterReturnsTrue))
        {
            SetDisplayName(displayName);
        }

        ImGuiHelpers.ScaledDummy(4.0f);

        Vector3 position = Position;
        if (ImGui.DragFloat3("Position", ref position, vSpeed: 0.01f))
        {
            Position = position;
        }

        Vector3 rotation = RotationPitchYawRollDegrees;
        if (ImGui.DragFloat3("Rotation", ref rotation, vSpeed: 0.5f))
        {
            RotationPitchYawRollDegrees = rotation;
        }

        Vector3 scale = Scale;
        if (ImGui.DragFloat3("Scale", ref scale, vSpeed: 0.1f))
        {
            Scale = scale;
        }

        ImGuiHelpers.ScaledDummy(4.0f);
    }

    protected virtual IEnumerable<OutlinerContextMenuItem> GenerateContextMenuItems()
    {
        yield return new OutlinerContextMenuItem("Duplicate", "Creates a copy of this object.", _ =>
        {
            var clonedDefinition = Definition.Clone();
            var newEditor = Stage.AddObject(clonedDefinition);
            SelectionManager.SelectedEditor = newEditor;
        });
        yield return new OutlinerContextMenuItem("Delete", $"Removes this {TypeInfo.DisplayName} from the stage.", _ =>
        {
            Stage.RemoveObject(this);
        });
    }

    public override void Selected()
    {
        base.Selected();

        OutlinerNode.IsSelected = true;
        OverlayService.DrawOverlays += OnDrawSelectedOverlays;
    }

    protected override void SetPropertyValue<TValue>(Action<TValue> setter, TValue value, [CallerMemberName] string? propertyName = null)
    {
        base.SetPropertyValue(setter, value, propertyName);
        RefreshPreviewObject();
    }

    private void OnDrawSelectedOverlays(IOverlayDrawContext obj)
    {
        var pyrRadians = RotationPitchYawRollDegrees * MathF.PI / 180.0f;
        var transform = Matrix4x4.CreateScale(Scale * 0.1f) * Matrix4x4.CreateFromYawPitchRoll(pyrRadians.Y, pyrRadians.X, pyrRadians.Z) * Matrix4x4.CreateTranslation(Position);
        obj.DrawCross(transform, 1.0f, new Vector4(0.9f, 0.9f, 0.9f, 0.5f));
    
        if (PreviewLiveObject != null)
        {
            if (PreviewLiveObject.TryGetOrientedBounds(out var bounds))
            {
                obj.DrawBox(bounds.Transform, bounds.HalfExtents, 1.0f, new Vector4(0.9f, 0.9f, 0.9f, 0.5f));
            }
        }
    }

    public override void Deselected()
    {
        base.Deselected();

        OverlayService.DrawOverlays -= OnDrawSelectedOverlays;
        OutlinerNode.IsSelected = false;
    }

    public virtual void AddedToStage()
    {
        PreviewLiveObject = LiveObjectService.CreateObject(Definition);
    }

    public virtual void RefreshPreviewObject()
    {
        PreviewLiveObject = PreviewLiveObject != null ? LiveObjectService.UpdateOrRecreateObject(PreviewLiveObject, Definition) : LiveObjectService.CreateObject(Definition);
    }

    public virtual void RemovedFromStage()
    {
        if (SelectionManager.SelectedEditor == this)
        {
            SelectionManager.SelectedEditor = null;
        }
        PreviewLiveObject?.Dispose();
        PreviewLiveObject = null;
    }
}
