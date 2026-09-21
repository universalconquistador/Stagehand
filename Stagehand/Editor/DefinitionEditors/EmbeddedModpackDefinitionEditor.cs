using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions;
using Stagehand.Definitions.ModResources;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.DefinitionEditors.Objects;
using Stagehand.Editor.Services;
using Stagehand.Live;
using Stagehand.Services;
using Stagehand.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniCon.PenumbraMeta;
using UniCon.PenumbraMeta.Groups;

namespace Stagehand.Editor.DefinitionEditors;

public class EmbeddedModpackDefinitionEditor : DefinitionEditorBase, IChildDefinitionEditor<EmbeddedModpackDefinition, EmbeddedModpackDefinitionEditor>
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Embedded Modpack", "A collection of game files to modify.", FontAwesomeIcon.Archive);

    private enum NewModResourceType
    {
        DiskResource,
        EmbeddedResource,
        GameResource,
    }

    private readonly ILogger _logger;
    private readonly IObjectTable _objectTable;
    private readonly ISelectionManager _selectionManager;
    private readonly IResourceRedirectionService _resourceRedirectionService;
    private readonly IPenumbraInteropService _penumbraInteropService;
    private readonly FileDialogManager _fileDialogManager;

    private string? _loadedPenumbraModFolder = null;
    private PenumbraModMetaV4? _loadedPenumbraModMeta = null;
    private ConcurrentDictionary<string, List<string>> _penumbraModSelectedOptions = new();
    
    private EmbeddedModpackDefinition Definition { get; }

    public override string DisplayName => Definition.DisplayName;
    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;
    
    public OutlinerNode OutlinerNode { get; }

    public ILiveModpack? PreviewLiveModpack { get; private set; }
    public bool IsInStage { get; private set; } = false;

    public StageDefinitionEditor Stage { get; }
    public string Key { get; }

    public string PenumbraSourceModDirectory
    {
        get => Definition.PenumbraSourceModDirectory;
        set => SetPropertyValue(value => Definition.PenumbraSourceModDirectory = value, value, Definition.PenumbraSourceModDirectory);
    }

    public string PenumbraSourceModVersion
    {
        get => Definition.PenumbraSourceModVersion;
        set => SetPropertyValue(value => Definition.PenumbraSourceModVersion = value, value, Definition.PenumbraSourceModVersion);
    }

    public DefinitionEditorDictionary<EmbeddedModpackDefinition, EmbeddedModpackDefinitionEditor>? OwnerDictionary { get; set; }

    public EmbeddedModpackDefinitionEditor(IServiceProvider serviceProvider, EmbeddedModpackDefinition definition, StageDefinitionEditor stage, string key)
        : base(serviceProvider)
    {
        Definition = definition;
        Stage = stage;
        Key = key;
        _logger = serviceProvider.GetRequiredService<ILogger<EmbeddedModpackDefinitionEditor>>();
        _objectTable = serviceProvider.GetRequiredService<IObjectTable>();
        _selectionManager = serviceProvider.GetRequiredService<ISelectionManager>();
        _resourceRedirectionService = serviceProvider.GetRequiredService<IResourceRedirectionService>();
        _penumbraInteropService = serviceProvider.GetRequiredService<IPenumbraInteropService>();
        _fileDialogManager = serviceProvider.GetRequiredService<FileDialogManager>();

        OutlinerNode = new OutlinerNode(DisplayName, Guid.NewGuid().ToString(), TypeInfo.Icon, TypeInfo.DisplayName, TypeInfo.Description);
        OutlinerNode.SortOrder = -1;
        OutlinerNode.Clicked += OnOutlinerNodeClicked;
        OutlinerNode.ContextMenuItems = GenerateContextMenuItems();
    }

    private ILiveModpack CreatePreviewLiveModpack()
    {
        return _resourceRedirectionService.CreateModpack($"Editor-{Stage.Name}-{DisplayName}", Definition.ModdedResources);
    }

    public void RefreshPreviewLiveModpack()
    {
        var currentHash = ResourceRedirectionHelpers.HashModpackEffects(Definition.ModdedResources);

        var old = PreviewLiveModpack;

        if (old == null || old.EffectsHash != currentHash)
        {
            PreviewLiveModpack = CreatePreviewLiveModpack();
            old?.Dispose();

            RefreshDependentPreviewObjects();
        }
    }

    private void OnOutlinerNodeClicked(OutlinerNode obj)
    {
        _selectionManager.SelectedEditor = this;
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

    private IEnumerable<OutlinerContextMenuItem> GenerateContextMenuItems()
    {
        yield return new OutlinerContextMenuItem("Delete", $"Removes this {TypeInfo.DisplayName} from the stage.", _ =>
        {
            Delete();
        });
    }

    public void Delete()
    {
        using (var transactionGroup = TransactionManager.BeginTransactionGroup($"Delete {DisplayName}"))
        {
            // Clear out any object definition references to this modpack
            foreach (var objectEditor in Stage.Objects.GetValuesAndDescendants())
            {
                if (objectEditor.ModpackId == Key)
                {
                    objectEditor.ModpackId = string.Empty;
                }
            }

            Stage.EmbeddedModpacks.Remove(this);
        }
    }

    private int _importingPenumbraMod = 0;
    private string _modFilterText = "";
    private string _filterText = "";
    private string _newModResourceGamePath = "";
    private string _newModResourceDiskFilePath = "";
    private string _newModResourceRedirectionPath = "";
    private NewModResourceType _newModResourceType = NewModResourceType.EmbeddedResource;
    private bool _isAddingEmbed = false;
    private int _goToTab = -1;
    protected override void OnDrawProperties()
    {
        string displayName = DisplayName;
        if (ImGui.InputText("Name", ref displayName, 512, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            SetDisplayName(displayName);
        }

        if (PenumbraSourceModDirectory != "")
        {
            ImGui.LabelText("Source Penumbra Mod", $"{PenumbraSourceModDirectory}{(PenumbraSourceModVersion != string.Empty ? $" ver. {PenumbraSourceModVersion}" : "")}");

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight() * 2 - ImGui.GetStyle().ItemInnerSpacing.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                _goToTab = 1;
                RefreshPenumbraOptionsAsync(PenumbraSourceModDirectory);
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted("Go to source Penumbra mod");
                    ImGui.Separator();
                    ImGui.TextDisabled("Selects the Penumbra mod that was last imported in the Penumbra Import tab.");
                }
            }
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                using (TransactionManager.BeginTransactionGroup($"Unlink {DisplayName} from Penumbra mod"))
                {
                    PenumbraSourceModDirectory = string.Empty;
                    PenumbraSourceModVersion = string.Empty;
                }
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted("Unlink from Penumbra Mod");
                    ImGui.Separator();
                    ImGui.TextDisabled($"Removes the association with Penumbra mod {PenumbraSourceModDirectory}.");
                }
            }
        }

        var goToTab = Interlocked.Exchange(ref _goToTab, -1);

        using (var tabStrip = ImRaii.TabBar("###ModpackTabs"u8))
        {
            if (tabStrip.Success)
            {
                using (var resourcesTab = ImRaii.TabItem("Resources"u8, goToTab == 0 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                {
                    if (resourcesTab.Success)
                    {
                        DrawResourcesTab();
                    }
                }

                using (var penumbraImportTab = ImRaii.TabItem("Penumbra Import"u8, goToTab == 1 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                {
                    if (penumbraImportTab.Success)
                    {
                        DrawPenumbraImportTab();
                    }
                }
            }
        }
    }

    private void DrawResourcesTab()
    {
        Utils.ImGuiExtensions.FilterBox("Filter", ref _filterText);
        using (var table = ImRaii.Table("###Replacements", 4, ImGuiTableFlags.PadOuterX | ImGuiTableFlags.ScrollY, ImGui.GetContentRegionAvail()))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("###CreateButton", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
                ImGui.TableSetupColumn("Game Path", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                ImGui.TableSetupColumn("Contents", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                ImGui.TableSetupColumn("###Commands", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2.0f + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.GetStyle().CellPadding.X * 2.0f);

                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                var filterParams = new ModResourceFilterParams(_filterText);
                foreach (var entry in Definition.ModdedResources.OrderBy(pair => pair.Key, PathSorter.CurrentCultureIgnoreCase))
                {
                    if (_filterText.Length > 0 && (!entry.Key.Contains(_filterText) && !entry.Value.Visit<ModResourceFilterer, ModResourceFilterParams, bool>(ref filterParams)))
                    {
                        continue;
                    }

                    using (ImRaii.PushId(entry.Key))
                    {
                        ImGui.TableNextColumn();
                        bool isModelResource = entry.Key.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase);
                        bool isVfxResource = entry.Key.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase);
                        bool isScdResource = entry.Key.EndsWith(".scd", StringComparison.OrdinalIgnoreCase);
                        if (isModelResource || isVfxResource || isScdResource)
                        {
                            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new Vector2(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                            {
                                var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _objectTable.LocalPlayer?.Rotation ?? 0.0f);
                                ObjectDefinition? newDefinition = null;
                                if (isModelResource)
                                {
                                    newDefinition = new BgObjectDefinition() { ModelGamePath = entry.Key };
                                }
                                else if (isVfxResource)
                                {
                                    newDefinition = new VfxObjectDefinition() { VfxGamePath = entry.Key };
                                }
                                else if (isScdResource)
                                {
                                    newDefinition = new SoundObjectDefinition() { SoundGamePath = entry.Key };
                                }

                                if (newDefinition != null)
                                {
                                    newDefinition.DisplayName = Path.GetFileNameWithoutExtension(entry.Key);
                                    newDefinition.ModpackId = Key;
                                    newDefinition.Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.Transform(Vector3.UnitZ, rotation) * 2.0f;
                                    newDefinition.RotationQuaternion = rotation;
                                    Stage.GetNewObjectContainer().Add(newDefinition);
                                }
                            }
                            if (ImGui.IsItemHovered())
                            {
                                using (ImRaii.Tooltip())
                                {
                                    ImGui.TextUnformatted("Add to Stage");
                                }
                            }
                        }

                        // Game path
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(entry.Key);
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        {
                            ImGui.SetClipboardText(entry.Key);
                        }
                        if (ImGui.IsItemHovered())
                        {
                            using (ImRaii.Tooltip())
                            {
                                ImGui.TextUnformatted(entry.Key);
                                ImGui.Separator();
                                ImGui.TextDisabled("Click to copy");
                            }
                        }

                        ModRowDrawerParams param = new()
                        {
                            Editor = this,
                            FileDialogManager = _fileDialogManager,
                            GamePath = entry.Key,
                        };
                        entry.Value.Visit<ModRowDrawer, ModRowDrawerParams, object?>(ref param);
                    }
                }

                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Add resource:");
                ImGui.TableNextColumn();
                float resourceTypeButtonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemInnerSpacing.X * 2.0f) / 3.0f;
                using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], _newModResourceType == NewModResourceType.GameResource))
                {
                    if (ImGui.Button("Redirect", size: new Vector2(resourceTypeButtonWidth, 0.0f)))
                    {
                        _newModResourceType = NewModResourceType.GameResource;
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted("The new resource will redirect to a vanilla game resource.");
                    }
                }
                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                using (ImRaii.Disabled()) // TEMP: Until disk mod support is complete
                using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], _newModResourceType == NewModResourceType.DiskResource))
                {
                    if (ImGui.Button("File", size: new Vector2(resourceTypeButtonWidth, 0.0f)))
                    {
                        _newModResourceType = NewModResourceType.DiskResource;
                    }
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted("The new resource will point to a file on disk.");
                        ImGui.Separator();
                        ImGui.TextDisabled("If you send this Stage file to someone, the modded resource will not be sent.");
                        // TEMP: Until disk mod support is complete
                        ImGui.TextColored(ImGuiColors.DPSRed, "Not yet implemented.");
                    }
                }
                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], _newModResourceType == NewModResourceType.EmbeddedResource))
                {
                    if (ImGui.Button("Embed", size: new Vector2(resourceTypeButtonWidth, 0.0f)))
                    {
                        _newModResourceType = NewModResourceType.EmbeddedResource;
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted("The new resource will be embedded in this Stage definition.");
                        ImGui.Separator();
                        ImGui.TextDisabled("If you send this Stage file to someone, the modded resource will be sent as part of it.");
                    }
                }

                ImGui.TableNextColumn();

                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1.0f);
                ImGui.InputTextWithHint("###NewModResourceGamePath", "Game path", ref _newModResourceGamePath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

                if (_newModResourceType == NewModResourceType.DiskResource || _newModResourceType == NewModResourceType.EmbeddedResource)
                {
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X);
                    ImGui.InputTextWithHint("###NewModResourceDiskPath", "File path", ref _newModResourceDiskFilePath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
                    ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Folder, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                    {
                        _fileDialogManager.OpenFileDialog($"Select mod file{(_newModResourceGamePath.Length > 0 ? $" for {Path.GetFileName(_newModResourceGamePath)}" : "")}", _newModResourceGamePath.Length > 0 ? Path.GetExtension(_newModResourceGamePath) : ".*", (accepted, path) =>
                        {
                            if (accepted)
                            {
                                _newModResourceDiskFilePath = path;
                            }
                        });
                    }
                }
                else if (_newModResourceType == NewModResourceType.GameResource)
                {
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1.0f);
                    ImGui.InputTextWithHint("###NewRedirectionDestinationPath", "Destination path", ref _newModResourceRedirectionPath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
                }

                ImGui.TableNextColumn();
                if (_newModResourceType == NewModResourceType.DiskResource)
                {
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                    {
                        if (TryAddDiskResource(_newModResourceGamePath, _newModResourceDiskFilePath))
                        {
                            _newModResourceGamePath = "";
                            _newModResourceDiskFilePath = "";
                            _newModResourceRedirectionPath = "";
                        }
                    }
                    if (ImGui.IsItemHovered())
                    {
                        using (ImRaii.Tooltip())
                        {
                            ImGui.TextUnformatted("Add file replacement");
                        }
                    }
                }
                else if (_newModResourceType == NewModResourceType.GameResource)
                {
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                    {
                        if (TryAddGameResource(_newModResourceGamePath, _newModResourceRedirectionPath))
                        {
                            _newModResourceGamePath = "";
                            _newModResourceDiskFilePath = "";
                            _newModResourceRedirectionPath = "";
                        }
                    }
                    if (ImGui.IsItemHovered())
                    {
                        using (ImRaii.Tooltip())
                        {
                            ImGui.TextUnformatted("Add redirection");
                        }
                    }
                }
                else if (_newModResourceType == NewModResourceType.EmbeddedResource)
                {
                    using (ImRaii.Disabled(_isAddingEmbed))
                    {
                        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                        {
                            if (!_isAddingEmbed)
                            {
                                _isAddingEmbed = true;

                                Func<Task> addFunction = async () =>
                                {
                                    if (await TryAddEmbeddedResourceAsync(_newModResourceGamePath, _newModResourceDiskFilePath))
                                    {
                                        _newModResourceGamePath = "";
                                        _newModResourceDiskFilePath = "";
                                        _newModResourceRedirectionPath = "";
                                    }

                                    _isAddingEmbed = false;
                                };

                                _ = addFunction();
                            }
                        }
                    }
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        using (ImRaii.Tooltip())
                        {
                            ImGui.TextUnformatted("Add embedded replacement");
                        }
                    }
                }
            }
        }
    }

    private void DrawPenumbraImportTab()
    {
        if (_penumbraInteropService.IsAvailable)
        {
            ImGui.SetNextItemWidth(-1.0f);
            using (var combo = ImRaii.Combo("###PenumbraModCombo"u8, _loadedPenumbraModMeta?.Name ?? "<Select Penumbra Mod>"))
            {
                if (combo.Success)
                {
                    Utils.ImGuiExtensions.FilterBox("Filter", ref _modFilterText);
                    var mods = _penumbraInteropService.GetModList();
                    foreach (var modPair in mods)
                    {
                        if (modPair.Key.Contains(_modFilterText, StringComparison.CurrentCultureIgnoreCase) || modPair.Value.Contains(_modFilterText, StringComparison.CurrentCultureIgnoreCase))
                        {
                            if (ImGui.Selectable(modPair.Value, modPair.Key == _loadedPenumbraModFolder))
                            {
                                RefreshPenumbraOptionsAsync(modPair.Key);
                            }
                        }
                    }
                }
            }

            if (_loadedPenumbraModFolder != null && _loadedPenumbraModMeta != null)
            {
                ImGui.Spacing();
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled($"Options:");
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight() * 2 - ImGui.GetStyle().ItemInnerSpacing.X);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.SyncAlt, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                {
                    // Reset to Default
                    var newOptions = new ConcurrentDictionary<string, List<string>>();
                    var oldOptions = Interlocked.Exchange(ref _penumbraModSelectedOptions, newOptions);

                    if (_loadedPenumbraModMeta.Groups != null)
                    {
                        foreach (var group in _loadedPenumbraModMeta.Groups)
                        {
                            oldOptions.TryGetValue(group.Name, out var oldChoices);
                            oldChoices?.Clear();
                            newOptions[group.Name] = group.Visit<ResetGroupSelectionVisitor, List<string>?, List<string>>(ref oldChoices);
                        }
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted("Reset to Default Settings");
                        ImGui.Separator();
                        ImGui.TextDisabled("Selects the default settings specified in the mod.");
                    }
                }
                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                var currentCollection = _penumbraInteropService.GetCollection(Penumbra.Api.Enums.ApiCollectionType.Current);
                using (ImRaii.Disabled(currentCollection == null))
                {
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.LevelDownAlt, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
                    {
                        // Use Current Settings
                        if (currentCollection != null)
                        {
                            var currentSettings = _penumbraInteropService.GetCurrentModSettingsWithTemp(currentCollection.Value.Id, _loadedPenumbraModFolder, ignoreInheritance: false, ignoreTemporary: false, key: 0).Item2;
                            if (currentSettings.HasValue)
                            {
                                var newDictionary = new ConcurrentDictionary<string, List<string>>(currentSettings.Value.Item3);
                                Interlocked.Exchange(ref _penumbraModSelectedOptions, newDictionary);
                            }
                        }
                    }
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted($"Use Settings from {currentCollection?.Name ?? "Default Collection"}");
                        ImGui.Separator();
                        ImGui.TextDisabled("Selects the settings in your currently selected collection.");
                    }
                }

                using (var child = ImRaii.Child("###PenumbraModOptions", ImGui.GetContentRegionAvail() - new Vector2(0.0f, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y)))
                {
                    if (child.Success)
                    {
                        bool drewAnyGroups = false;
                        using (ImRaii.PushIndent())
                        {
                            if (_loadedPenumbraModMeta.Groups != null)
                            {
                                foreach (var group in _loadedPenumbraModMeta.Groups)
                                {
                                    var editor = this;
                                    drewAnyGroups |= group.Visit<DrawGroupOptionsVisitor, EmbeddedModpackDefinitionEditor, bool>(ref editor);
                                }
                            }
                        }
                        if (!drewAnyGroups)
                        {
                            var text = "(No options)";
                            var textWidth = ImGui.CalcTextSize(text).X;
                            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X / 2.0f - textWidth / 2.0f);
                            ImGui.TextDisabled(text);
                        }
                    }
                }

                using (ImRaii.Disabled(_importingPenumbraMod > 0))
                {
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FileImport, $"Import {_loadedPenumbraModMeta.Name}{(!string.IsNullOrEmpty(_loadedPenumbraModMeta.Version) ? $" ver. {_loadedPenumbraModMeta.Version}" : "")}"))
                    {
                        _ = ImportPenumbraModAsync(_loadedPenumbraModMeta, _penumbraModSelectedOptions, Path.Combine(_penumbraInteropService.ModDirectory, _loadedPenumbraModFolder));
                    }
                }
            }
        }
        else
        {
            ImGui.Spacing();
            var text = "(Penumbra missing or disabled)";
            var textWidth = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X / 2.0f - textWidth / 2.0f);
            ImGui.TextDisabled(text);
        }
    }

    private async void RefreshPenumbraOptionsAsync(string modFolder)
    {
        if (_penumbraInteropService.IsAvailable)
        {
            var metaV4 = await LoadPenumbraModMetaAsync(modFolder);

            if (metaV4 != null)
            {
                _loadedPenumbraModMeta = metaV4;
                if (_loadedPenumbraModFolder != modFolder)
                {
                    _loadedPenumbraModFolder = modFolder;
                }

                var newOptions = new ConcurrentDictionary<string, List<string>>();
                var oldOptions = Interlocked.Exchange(ref _penumbraModSelectedOptions, newOptions);

                if (metaV4.Groups != null)
                {
                    foreach (var group in metaV4.Groups)
                    {
                        oldOptions.TryGetValue(group.Name, out var oldChoices);
                        newOptions[group.Name] = group.Visit<ResetGroupSelectionVisitor, List<string>?, List<string>>(ref oldChoices);
                    }
                }
            }
        }
    }

    private async Task<PenumbraModMetaV4?> LoadPenumbraModMetaAsync(string modFolder)
    {
        try
        {
            using (var stream = new FileStream(Path.Combine(_penumbraInteropService.ModDirectory, modFolder, "meta.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var meta = await PenumbraModMeta.FromStreamAsync(stream).ConfigureAwait(false);
                if (meta is PenumbraModMetaV4 metaV4)
                {
                    return metaV4;
                }
                else
                {
                    _logger.LogWarning("The mod {mod} could not be parsed or is outdated (meta v3), please make sure you are using Penumbra version 1.7 or greater.", modFolder);
                }
            }
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "The mod {mod} was missing a meta.json file, please ensure it is a valid Penumbra mod.", modFolder);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "The meta.json for mod {mod} could not be read from disk, please ensure it exists and is not in use.", modFolder);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "The mod {mod} could not be parsed, please make sure you are using Penumbra version 1.7 or greater.", modFolder);
        }
        return null;
    }

    // Takes the existing list of selections (if any) and returns a list of selections (the existing list or a new one)
    private class ResetGroupSelectionVisitor : IGroupVisitor<List<string>?, List<string>>
    {
        private static void AddOptions(IReadOnlyList<IOption> options, uint optionBitfield, List<string> list)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if ((optionBitfield & (1 << i)) != 0)
                {
                    list.Add(options[i].Name);
                }
            }
        }

        public static List<string> VisitCombiningGroup(CombiningGroup combiningGroup, ref List<string>? param)
        {
            var defaultSettingsBitfield = combiningGroup.DefaultSettings;

            if (param != null)
            {
                // Remove invalid choices
                for (int i = 0; i < param.Count; i++)
                {
                    List<string> param2 = param; // Have to save a copy of the variable to use a `ref` in a lambda
                    if (!combiningGroup.Options.Any(option => option.Name == param2[i]))
                    {
                        param.RemoveAt(i);
                        i -= 1;
                    }
                }

                return param;
            }
            else
            {
                var result = new List<string>();
                AddOptions(combiningGroup.Options, unchecked((uint)defaultSettingsBitfield), result);
                return result;
            }
        }

        public static List<string> VisitImcGroup(ImcGroup imcGroup, ref List<string>? param)
        {
            var defaultSettingsBitfield = imcGroup.DefaultSettings;

            if (param != null)
            {
                // Remove invalid choices
                for (int i = 0; i < param.Count; i++)
                {
                    List<string> param2 = param; // Have to save a copy of the variable to use a `ref` in a lambda
                    if (!imcGroup.Options.Any(option => option.Name == param2[i]))
                    {
                        param.RemoveAt(i);
                        i -= 1;
                    }
                }

                return param;
            }
            else
            {
                var result = new List<string>();
                AddOptions(imcGroup.Options, unchecked((uint)defaultSettingsBitfield), result);
                return result;
            }
        }

        public static List<string> VisitMultiGroup(MultiGroup multiGroup, ref List<string>? param)
        {
            var defaultSettingsBitfield = multiGroup.DefaultSettings;

            if (param != null)
            {
                // Remove invalid choices
                for (int i = 0; i < param.Count; i++)
                {
                    List<string> param2 = param; // Have to save a copy of the variable to use a `ref` in a lambda
                    if (!multiGroup.Options.Any(option => option.Name == param2[i]))
                    {
                        param.RemoveAt(i);
                        i -= 1;
                    }
                }

                return param;
            }
            else
            {
                var result = new List<string>();
                AddOptions(multiGroup.Options, unchecked((uint)defaultSettingsBitfield), result);
                return result;
            }
        }

        public static List<string> VisitSingleGroup(SingleGroup singleGroup, ref List<string>? param)
        {
            var defaultSettingIndex = singleGroup.DefaultSettings;
            var defaultSettingName = singleGroup.Options[defaultSettingIndex].Name;

            if (param != null)
            {
                if (param.Count == 0)
                {
                    param.Add(defaultSettingName);
                    return param;
                }
                else
                {
                    // Looks n^2, but editor *should* only have one element in all anticipated circumstances, unless the group type just changed to single with this reload.
                    var firstValidChoice = param.FirstOrDefault(choice => singleGroup.Options.Any(option => option.Name == choice));
                    param.Clear();
                    param.Add(firstValidChoice ?? defaultSettingName);
                    return param;
                }
            }
            else
            {
                return new List<string> { defaultSettingName };
            }
        }
    }

    // Draws the options for the given group and returns true, or returns false if nothing was drawn (empty group, one-item single choice group)
    private class DrawGroupOptionsVisitor : IGroupVisitor<EmbeddedModpackDefinitionEditor, bool>
    {
        public static bool VisitCombiningGroup(CombiningGroup combiningGroup, ref EmbeddedModpackDefinitionEditor param)
        {
            return VisitMultiOptionGroup(combiningGroup, combiningGroup.Options, ref param);
        }

        public static bool VisitImcGroup(ImcGroup imcGroup, ref EmbeddedModpackDefinitionEditor param)
        {
            return VisitMultiOptionGroup(imcGroup, imcGroup.Options, ref param);
        }

        public static bool VisitMultiGroup(MultiGroup multiGroup, ref EmbeddedModpackDefinitionEditor param)
        {
            return VisitMultiOptionGroup(multiGroup, multiGroup.Options, ref param);
        }

        private static bool VisitMultiOptionGroup(Group group, IReadOnlyList<IOption> options, ref EmbeddedModpackDefinitionEditor editor)
        {
            if (options.Count > 0)
            {
                ImGui.TextUnformatted(group.Name);
                using (ImRaii.PushIndent())
                using (ImRaii.Group())
                using (ImRaii.PushId(group.Name))
                {
                    var groupSelections = editor._penumbraModSelectedOptions.GetOrAdd(group.Name, _ => new List<string>());
                    foreach (var option in options)
                    {
                        bool isChecked = groupSelections.Contains(option.Name);
                        if (ImGui.Checkbox(option.Name, ref isChecked))
                        {
                            if (isChecked)
                            {
                                groupSelections.Add(option.Name);
                            }
                            else
                            {
                                groupSelections.Remove(option.Name);
                            }
                        }
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool VisitSingleGroup(SingleGroup singleGroup, ref EmbeddedModpackDefinitionEditor editor)
        {
            if (singleGroup.Options.Count > 1)
            {
                ImGui.TextUnformatted(singleGroup.Name);
                using (ImRaii.PushIndent())
                using (ImRaii.Group())
                using (ImRaii.PushId(singleGroup.Name))
                {
                    var groupSelections = editor._penumbraModSelectedOptions.GetOrAdd(singleGroup.Name, _ => new List<string>());
                    foreach (var option in singleGroup.Options)
                    {
                        if (ImGui.RadioButton(option.Name, groupSelections.Contains(option.Name)))
                        {
                            groupSelections.Clear();
                            groupSelections.Add(option.Name);
                        }
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    private static Dictionary<string, string> ComputeFinalModPaths(PenumbraModMetaV4 modMeta, IReadOnlyDictionary<string, List<string>> selectedOptions, string fullModPath)
    {
        Dictionary<string, string> result = new();

        if (modMeta.Groups != null)
        {
            foreach (var group in modMeta.Groups.OrderBy(g => g.Priority))
            {
                foreach (var container in group.Visit<GroupFinalModContainersVisitor, IReadOnlyDictionary<string, List<string>>, IEnumerable<IContainer>>(ref selectedOptions))
                {
                    if (container.FileSwaps != null)
                    {
                        foreach (var fileSwap in container.FileSwaps)
                        {
                            result[fileSwap.Key] = fileSwap.Value;
                        }
                    }

                    if (container.Files != null)
                    {
                        foreach (var fileReplacement in container.Files)
                        {
                            result[fileReplacement.Key] = Path.Combine(fullModPath, fileReplacement.Value);
                        }
                    }
                }
            }
        }

        return result;
    }

    private class GroupFinalModContainersVisitor : IGroupVisitor<IReadOnlyDictionary<string, List<string>>, IEnumerable<IContainer>>
    {
        public static IEnumerable<IContainer> VisitCombiningGroup(CombiningGroup combiningGroup, ref IReadOnlyDictionary<string, List<string>> param)
        {
            if (param.TryGetValue(combiningGroup.Name, out var groupChoices))
            {
                // Each option corresponds to a bit in the container index
                uint containerIndex = 0;
                for (int i = 0; i < combiningGroup.Options.Count; i++)
                {
                    if (groupChoices.Contains(combiningGroup.Options[i].Name))
                    {
                        containerIndex |= 1u << i;
                    }
                }

                if (containerIndex < combiningGroup.Containers.Count)
                {
                    return [ combiningGroup.Containers[(int)containerIndex] ];
                }
            }

            return Array.Empty<IContainer>();
        }

        public static IEnumerable<IContainer> VisitImcGroup(ImcGroup imcGroup, ref IReadOnlyDictionary<string, List<string>> param)
        {
            // IMC groups have no containers because they do not replace resources.
            // Well, technically they do become .imc resources, but I'm punting on that for the time being.
            return Array.Empty<IContainer>();
        }

        public static IEnumerable<IContainer> VisitMultiGroup(MultiGroup multiGroup, ref IReadOnlyDictionary<string, List<string>> param)
        {
            List<IContainer> result = new();

            if (param.TryGetValue(multiGroup.Name, out var groupChoices))
            {
                foreach (var option in multiGroup.Options.OrderBy(o => o.Priority))
                {
                    if (groupChoices.Contains(option.Name))
                    {
                        result.Add(option);
                    }
                }
            }

            return result;
        }

        public static IEnumerable<IContainer> VisitSingleGroup(SingleGroup singleGroup, ref IReadOnlyDictionary<string, List<string>> param)
        {
            if (param.TryGetValue(singleGroup.Name, out var groupChoices))
            {
                foreach (var option in singleGroup.Options)
                {
                    if (groupChoices.Contains(option.Name))
                    {
                        return [ option ];
                    }
                }
            }

            return Array.Empty<IContainer>();
        }
    }

    private record class PendingReplacement(string DiskPath)
    {
        public byte[] NewData { get; set; } = null!;
        public ModCompressionScheme NewCompressionScheme { get; set; } = default;
    }

    public async Task ImportPenumbraModAsync(PenumbraModMetaV4 modMeta, IReadOnlyDictionary<string, List<string>> selectedOptions, string fullModPath)
    {
        Interlocked.Increment(ref _importingPenumbraMod);
        Dictionary<string, string> redirections = new();
        Dictionary<string, PendingReplacement> replacements = new();
        
        foreach (var paths in ComputeFinalModPaths(modMeta, selectedOptions, fullModPath))
        {
            if (Path.IsPathRooted(paths.Value))
            {
                replacements[paths.Key] = new(paths.Value);
            }
            else
            {
                redirections[paths.Key] = paths.Value;
            }
        }

        // Load all the files from disk in parallel
        await Parallel.ForEachAsync(replacements.Values, async (replacement, token) =>
        {
            var fileBytes = await File.ReadAllBytesAsync(replacement.DiskPath).ConfigureAwait(false);
            var compression = ModCompressionScheme.Zlib;
            var compressedBytes = await Task.Run(() => EmbeddedModResourceDefinition.CompressDataBytes(fileBytes, compression));

            replacement.NewData = compressedBytes;
            replacement.NewCompressionScheme = compression;
        });

        var oldModDirectory = Definition.PenumbraSourceModDirectory;
        var oldModVersion = Definition.PenumbraSourceModVersion;

        // Apply all their data in one synchronous transaction
        using (TransactionManager.BeginTransactionGroup($"Import Penumbra mod {modMeta.Name}"))
        {
            TransactionManager.DoTransaction(new DelegateTransaction("Refresh after undoing transaction", () => { }, RefreshPreviewLiveModpack, affectsDataModel: false));

            // Add the redirections
            foreach (var redirection in redirections)
            {
                var previousResource = Definition.ModdedResources.GetValueOrDefault(redirection.Key);
                var newRedirection = new GameModResourceDefinition()
                {
                    SourceGamePath = redirection.Value,
                };
                TransactionManager.DoTransaction(new DelegateTransaction($"Add redirection for {Path.GetFileName(redirection.Key)}", () =>
                {
                    if (previousResource != null)
                    {
                        Definition.ModdedResources.Remove(redirection.Key);
                    }
                    Definition.ModdedResources.Add(redirection.Key, newRedirection);
                }, () =>
                {
                    Definition.ModdedResources.Remove(redirection.Key);
                    if (previousResource != null)
                    {
                        Definition.ModdedResources.Add(redirection.Key, newRedirection);
                    }
                }, affectsDataModel: true));
            }

            // Add the replacements
            foreach (var replacement in replacements)
            {
                var previousResource = Definition.ModdedResources.GetValueOrDefault(replacement.Key);
                var newReplacement = new EmbeddedModResourceDefinition()
                {
                    CompressedDataBytes = replacement.Value.NewData,
                    CompressionScheme = replacement.Value.NewCompressionScheme,
                };
                TransactionManager.DoTransaction(new DelegateTransaction($"Add replacement for {Path.GetFileName(replacement.Key)} from {Path.GetFileName(replacement.Value.DiskPath)}", () =>
                {
                    if (previousResource != null)
                    {
                        Definition.ModdedResources.Remove(replacement.Key);
                    }
                    Definition.ModdedResources.Add(replacement.Key, newReplacement);
                }, () =>
                {
                    Definition.ModdedResources.Remove(replacement.Key);
                    if (previousResource != null)
                    {
                        Definition.ModdedResources.Add(replacement.Key, newReplacement);
                    }
                }, affectsDataModel: true));
            }

            Definition.PenumbraSourceModDirectory = Path.GetFileName(fullModPath);
            Definition.PenumbraSourceModVersion = modMeta.Version ?? "";

            TransactionManager.DoTransaction(new DelegateTransaction("Refresh after doing transaction", RefreshPreviewLiveModpack, () => { }, affectsDataModel: false));
        }
        Interlocked.Decrement(ref _importingPenumbraMod);

        _goToTab = 0;
    }

    public bool TryAddGameResource(string gamePath, string destinationPath)
    {
        // Ensure this does not already exist
        if (Definition.ModdedResources.ContainsKey(gamePath))
        {
            return false;
        }

        if (!IsPlausibleGamePath(gamePath))
        {
            return false;
        }

        if (!IsPlausibleGamePath(destinationPath))
        {
            return false;
        }

        var newModResourceDefinition = new GameModResourceDefinition()
        {
            SourceGamePath = destinationPath,
        };

        TransactionManager.DoTransaction(new DelegateTransaction($"Add redirection for {Path.GetFileName(gamePath)}", () =>
        {
            Definition.ModdedResources.Add(gamePath, newModResourceDefinition);
            RefreshPreviewLiveModpack();
        }, () =>
        {
            Definition.ModdedResources.Remove(gamePath);
            RefreshPreviewLiveModpack();
        }, affectsDataModel: true));
        return true;
    }

    private bool TryAddDiskResource(string gamePath, string diskPath)
    {
        // Ensure this does not already exist
        if (Definition.ModdedResources.ContainsKey(gamePath))
        {
            return false;
        }

        if (!IsPlausibleGamePath(gamePath))
        {
            return false;
        }

        var newDefinition = new DiskModResourceDefinition()
        {
            SourceDiskPath = diskPath,
        };

        TransactionManager.DoTransaction(new DelegateTransaction($"Add disk replacement for {Path.GetFileName(gamePath)} from {Path.GetFileName(diskPath)}", () =>
        {
            Definition.ModdedResources.Add(gamePath, newDefinition);
            RefreshPreviewLiveModpack();
        }, () =>
        {
            Definition.ModdedResources.Remove(gamePath);
            RefreshPreviewLiveModpack();
        }, affectsDataModel: true));

        return true;
    }

    private Task<bool> TryAddEmbeddedResourceAsync(string gamePath, string filePath)
    {
        // Ensure this does not already exist
        if (Definition.ModdedResources.ContainsKey(gamePath))
        {
            return Task.FromResult(false);
        }

        if (!IsPlausibleGamePath(gamePath))
        {
            return Task.FromResult(false);
        }

        var newDefinition = new EmbeddedModResourceDefinition();

        TransactionManager.DoTransaction(new DelegateTransaction($"Add embedded replacement for {Path.GetFileName(gamePath)}", () =>
        {
            Definition.ModdedResources.Add(gamePath, newDefinition);
            RefreshPreviewLiveModpack();
        }, () =>
        {
            Definition.ModdedResources.Remove(gamePath);
            RefreshPreviewLiveModpack();
        }, affectsDataModel: true));

        return TryUpdateEmbeddedResourceAsync(gamePath, newDefinition, filePath);
    }

    private async Task<bool> TryUpdateEmbeddedResourceAsync(string gamePath, EmbeddedModResourceDefinition definition, string filePath)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(filePath);
            var compression = ModCompressionScheme.Zlib;
            var compressedBytes = await Task.Run(() => EmbeddedModResourceDefinition.CompressDataBytes(fileBytes, compression));

            var oldBytes = definition.CompressedDataBytes;
            var oldCompression = definition.CompressionScheme;

            TransactionManager.DoTransaction(new DelegateTransaction($"Update replacement for {Path.GetFileName(gamePath)} from {Path.GetFileName(filePath)}", () =>
            {
                definition.CompressedDataBytes = compressedBytes;
                definition.CompressionScheme = compression;
                RefreshPreviewLiveModpack();
            }, () =>
            {
                definition.CompressedDataBytes = oldBytes;
                definition.CompressionScheme = oldCompression;
                RefreshPreviewLiveModpack();
            }, affectsDataModel: true));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update embedded resource {resource} in {modpack} from {sourceFile}!", gamePath, DisplayName, filePath);
            return false;
        }
    }

    private void UpdateDiskResource(string gamePath, DiskModResourceDefinition definition, string diskPath)
    {
        string previousValue = definition.SourceDiskPath;

        TransactionManager.DoTransaction(new SetPropertyTransaction<DiskModResourceDefinition, string>(Definition.DisplayName, gamePath, definition, diskPath, definition.SourceDiskPath, (newValue, oldValue) => definition.SourceDiskPath = newValue));
    }

    private bool TryRemoveModdedResource(string gamePath)
    {
        if (!Definition.ModdedResources.TryGetValue(gamePath, out var modResourceDefinition))
        {
            return false;
        }

        TransactionManager.DoTransaction(new DelegateTransaction($"Remove modded resource {Path.GetFileName(gamePath)}", () =>
        {
            Definition.ModdedResources.Remove(gamePath);
            RefreshPreviewLiveModpack();
        }, () =>
        {
            Definition.ModdedResources.Add(gamePath, modResourceDefinition);
            RefreshPreviewLiveModpack();
        }, affectsDataModel: true));
        return true;
    }

    private bool IsPlausibleGamePath(string path)
    {
        // TODO: Better validation
        return path.Length > 0;
    }

    public override void Selected()
    {
        OutlinerNode.IsSelected = true;
    }

    public override void Deselected()
    {
        OutlinerNode.IsSelected = false;
    }

    public void RefreshDependentPreviewObjects()
    {
        // Refresh any objects using this modpack.
        // Stage.Objects *can* be null during the Stage constructor, at which point there aren't any objects
        // that could possibly need their previews refreshed.
        if (Stage.Objects != null)
        {
            foreach (var obj in Stage.Objects.GetValuesAndDescendants())
            {
                if (obj.ModpackId == Key)
                {
                    obj.RefreshPreviewObject();
                }
            }
        }
    }

    public void AddedToStage()
    {
        IsInStage = true;
        PreviewLiveModpack = CreatePreviewLiveModpack();

        RefreshDependentPreviewObjects();
    }

    public void RemovedFromStage()
    {
        PreviewLiveModpack?.Dispose();
        PreviewLiveModpack = null;
        IsInStage = false;

        if (!Stage.IsDisposing)
        {
            RefreshDependentPreviewObjects();
        }
    }

    private record struct ModResourceFilterParams(string FilterText);

    private class ModResourceFilterer : IModResourceDefinitionVisitor<ModResourceFilterParams, bool>
    {
        public static bool VisitDiskModResourceDefinition(DiskModResourceDefinition definition, ref ModResourceFilterParams param)
        {
            // Check the filename
            if (definition.SourceDiskPath.Contains(param.FilterText, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            // Does not support searching through the contents of disk resoruces
            return false;
        }

        public static bool VisitEmbeddedModResourceDefinition(EmbeddedModResourceDefinition definition, ref ModResourceFilterParams param)
        {
            // Does not support searching through the contents of embedded resources
            return false;
        }

        public static bool VisitGameModResourceDefinition(GameModResourceDefinition definition, ref ModResourceFilterParams param)
        {
            // Check the game path
            if (definition.SourceGamePath.Contains(param.FilterText, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }

    private record struct ModRowDrawerParams(string GamePath, EmbeddedModpackDefinitionEditor Editor, FileDialogManager FileDialogManager);

    private class ModRowDrawer : IModResourceDefinitionVisitor<ModRowDrawerParams, object?>
    {
        public static object? VisitDiskModResourceDefinition(DiskModResourceDefinition definition, ref ModRowDrawerParams param)
        {
            // Destination path
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(definition.SourceDiskPath);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ImGui.SetClipboardText(definition.SourceDiskPath);
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted(definition.SourceDiskPath);
                    ImGui.Separator();
                    ImGui.TextDisabled("Click to copy");
                }
            }

            // Delete button
            ImGui.TableNextColumn();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                param.Editor.TryRemoveModdedResource(param.GamePath);
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted("Remove disk replacement");
                }
            }

            // Replace button
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Folder, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                var editor = param.Editor;
                var gamePath = param.GamePath;
                param.FileDialogManager.OpenFileDialog($"Choose new file for {Path.GetFileName(param.GamePath)}", Path.GetExtension(param.GamePath), (accepted, path) =>
                {
                    if (accepted)
                    {
                        editor.UpdateDiskResource(gamePath, definition, path);
                    }
                });
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted("Choose new file");
                }
            }

            return null;
        }

        public static object? VisitEmbeddedModResourceDefinition(EmbeddedModResourceDefinition definition, ref ModRowDrawerParams param)
        {
            // Contents
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(definition.CompressedDataBytes.Length == 0 ? "(empty)" : $"{Utils.ImGuiExtensions.ByteSizeToString(definition.CompressedDataBytes.LongLength)}{(definition.CompressionScheme != ModCompressionScheme.None ? " (compressed)" : "")}");

            // Delete button
            ImGui.TableNextColumn();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                param.Editor.TryRemoveModdedResource(param.GamePath);
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted("Remove embedded replacement");
                }
            }

            // Replace button
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Upload, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                var editor = param.Editor;
                var gamePath = param.GamePath;
                param.FileDialogManager.OpenFileDialog($"Replace mod data for {Path.GetFileName(param.GamePath)}", Path.GetExtension(param.GamePath), (accepted, path) =>
                {
                    if (accepted)
                    {
                        var _ = editor.TryUpdateEmbeddedResourceAsync(gamePath, definition, path);
                    }
                });
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted("Replace data");
                }
            }
            
            return null;
        }

        public static object? VisitGameModResourceDefinition(GameModResourceDefinition definition, ref ModRowDrawerParams param)
        {
            // Destination path
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(definition.SourceGamePath);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ImGui.SetClipboardText(definition.SourceGamePath);
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted(definition.SourceGamePath);
                    ImGui.Separator();
                    ImGui.TextDisabled("Click to copy");
                }
            }

            // Delete button
            ImGui.TableNextColumn();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash, new(ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
            {
                param.Editor.TryRemoveModdedResource(param.GamePath);
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted("Remove redirection");
                }
            }

            return null;
        }
    }
}
