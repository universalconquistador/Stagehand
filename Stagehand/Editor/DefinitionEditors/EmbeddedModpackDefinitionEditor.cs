using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions;
using Stagehand.Definitions.ModResources;
using Stagehand.Definitions.Objects;
using Stagehand.Editor.Services;
using Stagehand.Live;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Editor.DefinitionEditors;

public class EmbeddedModpackDefinitionEditor : DefinitionEditorBase, IChildDefinitionEditor
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Embedded Modpack", "A collection of game files to modify.", FontAwesomeIcon.Archive);

    private enum NewModResourceType
    {
        DiskResource,
        EmbeddedResource,
        GameResource,
    }

    private readonly IObjectTable _objectTable;
    private readonly ISelectionManager _selectionManager;
    private readonly IResourceRedirectionService _resourceRedirectionService;
    private readonly FileDialogManager _fileDialogManager;
    
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

    public EmbeddedModpackDefinitionEditor(IServiceProvider serviceProvider, EmbeddedModpackDefinition definition, StageDefinitionEditor stage, string key)
        : base(serviceProvider)
    {
        Definition = definition;
        Stage = stage;
        Key = key;
        _objectTable = serviceProvider.GetRequiredService<IObjectTable>();
        _selectionManager = serviceProvider.GetRequiredService<ISelectionManager>();
        _resourceRedirectionService = serviceProvider.GetRequiredService<IResourceRedirectionService>();
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

            RefreshDependantPreviewObjects();
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
            foreach (var objectEditor in Stage.Objects.Values)
            {
                if (objectEditor.ModpackId == Key)
                {
                    objectEditor.ModpackId = string.Empty;
                }
            }

            Stage.EmbeddedModpacks.Remove(this);
        }
    }

    private string _filterText = "";
    private string _newModResourceGamePath = "";
    private string _newModResourceDiskFilePath = "";
    private string _newModResourceRedirectionPath = "";
    private NewModResourceType _newModResourceType = NewModResourceType.DiskResource;
    private bool _isAddingEmbed = false;
    protected override void OnDrawProperties()
    {
        string displayName = DisplayName;
        if (ImGui.InputText("Name", ref displayName, 512, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            SetDisplayName(displayName);
        }

        if (PenumbraSourceModDirectory != string.Empty)
        {
            ImGui.LabelText("Source Penumbra Mod", $"{PenumbraSourceModDirectory}{(PenumbraSourceModVersion != string.Empty ? $" ver. {PenumbraSourceModVersion}" : "")}");

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
            using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.LeftCtrl)))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.SyncAlt, new(ImGui.GetFrameHeight())))
                {
                    UpdateFromPenumbraMod();
                }
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.TextUnformatted("Update from Penumbra Mod");
                    ImGui.Separator();
                    ImGui.TextDisabled("Replaces the contents of this modpack with those of the Penumbra mod.\nThis is destructive - hold Ctrl to enable, and remember that this IS undoable.");
                }
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Resources:");
        ImGuiExtensions.FilterBox("Filter", ref _filterText);
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
                        if (isModelResource || isVfxResource)
                        {
                            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new Vector2(ImGui.GetFrameHeight())))
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

                                if (newDefinition != null)
                                {
                                    newDefinition.DisplayName = Path.GetFileNameWithoutExtension(entry.Key);
                                    newDefinition.ModpackId = Key;
                                    newDefinition.Position = (_objectTable.LocalPlayer?.Position ?? Vector3.Zero) + Vector3.Transform(Vector3.UnitZ, rotation) * 2.0f;
                                    newDefinition.RotationQuaternion = rotation;
                                    Stage.Objects.Add(newDefinition);
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
                using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], _newModResourceType == NewModResourceType.DiskResource))
                {
                    if (ImGui.Button("File", size: new Vector2(resourceTypeButtonWidth, 0.0f)))
                    {
                        _newModResourceType = NewModResourceType.DiskResource;
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted("The new resource will point to a file on disk.");
                        ImGui.Separator();
                        ImGui.TextDisabled("If you send this Stage file to someone, the modded resource will not be sent.");
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
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Folder, new(ImGui.GetFrameHeight())))
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
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new(ImGui.GetFrameHeight())))
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
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new(ImGui.GetFrameHeight())))
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
                        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new(ImGui.GetFrameHeight())))
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
            // TODO: Log!
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

    public void UpdateFromPenumbraMod()
    {
        // TODO: Implement!
    }

    public override void Selected()
    {
        OutlinerNode.IsSelected = true;
    }

    public override void Deselected()
    {
        OutlinerNode.IsSelected = false;
    }

    public void RefreshDependantPreviewObjects()
    {
        // Refresh any objects using this modpack.
        // Stage.Objects *can* be null during the Stage constructor, at which point there aren't any objects
        // that could possibly need their previews refreshed.
        if (Stage.Objects != null)
        {
            foreach (var obj in Stage.Objects)
            {
                if (obj.Value.ModpackId == Key)
                {
                    obj.Value.RefreshPreviewObject();
                }
            }
        }
    }

    public void AddedToStage()
    {
        IsInStage = true;
        PreviewLiveModpack = CreatePreviewLiveModpack();

        RefreshDependantPreviewObjects();
    }

    public void RemovedFromStage()
    {
        PreviewLiveModpack?.Dispose();
        PreviewLiveModpack = null;
        IsInStage = false;

        if (!Stage.IsDisposing)
        {
            RefreshDependantPreviewObjects();
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
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash, new(ImGui.GetFrameHeight())))
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
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Folder, new(ImGui.GetFrameHeight())))
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
            ImGui.TextUnformatted(definition.CompressedDataBytes.Length == 0 ? "(empty)" : $"{ImGuiExtensions.ByteSizeToString(definition.CompressedDataBytes.LongLength)}{(definition.CompressionScheme != ModCompressionScheme.None ? " (compressed)" : "")}");

            // Delete button
            ImGui.TableNextColumn();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash, new(ImGui.GetFrameHeight())))
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
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Upload, new(ImGui.GetFrameHeight())))
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
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash, new(ImGui.GetFrameHeight())))
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
