using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.Services;
using Stagehand.Live;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

/// <summary>
/// A definition editor that edits an object definition.
/// </summary>
public interface IObjectDefinitionEditor : IChildDefinitionEditor
{
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

    string ModpackId { get; set; }

    void RefreshPreviewObject();
}

internal abstract class ObjectDefinitionEditor<TDefinition> : DefinitionEditorBase, IObjectDefinitionEditor
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
    public bool IsInStage { get; private set; }

    public override string DisplayName => Definition.DisplayName;

    public Vector3 Position
    {
        get => Definition.Position;
        set => SetPropertyValue(SetPositionInternal, value, Definition.Position);
    }

    public Vector3 RotationPitchYawRollDegrees
    {
        get => Definition.RotationPitchYawRollDegrees;
        set => SetPropertyValue(SetRotationPitchYawRollDegreesInternal, value, Definition.RotationPitchYawRollDegrees);
    }

    public Quaternion RotationQuaternion
    {
        get => Definition.RotationQuaternion;
        set => SetPropertyValue(SetRotationQuaternionInternal, value, Definition.RotationQuaternion);
    }

    public Vector3 Scale
    {
        get => Definition.Scale;
        set => SetPropertyValue(SetScaleInternal, value, Definition.Scale);
    }

    public Matrix4x4 TransformNoScale => Matrix4x4.CreateFromQuaternion(RotationQuaternion) * Matrix4x4.CreateTranslation(Position);

    public Matrix4x4 Transform => Matrix4x4.CreateScale(Scale) * TransformNoScale;

    public string ModpackId
    {
        get => Definition.ModpackId;
        set => SetPropertyValue(SetModpackIdInternal, value, Definition.ModpackId);
    }

    protected virtual void SetPositionInternal(Vector3 position)
    {
        Definition.Position = position;
    }

    protected virtual void SetRotationPitchYawRollDegreesInternal(Vector3 rotationPYRDegrees)
    {
        Definition.RotationPitchYawRollDegrees = rotationPYRDegrees;
    }

    protected virtual void SetRotationQuaternionInternal(Quaternion rotationQuaternion)
    {
        Definition.RotationQuaternion = rotationQuaternion;
    }

    protected virtual void SetScaleInternal(Vector3 scale)
    {
        Definition.Scale = scale;
    }

    protected virtual void SetModpackIdInternal(string modpackId)
    {
        Definition.ModpackId = modpackId;
    }

    protected ObjectDefinitionEditor(IServiceProvider serviceProvider, TDefinition definition, string key, StageDefinitionEditor stage) : base(serviceProvider)
    {
        SelectionManager = serviceProvider.GetRequiredService<ISelectionManager>();
        OverlayService = serviceProvider.GetRequiredService<IOverlayService>();
        LiveObjectService = serviceProvider.GetRequiredService<ILiveObjectService>();

        Definition = definition;
        Key = key;
        Stage = stage;

        OutlinerNode = new OutlinerNode(definition.DisplayName, key, TypeInfo.Icon, TypeInfo.DisplayName, TypeInfo.Description);
        OutlinerNode.Clicked += OnOutlinerNodeClicked;
        OutlinerNode.ContextMenuItems = GenerateContextMenuItems();
    }

    private void OnOutlinerNodeClicked(OutlinerNode obj)
    {
        SelectionManager.SelectedEditor = this;
    }

    public void SetDisplayName(string displayName)
    {
        SetPropertyValue(SetDisplayNameInternal, displayName, DisplayName, "Display Name");
    }

    protected virtual void SetDisplayNameInternal(string displayName)
    {
        Definition.DisplayName = displayName;
        OutlinerNode.DisplayName = displayName;
    }

    protected override void OnDrawProperties()
    {
        string displayName = DisplayName;
        if (ImGui.InputText("Display Name", ref displayName, 512, flags: ImGuiInputTextFlags.EnterReturnsTrue))
        {
            SetDisplayName(displayName);
        }
        var modpackPreview = "(None)";
        if (ModpackId != string.Empty)
        {
            if (Stage.EmbeddedModpacks.TryGetValue(ModpackId, out var editor))
            {
                modpackPreview = editor.DisplayName;
            }
            else
            {
                modpackPreview = "(Invalid)";
            }
        }
        using (var modpackCombo = ImRaii.Combo("Modpack", modpackPreview))
        {
            if (modpackCombo)
            {
                if (ImGui.Selectable("(None)", ModpackId == string.Empty))
                {
                    ModpackId = string.Empty;
                }

                foreach (var modpack in Stage.EmbeddedModpacks.OrderBy(pair => pair.Value.DisplayName))
                {
                    if (ImGui.Selectable(modpack.Value.DisplayName, ModpackId == modpack.Key))
                    {
                        ModpackId = modpack.Key;
                    }
                }
            }
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
            Stage.Objects.Add(clonedDefinition);
        });
        yield return new OutlinerContextMenuItem("Delete", $"Removes this {TypeInfo.DisplayName} from the stage.", _ =>
        {
            Stage.Objects.Remove(this);
        });
    }

    public override void Selected()
    {
        base.Selected();

        OutlinerNode.IsSelected = true;
    }

    protected override void SetPropertyValue<TValue>(Action<TValue> setter, TValue newValue, TValue oldValue, [CallerMemberName] string? propertyName = null)
    {
        base.SetPropertyValue(val =>
        {
            setter.Invoke(val);
            RefreshPreviewObject();
        }, newValue, oldValue, propertyName);
    }

    protected virtual void DrawOverlays(IOverlayDrawContext obj)
    {
        if (IsSelected)
        {
            var color = ComputeOverlayColor();
            
            if (PreviewLiveObject != null)
            {
                if (PreviewLiveObject.TryGetOrientedBounds(out var bounds))
                {
                    obj.DrawBox(bounds.Transform, bounds.HalfExtents, 2.0f, color);
                }
            }
        }
    }

    protected virtual Vector4 ComputeOverlayColor()
    {
        return IsSelected ? new Vector4(0.4f, 1.0f, 0.7f, 1.0f) : new Vector4(1.0f, 1.0f, 1.0f, 0.45f);
    }

    public override void Deselected()
    {
        base.Deselected();

        OutlinerNode.IsSelected = false;
    }

    public virtual void AddedToStage()
    {
        PreviewLiveObject = LiveObjectService.CreateObject(Definition, GetPreviewModpack());
        OverlayService.DrawOverlays += DrawOverlays;
        IsInStage = true;
    }

    public virtual void RefreshPreviewObject()
    {
        PreviewLiveObject = PreviewLiveObject != null ? LiveObjectService.UpdateOrRecreateObject(PreviewLiveObject, Definition, GetPreviewModpack()) : LiveObjectService.CreateObject(Definition, GetPreviewModpack());
    }

    private ILiveModpack? GetPreviewModpack()
    {
        if (ModpackId == string.Empty)
        {
            return null;
        }

        if (Stage.EmbeddedModpacks?.TryGetValue(ModpackId, out var modpackEditor) ?? false)
        {
            return modpackEditor.PreviewLiveModpack;
        }
        else
        {
            return null;
        }
    }

    public virtual void RemovedFromStage()
    {
        IsInStage = false;
        OverlayService.DrawOverlays -= DrawOverlays;
        PreviewLiveObject?.Dispose();
        PreviewLiveObject = null;
    }
}
