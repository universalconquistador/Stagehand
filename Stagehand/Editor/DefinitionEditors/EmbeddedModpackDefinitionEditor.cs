using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions;
using Stagehand.Editor.Services;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors;

public class EmbeddedModpackDefinitionEditor : DefinitionEditorBase, IChildDefinitionEditor
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Embedded Modpack", "A collection of game files to modify.", FontAwesomeIcon.Archive);

    private readonly ISelectionManager _selectionManager;
    private readonly FileDialogManager _fileDialogManager;
    
    private EmbeddedModpackDefinition Definition { get; }

    public override string DisplayName => Definition.DisplayName;
    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;
    
    public OutlinerNode OutlinerNode { get; }

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

    public EmbeddedModpackDefinitionEditor(IServiceProvider serviceProvider, EmbeddedModpackDefinition definition, string key)
        : base(serviceProvider)
    {
        Definition = definition;
        Key = key;
        _selectionManager = serviceProvider.GetRequiredService<ISelectionManager>();
        _fileDialogManager = serviceProvider.GetRequiredService<FileDialogManager>();

        OutlinerNode = new OutlinerNode(DisplayName, Guid.NewGuid().ToString(), TypeInfo.Icon, TypeInfo.DisplayName, TypeInfo.Description);
        OutlinerNode.SortOrder = -1;
        OutlinerNode.Clicked += OnOutlinerNodeClicked;
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

    private string _filterText = "";
    private string _newReplacementGamePath = "";
    private string _newReplacementFilePath = "";
    private string _newRedirectionGamePath = "";
    private string _newRedirectionDestinationPath = "";
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

        using (var tabBar = ImRaii.TabBar("###ModpackEntries"))
        {
            if (tabBar.Success)
            {
                using (var replacementsTab = ImRaii.TabItem("Replacements"))
                {
                    if (replacementsTab.Success)
                    {
                        ImGuiExtensions.FilterBox("Filter", ref _filterText);
                        using (var table = ImRaii.Table("###Replacements", 3, ImGuiTableFlags.PadOuterX | ImGuiTableFlags.ScrollY, ImGui.GetContentRegionAvail()))
                        {
                            if (table.Success)
                            {
                                ImGui.TableSetupColumn("Game Path", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                                ImGui.TableSetupColumn("Contents", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                                ImGui.TableSetupColumn("###Commands", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2.0f + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.GetStyle().CellPadding.X * 2.0f);

                                ImGui.TableSetupScrollFreeze(0, 1);
                                ImGui.TableHeadersRow();

                                foreach (var entry in Definition.FileReplacements.OrderBy(pair => pair.Key, Utils.PathSorter.CurrentCultureIgnoreCase))
                                {
                                    if (_filterText.Length > 0 && !entry.Key.Contains(_filterText))
                                    {
                                        continue;
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

                                    // Contents
                                    ImGui.TableNextColumn();
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.TextUnformatted(entry.Value.Length == 0 ? "(empty)" : ImGuiExtensions.ByteSizeToString(entry.Value.LongLength));

                                    // Delete button
                                    ImGui.TableNextColumn();
                                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash, new(ImGui.GetFrameHeight())))
                                    {
                                        TryRemoveReplacement(entry.Key);
                                    }

                                    // Replace button
                                    ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Upload, new(ImGui.GetFrameHeight())))
                                    {
                                        _fileDialogManager.OpenFileDialog($"Replace mod data for {Path.GetFileName(entry.Key)}", Path.GetExtension(entry.Key), (accepted, path) =>
                                        {
                                            if (accepted)
                                            {
                                                TryUpdateReplacement(entry.Key, path);
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
                                }

                                ImGui.TableNextColumn();
                                ImGui.SetNextItemWidth(-1.0f);
                                ImGui.InputTextWithHint("###NewRedirectionGamePath", "Game path", ref _newReplacementGamePath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

                                ImGui.TableNextColumn();
                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X);
                                ImGui.InputTextWithHint("###NewRedirectionDestinationPath", "File path", ref _newReplacementFilePath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
                                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                                if (ImGuiComponents.IconButton(FontAwesomeIcon.Folder, new(ImGui.GetFrameHeight())))
                                {
                                    _fileDialogManager.OpenFileDialog($"Replace mod data{(_newReplacementGamePath.Length > 0 ? $" for {Path.GetFileName(_newReplacementGamePath)}" : "")}", _newReplacementGamePath.Length > 0 ? Path.GetExtension(_newReplacementGamePath) : ".*", (accepted, path) =>
                                    {
                                        if (accepted)
                                        {
                                            _newReplacementFilePath = path;
                                        }
                                    });
                                }

                                ImGui.TableNextColumn();
                                if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new(ImGui.GetFrameHeight())))
                                {
                                    if (TryAddReplacement(_newReplacementGamePath, _newReplacementFilePath))
                                    {
                                        _newReplacementGamePath = "";
                                        _newReplacementFilePath = "";
                                    }
                                }
                            }
                        }
                    }
                }

                using (var redirectionsTab = ImRaii.TabItem("Redirections"))
                {
                    if (redirectionsTab.Success)
                    {
                        ImGuiExtensions.FilterBox("Filter", ref _filterText);
                        using (var table = ImRaii.Table("###Redirections", 3, ImGuiTableFlags.PadOuterX | ImGuiTableFlags.ScrollY, ImGui.GetContentRegionAvail()))
                        {
                            if (table.Success)
                            {
                                ImGui.TableSetupColumn("Game Path", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                                ImGui.TableSetupColumn("Destination Path", ImGuiTableColumnFlags.WidthStretch, 1.0f);
                                ImGui.TableSetupColumn("###Commands", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2.0f + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.GetStyle().CellPadding.X * 2.0f);

                                ImGui.TableSetupScrollFreeze(0, 1);
                                ImGui.TableHeadersRow();

                                foreach (var entry in Definition.FileRedirections.OrderBy(pair => pair.Key, Utils.PathSorter.CurrentCultureIgnoreCase))
                                {
                                    if (_filterText.Length > 0 && !entry.Key.Contains(_filterText) && !entry.Value.Contains(_filterText))
                                    {
                                        continue;
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

                                    // Destination path
                                    ImGui.TableNextColumn();
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.TextUnformatted(entry.Value);
                                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                                    {
                                        ImGui.SetClipboardText(entry.Value);
                                    }
                                    if (ImGui.IsItemHovered())
                                    {
                                        using (ImRaii.Tooltip())
                                        {
                                            ImGui.TextUnformatted(entry.Value);
                                            ImGui.Separator();
                                            ImGui.TextDisabled("Click to copy");
                                        }
                                    }

                                    // Delete button
                                    ImGui.TableNextColumn();
                                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash, new(ImGui.GetFrameHeight())))
                                    {
                                        TryRemoveRedirection(entry.Key);
                                    }
                                }

                                ImGui.TableNextColumn();
                                ImGui.SetNextItemWidth(-1.0f);
                                ImGui.InputTextWithHint("###NewRedirectionGamePath", "Game path", ref _newRedirectionGamePath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

                                ImGui.TableNextColumn();
                                ImGui.SetNextItemWidth(-1.0f);
                                ImGui.InputTextWithHint("###NewRedirectionDestinationPath", "Destination path", ref _newRedirectionDestinationPath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

                                ImGui.TableNextColumn();
                                if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus, new(ImGui.GetFrameHeight())))
                                {
                                    if (TryAddRedirection(_newRedirectionGamePath, _newRedirectionDestinationPath))
                                    {
                                        _newRedirectionGamePath = "";
                                        _newRedirectionDestinationPath = "";
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    public bool TryAddRedirection(string gamePath, string destinationPath)
    {
        // Ensure this does not already exist
        if (Definition.FileRedirections.ContainsKey(gamePath)
            || Definition.FileReplacements.ContainsKey(gamePath))
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

        TransactionManager.DoTransaction(new DelegateTransaction($"Add redirection for {Path.GetFileName(gamePath)}", () =>
        {
            Definition.FileRedirections.Add(gamePath, destinationPath);
        }, () =>
        {
            Definition.FileRedirections.Remove(gamePath);
        }, affectsDataModel: true));
        return true;
    }

    public bool TryRemoveRedirection(string gamePath)
    {
        if (!Definition.FileRedirections.TryGetValue(gamePath, out var existingValue))
        {
            return false;
        }

        TransactionManager.DoTransaction(new DelegateTransaction($"Remove redirection for {Path.GetFileName(gamePath)}", () =>
        {
            Definition.FileRedirections.Remove(gamePath);
        }, () =>
        {
            Definition.FileRedirections.Add(gamePath, existingValue);
        }, affectsDataModel: true));
        return true;
    }

    public bool TryAddReplacement(string gamePath, string filePath)
    {
        // Ensure this does not already exist
        if (Definition.FileRedirections.ContainsKey(gamePath)
            || Definition.FileReplacements.ContainsKey(gamePath))
        {
            return false;
        }

        if (!IsPlausibleGamePath(gamePath))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(filePath);

            TransactionManager.DoTransaction(new DelegateTransaction($"Add replacement for {Path.GetFileName(gamePath)} from {Path.GetFileName(filePath)}", () =>
            {
                Definition.FileReplacements.Add(gamePath, bytes);
            }, () =>
            {
                Definition.FileReplacements.Remove(gamePath);
            }, affectsDataModel: true));

            return true;
        }
        catch (Exception ex)
        {
            // TODO: Log!
            return false;
        }
    }

    public bool TryUpdateReplacement(string gamePath, string filePath)
    {
        if (!Definition.FileReplacements.TryGetValue(gamePath, out var oldBytes))
        {
            return false;
        }

        try
        {
            var newBytes = File.ReadAllBytes(filePath);
            TransactionManager.DoTransaction(new DelegateTransaction($"Update replacement for {Path.GetFileName(gamePath)} from {Path.GetFileName(filePath)}", () =>
            {
                Definition.FileReplacements[gamePath] = newBytes;
            }, () =>
            {
                Definition.FileReplacements[gamePath] = oldBytes;
            }, affectsDataModel: true));

            return true;
        }
        catch (Exception ex)
        {
            // TODO: Log!
            return false;
        }
    }

    public bool TryRemoveReplacement(string gamePath)
    {
        if (!Definition.FileReplacements.TryGetValue(gamePath, out var bytes))
        {
            return false;
        }

        TransactionManager.DoTransaction(new DelegateTransaction($"Remove replacement for {Path.GetFileName(gamePath)}", () =>
        {
            Definition.FileReplacements.Remove(gamePath);
        }, () =>
        {
            Definition.FileReplacements.Add(gamePath, bytes);
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

    public void AddedToStage()
    {
        // TODO: Register with the redirection service!
    }

    public void RemovedFromStage()
    {
        // TODO: Unregister with the redirection service!
    }
}
