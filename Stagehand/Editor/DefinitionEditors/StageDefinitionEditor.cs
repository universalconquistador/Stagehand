using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors;

public class StageDefinitionEditor : DefinitionEditorBase
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Stage", "A collection of objects to create in the game.", FontAwesomeIcon.FileImage);

    private readonly IDataManager _dataManager;
    private readonly ISelectionManager _selectionManager;
    private readonly IClientState _clientState;

    private StageDefinition Definition { get; }

    public override string DisplayName => Name;
    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    public OutlinerNode OutlinerNode { get; }
    public DefinitionEditorDictionary<ObjectDefinition, IObjectDefinitionEditor> Objects { get; }
    public DefinitionEditorDictionary<EmbeddedModpackDefinition, EmbeddedModpackDefinitionEditor> EmbeddedModpacks { get; }
    public bool IsDisposing { get; private set; } = false;

    public string Name
    {
        get => Definition.Info.Name;
        set => SetPropertyValue(value => Definition.Info = Definition.Info with { Name = value }, value, Definition.Info.Name);
    }

    public string AuthorName
    {
        get => Definition.Info.AuthorName;
        set => SetPropertyValue(value => Definition.Info = Definition.Info with { AuthorName = value }, value, Definition.Info.AuthorName);
    }

    public string Version
    {
        get => Definition.Info.VersionString;
        set => SetPropertyValue(value => Definition.Info = Definition.Info with { VersionString = value }, value, Definition.Info.VersionString);
    }

    public string Description
    {
        get => Definition.Info.Description;
        set => SetPropertyValue(value => Definition.Info = Definition.Info with { Description = value }, value, Definition.Info.Description);
    }

    public int IntendedTerritoryType
    {
        get => Definition.Info.IntendedTerritoryType;
        set => SetPropertyValue(value => Definition.Info = Definition.Info with { IntendedTerritoryType = value }, value, Definition.Info.IntendedTerritoryType);
    }

    private Vector3 _editTranslation = Vector3.Zero;
    public Vector3 EditTranslation
    {
        get => _editTranslation;
        set => SetPropertyValue(SetEditTranslationInternal, value, _editTranslation, affectsDataModel: false);
    }

    private Quaternion _editRotation = Quaternion.Identity;
    public Quaternion EditRotation
    {
        get => _editRotation;
        set => SetPropertyValue(SetEditRotationInternal, value, _editRotation, affectsDataModel: false);
    }

    private float _editUniformScale = 1.0f;
    public float EditUniformScale
    {
        get => _editUniformScale;
        set => SetPropertyValue(SetEditUniformScaleInternal, value, _editUniformScale, affectsDataModel: false);
    }

    public StageDefinitionEditor(IServiceProvider serviceProvider, StageDefinition definition)
        : base(serviceProvider)
    {
        Definition = definition;
        _dataManager = serviceProvider.GetRequiredService<IDataManager>();
        _selectionManager = serviceProvider.GetRequiredService<ISelectionManager>();
        _clientState = serviceProvider.GetRequiredService<IClientState>();

        OutlinerNode = new OutlinerNode(DisplayName, Guid.NewGuid().ToString(), TypeInfo.Icon, TypeInfo.DisplayName, TypeInfo.Description);
        OutlinerNode.Clicked += OnOutlinerNodeClicked;

        // NOTE: We need to load the modpacks before the object because the objects need to be able to find the modpacks when creating their live preview objects.
        // Maybe not the most theoretically elegant, but does the job.
        EmbeddedModpacks = new(definition.EmbeddedModpacks, OutlinerNode, CreateEditorForEmbeddedModpackDefinition, TransactionManager, _selectionManager);
        Objects = new(definition.Objects, OutlinerNode, CreateEditorForObjectDefinition, TransactionManager, _selectionManager);
    }

    protected virtual void SetEditTranslationInternal(Vector3 editTranslation)
    {
        _editTranslation = editTranslation;
        foreach (var objectEditor in Objects.Values)
        {
            objectEditor.SetParentTransform(EditTranslation, EditRotation, EditUniformScale);
        }
    }

    protected virtual void SetEditRotationInternal(Quaternion editRotation)
    {
        _editRotation = editRotation;
        foreach (var objectEditor in Objects.Values)
        {
            objectEditor.SetParentTransform(EditTranslation, EditRotation, EditUniformScale);
        }
    }

    protected virtual void SetEditUniformScaleInternal(float editUniformScale)
    {
        _editUniformScale = editUniformScale;
        foreach (var objectEditor in Objects.Values)
        {
            objectEditor.SetParentTransform(EditTranslation, EditRotation, EditUniformScale);
        }
    }

    private void OnOutlinerNodeClicked(OutlinerNode obj)
    {
        _selectionManager.SelectedEditor = this;
    }

    protected override void OnDrawProperties()
    {
        string name = Name;
        if (ImGui.InputText("Name", ref name, 512, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            Name = name;
        }

        string version = Version;
        if (ImGui.InputText("Version", ref version, 512, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            Version = version;
        }

        string authorName = AuthorName;
        if (ImGui.InputText("Author Name", ref authorName, 512, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            AuthorName = authorName;
        }

        string description = Description;
        if (ImGui.InputTextMultiline("Description", ref description, 4096, flags: ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CtrlEnterForNewLine))
        {
            Description = description;
        }

        int intendedTerritory = IntendedTerritoryType;
        using (var combo = ImRaii.Combo("Intended Location", (intendedTerritory >= 0 && _dataManager.GetExcelSheet<TerritoryType>().HasRow((uint)intendedTerritory)) ? _dataManager.GetExcelSheet<TerritoryType>().GetRow((uint)intendedTerritory).PlaceName.ValueNullable?.Name.ToString() : "(Unspecified)"))
        {
            if (combo.Success)
            {
                foreach (var row in _dataManager.GetExcelSheet<TerritoryType>())
                {
                    if (row.PlaceName.ValueNullable?.Name.ToString() is string placeName && placeName.Length > 0 && ImGui.Selectable($"{placeName}###Place{row.RowId}", row.RowId == intendedTerritory))
                    {
                        IntendedTerritoryType = (int)row.RowId;
                    }
                }
            }
        }
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight());
        if (ImGuiComponents.IconButton("###UseCurrentLocation", FontAwesomeIcon.LocationCrosshairs, new Vector2(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
        {
            IntendedTerritoryType = (int)_clientState.TerritoryType;
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted("Use current location");
            }
        }
        ImGui.Separator();
        Vector3 position = EditTranslation;
        if (ImGui.DragFloat3("Translation", ref position, vSpeed: 0.01f))
        {
            EditTranslation = position;
        }

        // The formula I'm using for quat -> PYR is z-up, so swizzle the dimensions around
        float x = EditRotation.Z;
        float y = EditRotation.X;
        float z = EditRotation.Y;
        float w = EditRotation.W;

        var roll = MathF.Atan2((2 * w * x) + (2 * y * z), 1 - (2 * x * x) - (2 * y * y));
        var pitch = MathF.Asin((2 * w * y) - (2 * z * x));
        var yaw = MathF.Atan2((2 * w * z) + (2 * x * y), 1 - (2 * y * y) - (2 * z * z));

        Vector3 rotation = new Vector3(RadiansToDegrees(pitch), RadiansToDegrees(yaw), RadiansToDegrees(roll));
        if (ImGui.DragFloat3("Rotation", ref rotation, vSpeed: 0.5f))
        {
            EditRotation = Quaternion.CreateFromYawPitchRoll(DegreesToRadians(rotation.Y), DegreesToRadians(rotation.X), DegreesToRadians(rotation.Z)); ;
        }

        float scale = EditUniformScale;
        if (ImGui.DragFloat("Scale", ref scale, vSpeed: 0.1f, vMin: 0.01f, vMax: 10.0f))
        {
            EditUniformScale = scale;
        }
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * MathF.PI / 180.0f;
    }

    private static float RadiansToDegrees(float radians)
    {
        return radians * 180.0f / MathF.PI;
    }

    public override void Selected()
    {
        OutlinerNode.IsSelected = true;
    }

    public override void Deselected()
    {
        OutlinerNode.IsSelected = false;
    }

    public override void Dispose()
    {
        IsDisposing = true;
        EmbeddedModpacks.Dispose();
        Objects.Dispose();

        base.Dispose();
    }

    private EmbeddedModpackDefinitionEditor CreateEditorForEmbeddedModpackDefinition(EmbeddedModpackDefinition definition, string key)
    {
        return new EmbeddedModpackDefinitionEditor(ServiceProvider, definition, this, key);
    }

    private IObjectDefinitionEditor CreateEditorForObjectDefinition(ObjectDefinition objectDefinition, string objectKey)
    {
        var factoryParams = new ObjectDefinitionEditorFactoryParams()
        {
            ServiceProvider = ServiceProvider,
            Key = objectKey,
            Stage = this,
        };
        var result = objectDefinition.Visit<ObjectDefinitionEditorFactory, ObjectDefinitionEditorFactoryParams, IObjectDefinitionEditor>(ref factoryParams);
        result.SetParentTransform(EditTranslation, EditRotation, EditUniformScale);
        return result;
    }

    private record struct ObjectDefinitionEditorFactoryParams(IServiceProvider ServiceProvider, string Key, StageDefinitionEditor Stage);

    private class ObjectDefinitionEditorFactory : IObjectVisitor<ObjectDefinitionEditorFactoryParams, IObjectDefinitionEditor>
    {
        public static IObjectDefinitionEditor VisitBgObjectDefinition(BgObjectDefinition definition, ref ObjectDefinitionEditorFactoryParams param)
        {
            return new BgObjectDefinitionEditor(param.ServiceProvider, definition, param.Key, param.Stage);
        }

        public static IObjectDefinitionEditor VisitLightDefinition(LightDefinition definition, ref ObjectDefinitionEditorFactoryParams param)
        {
            return new LightDefinitionEditor(param.ServiceProvider, definition, param.Key, param.Stage);
        }

        public static IObjectDefinitionEditor VisitSoundObjectDefinition(SoundObjectDefinition definition, ref ObjectDefinitionEditorFactoryParams param)
        {
            return new SoundObjectDefinitionEditor(param.ServiceProvider, definition, param.Key, param.Stage);
        }

        public static IObjectDefinitionEditor VisitVfxObjectDefinition(VfxObjectDefinition definition, ref ObjectDefinitionEditorFactoryParams param)
        {
            return new VfxObjectDefinitionEditor(param.ServiceProvider, definition, param.Key, param.Stage);
        }

        public static IObjectDefinitionEditor VisitWeaponDefinition(WeaponDefinition definition, ref ObjectDefinitionEditorFactoryParams param)
        {
            return new WeaponDefinitionEditor(param.ServiceProvider, definition, param.Key, param.Stage);
        }
    }
}
