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
using Stagehand.Live;
using Stagehand.Services;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors;

public class StageDefinitionEditor : DefinitionEditorBase
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Stage", "A collection of objects to create in the game.", FontAwesomeIcon.FileImage);

    private readonly IDataManager _dataManager;
    private readonly ISelectionManager _selectionManager;
    private readonly IClientState _clientState;
    private readonly IStagehandKeybinds _stagehandKeybinds;

    private StageDefinition Definition { get; }

    public override string DisplayName => Name;
    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    public OutlinerNode OutlinerNode { get; }
    public DefinitionEditorDictionary<ObjectDefinition, IObjectDefinitionEditor> Objects { get; }
    public DefinitionEditorDictionary<EmbeddedModpackDefinition, EmbeddedModpackDefinitionEditor> EmbeddedModpacks { get; }
    public IReadOnlyDictionary<string, ILiveModpack> PreviewModpacks => new Dictionary<string, ILiveModpack>(EmbeddedModpacks.Select(pair => new KeyValuePair<string, ILiveModpack>(pair.Key, pair.Value.PreviewLiveModpack!)));
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
        _stagehandKeybinds = serviceProvider.GetRequiredService<IStagehandKeybinds>();

        OutlinerNode = new OutlinerNode(DisplayName, Guid.NewGuid().ToString(), TypeInfo.Icon, TypeInfo.DisplayName, TypeInfo.Description);
        OutlinerNode.Clicked += OnOutlinerNodeClicked;
        OutlinerNode.ContextMenuItems = GenerateContextMenuItems();

        // NOTE: We need to load the modpacks before the object because the objects need to be able to find the modpacks when creating their live preview objects.
        // Maybe not the most theoretically elegant, but does the job.
        EmbeddedModpacks = new(definition.EmbeddedModpacks, OutlinerNode, CreateEditorForEmbeddedModpackDefinition, TransactionManager, _selectionManager);
        Objects = new ObjectDefinitionEditorDictionary(null, definition.Objects, OutlinerNode, CreateEditorForObjectDefinition, TransactionManager, _selectionManager);

        _stagehandKeybinds.EditorCutObject.Pressed += CutSelectedDefinitions;
        _stagehandKeybinds.EditorCopyObject.Pressed += CopySelectedDefinitions;
        _stagehandKeybinds.EditorPasteObject.Pressed += PasteDefinitions;

        _stagehandKeybinds.EditorDeleteObject.Pressed += DeleteSelectedDefinitions;
        _stagehandKeybinds.EditorDuplicateObject.Pressed += DuplicateSelectedDefinitions;
        _stagehandKeybinds.EditorHideObject.Pressed += HideSelectedObjects;
        _stagehandKeybinds.EditorUnhideObject.Pressed += UnhideSelectedObjects;
    }

    /// <summary>
    /// Gets the containing dictionary to add newly created objects to.
    /// </summary>
    public DefinitionEditorDictionary<ObjectDefinition, IObjectDefinitionEditor> GetNewObjectContainer()
    {
        var selectedObjectEditor = _selectionManager.PrimarySelectedEditor as IObjectDefinitionEditor;
        if (selectedObjectEditor != null)
        {
            if (selectedObjectEditor.ChildObjects != null)
            {
                return selectedObjectEditor.ChildObjects;
            }
            else if (selectedObjectEditor.OwnerDictionary != null)
            {
                return selectedObjectEditor.OwnerDictionary;
            }
        }

        return Objects;
    }

    private IEnumerable<OutlinerContextMenuItem> GenerateContextMenuItems()
    {
        yield return new KeybindOutlinerContextMenuItem(_stagehandKeybinds.EditorPasteObject, _ => PasteDefinitions());
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

    private void OnOutlinerNodeClicked(OutlinerNode obj, ImGuiMouseButton mouseButton)
    {
        if (ImGui.IsKeyDown(ImGuiKey.ModCtrl))
        {
            if (_selectionManager.SelectedEditors.Contains(this))
            {
                _selectionManager.TryRemoveSelectedEditor(this);
            }
            else
            {
                _selectionManager.TryAddSelectedEditor(this);
            }
        }
        else if (mouseButton == ImGuiMouseButton.Left || !_selectionManager.SelectedEditors.Contains(this))
        {
            _selectionManager.SelectedEditors = [this];
        }
    }

    public void PasteDefinitions()
    {
        if (DataTransferFragment.FromDataString(ImGui.GetClipboardText()) is StageDefinitionDataTransferFragment objectDefinitionFragment
            && (objectDefinitionFragment.ObjectDefinitions.Length > 0 || objectDefinitionFragment.ModpackDefinitions.Length > 0))
        {
            string transactionName = "Paste ";
            if (objectDefinitionFragment.ObjectDefinitions.Length == 1 && objectDefinitionFragment.ModpackDefinitions.Length == 0)
            {
                transactionName += objectDefinitionFragment.ObjectDefinitions[0].DisplayName;
            }
            else if (objectDefinitionFragment.ObjectDefinitions.Length == 0 && objectDefinitionFragment.ModpackDefinitions.Length == 1)
            {
                transactionName += objectDefinitionFragment.ModpackDefinitions[0].DisplayName;
            }
            else
            {
                transactionName += $"{objectDefinitionFragment.ObjectDefinitions.Length + objectDefinitionFragment.ModpackDefinitions.Length} Items";
            }
            using (TransactionManager.BeginTransactionGroup(transactionName))
            {
                List<IDefinitionEditor> newEditors = new();
                foreach (var objectDefinition in objectDefinitionFragment.ObjectDefinitions)
                {
                    newEditors.Add(GetNewObjectContainer().Add(objectDefinition, select: false));
                }

                foreach (var modpackDefinition in objectDefinitionFragment.ModpackDefinitions)
                {
                    newEditors.Add(EmbeddedModpacks.Add(modpackDefinition, select: false));
                }
                _selectionManager.SelectedEditors = newEditors;
            }
        }
    }

    public void CutSelectedDefinitions()
    {
        var selectedObjectEditors = _selectionManager.SelectedEditors.OfType<IObjectDefinitionEditor>().WithoutDescendants().OfType<IObjectDefinitionEditor>().ToArray();
        var selectedModpackEditors = _selectionManager.SelectedEditors.OfType<EmbeddedModpackDefinitionEditor>().ToArray();
        if (selectedObjectEditors.Length > 0 || selectedModpackEditors.Length > 0)
        {
            string transactionName = "Cut ";
            if (selectedObjectEditors.Length == 1 && selectedModpackEditors.Length == 0)
            {
                transactionName += selectedObjectEditors[0].DisplayName;
            }
            else if (selectedObjectEditors.Length == 0 && selectedModpackEditors.Length == 1)
            {
                transactionName += selectedModpackEditors[0].DisplayName;
            }
            else
            {
                transactionName += $"{selectedObjectEditors.Length + selectedModpackEditors.Length} Items";
            }
            using (TransactionManager.BeginTransactionGroup(transactionName))
            {
                TransactionManager.DoTransaction(new DelegateTransaction("Copy", () => CopyDefinitions(selectedObjectEditors, selectedModpackEditors), () => { }, affectsDataModel: false));
                foreach (var selectedEditor in selectedObjectEditors)
                {
                    selectedEditor.Delete();
                }
            }
        }
    }

    public void CopyDefinitions(IEnumerable<IObjectDefinitionEditor> objectEditors, IEnumerable<EmbeddedModpackDefinitionEditor> modpackEditors)
    {
        var fragment = new StageDefinitionDataTransferFragment(
            objectEditors.Select(objectEditor => objectEditor.CreateDefinitionCopy()).ToArray(),
            modpackEditors.Select(modpackEditor => modpackEditor.CreateDefinitionCopy()).ToArray());
        ImGui.SetClipboardText(fragment.ToDataString());
    }

    public void CopySelectedDefinitions()
    {
        var selectedObjectEditors = _selectionManager.SelectedEditors.OfType<IObjectDefinitionEditor>().WithoutDescendants().OfType<IObjectDefinitionEditor>();
        var selectedModpackEditors = _selectionManager.SelectedEditors.OfType<EmbeddedModpackDefinitionEditor>();
        CopyDefinitions(selectedObjectEditors, selectedModpackEditors);
    }

    public void DuplicateSelectedDefinitions()
    {
        var selectedEditors = _selectionManager.SelectedEditors.OfType<IChildDefinitionEditor>().WithoutDescendants().ToArray();
        if (selectedEditors.Length > 0)
        {
            using (TransactionManager.BeginTransactionGroup($"Duplicate {(selectedEditors.Length == 1 ? selectedEditors[0].DisplayName : $"{selectedEditors.Length} Items")}"))
            {
                foreach (var selectedEditor in selectedEditors)
                {
                    selectedEditor.Duplicate();
                }
            }
        }
    }

    public void DeleteSelectedDefinitions()
    {
        var selectedEditors = _selectionManager.SelectedEditors.OfType<IChildDefinitionEditor>().WithoutDescendants().ToArray();
        if (selectedEditors.Length > 0)
        {
            using (TransactionManager.BeginTransactionGroup($"Delete {(selectedEditors.Length == 1 ? selectedEditors[0].DisplayName : $"{selectedEditors.Length} Items")}"))
            {
                foreach (var selectedEditor in selectedEditors)
                {
                    selectedEditor.Delete();
                }
            }
        }
    }

    public void HideSelectedObjects()
    {
        var selectedEditors = _selectionManager.SelectedEditors.OfType<IObjectDefinitionEditor>();
        if (selectedEditors.Count() > 0)
        {
            using (TransactionManager.BeginTransactionGroup($"Hide {(selectedEditors.Count() == 1 ? selectedEditors.First().DisplayName : $"{selectedEditors.Count()} Items")}"))
            {
                foreach (var objectEditor in selectedEditors)
                {
                    objectEditor.IsDisabled = true;
                }
            }
        }
    }

    public void UnhideSelectedObjects()
    {
        var selectedEditors = _selectionManager.SelectedEditors.OfType<IObjectDefinitionEditor>();
        if (selectedEditors.Count() > 0)
        {
            using (TransactionManager.BeginTransactionGroup($"Show {(selectedEditors.Count() == 1 ? selectedEditors.First().DisplayName : $"{selectedEditors.Count()} Items")}"))
            {
                foreach (var objectEditor in selectedEditors)
                {
                    objectEditor.IsDisabled = false;
                }
            }
        }
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

        _stagehandKeybinds.EditorCutObject.Pressed -= CutSelectedDefinitions;
        _stagehandKeybinds.EditorCopyObject.Pressed -= CopySelectedDefinitions;
        _stagehandKeybinds.EditorPasteObject.Pressed -= PasteDefinitions;

        _stagehandKeybinds.EditorDeleteObject.Pressed -= DeleteSelectedDefinitions;
        _stagehandKeybinds.EditorDuplicateObject.Pressed -= DuplicateSelectedDefinitions;
        _stagehandKeybinds.EditorHideObject.Pressed -= HideSelectedObjects;
        _stagehandKeybinds.EditorUnhideObject.Pressed -= UnhideSelectedObjects;

        EmbeddedModpacks.Dispose();
        Objects.Dispose();

        base.Dispose();
    }

    private EmbeddedModpackDefinitionEditor CreateEditorForEmbeddedModpackDefinition(EmbeddedModpackDefinition definition, string key)
    {
        return new EmbeddedModpackDefinitionEditor(ServiceProvider, definition, this, key);
    }

    public IObjectDefinitionEditor CreateEditorForObjectDefinition(ObjectDefinition objectDefinition, string objectKey)
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

        public static IObjectDefinitionEditor VisitGroupDefinition(GroupDefinition definition, ref ObjectDefinitionEditorFactoryParams param)
        {
            return new GroupDefinitionEditor(param.ServiceProvider, definition, param.Key, param.Stage);
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
