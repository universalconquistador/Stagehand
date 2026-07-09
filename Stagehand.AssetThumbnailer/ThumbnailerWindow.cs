using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Stagehand.Api;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.AssetThumbnailer;

public class ThumbnailerWindow : Window, IDisposable
{
    private enum JobStatus
    {
        NotStarted = 0,
        Running = 1,
        Complete = 2,
        Failed = 3,
    }

    private record class CaptureJob(string AssetPath, string PathHash, JobStatus Status)
    {
        public JobStatus Status { get; set; } = Status;
    }

    private readonly IFramework _framework;
    private readonly IStagehandApi _stagehandApi;
    private readonly Configuration _configuration;
    private readonly FileDialogManager _dialogManager;
    private readonly ThumbnailCapturer _thumbnailCapturer;
    private readonly List<CaptureJob> _jobs = new();

    private int _operationInProgress = 0;
    private bool _isCapturing = false;
    private CancellationTokenSource _cancellationTokenSource = new();

    public ThumbnailerWindow(IFramework framework, IDataManager dataManager, IGameInteropProvider gameInteropProvider, IStagehandApi stagehandApi, Configuration configuration)
        : base("Stagehand Thumbnailer")
    {
        _framework = framework;
        _stagehandApi = stagehandApi;
        _configuration = configuration;
        _dialogManager = new();
        _thumbnailCapturer = new(_stagehandApi, _framework, dataManager, gameInteropProvider);
    }

    public override void Draw()
    {
        var width = ImGui.GetContentRegionAvail().X;
        using (ImRaii.ItemWidth(width * 3.0f / 4.0f))
        {
            unsafe
            {
                var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentLobby.Instance();
                ImGui.LabelText("AgentLobby->LobbyUIStage", agent->LobbyUIStage.ToString());
            }

            using (ImRaii.ItemWidth(width * 3.0f / 4.0f - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X * 2))
            {
                string outputDirectory = _configuration.OutputDirectory;
                if (ImGui.InputText("###OutputDirectory", ref outputDirectory, maxLength: 512, flags: ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    _configuration.OutputDirectory = outputDirectory;
                    _configuration.Save();
                }
                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Folder, new(ImGui.GetFrameHeight())))
                {
                    _dialogManager.OpenFolderDialog("Output Directory", (accepted, path) =>
                    {
                        if (accepted)
                        {
                            _configuration.OutputDirectory = path;
                            _configuration.Save();
                        }
                    }, startPath: outputDirectory);
                }
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Output Directory");
            }

            ImGui.Spacing();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Capture Queue:");
            ImGui.SameLine();
            if (ImGui.Button("Append from File..."))
            {
                _dialogManager.OpenFileDialog("Asset paths...", ".txt", (accepted, path) =>
                {
                    if (accepted && path.Count == 1)
                    {
                        _configuration.LastQueueDirectory = Path.GetDirectoryName(path[0]) ?? "";
                        _configuration.Save();
                        EnqueuePathsFromFile(path[0]);
                    }
                }, selectionCountMax: 1, startPath: _configuration.LastQueueDirectory);
            }

            int totalJobs = 0;
            int finishedJobs = 0;
            using (var listbox = ImRaii.ListBox("###CaptureQueue", ImGui.GetContentRegionAvail() - new System.Numerics.Vector2(0.0f, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y)))
            {
                if (listbox.Success)
                {
                    using (var table = ImRaii.Table("###Queue", 3))
                    {
                        if (table.Success)
                        {
                            ImGui.TableSetupColumn("###StatusIcon", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
                            ImGui.TableSetupColumn("###GamePath", ImGuiTableColumnFlags.WidthStretch, 1);
                            ImGui.TableSetupColumn("###PathHash", ImGuiTableColumnFlags.WidthStretch, 0.25f);

                            foreach (var job in _jobs)
                            {
                                ImGui.TableNextColumn();
                                using (ImRaii.PushColor(ImGuiCol.Text, job.Status == JobStatus.Complete ? ImGuiColors.HealerGreen : job.Status == JobStatus.Failed ? ImGuiColors.DPSRed : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]))
                                {
                                    using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
                                    {
                                        ImGui.TextUnformatted(job.Status == JobStatus.Complete ? FontAwesomeIcon.CheckCircle.ToIconString() : job.Status == JobStatus.Failed ? FontAwesomeIcon.ExclamationCircle.ToIconString() : "");
                                    }
                                }

                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(job.AssetPath);

                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(job.PathHash);

                                totalJobs += 1;
                                if (job.Status == JobStatus.Complete || job.Status == JobStatus.Failed)
                                {
                                    finishedJobs += 1;
                                }
                            }
                        }
                    }
                }
            }

            using (ImRaii.Disabled(!_isCapturing && _operationInProgress > 0))
            {
                if (ImGui.Button(_isCapturing ? "Cancel" : "Begin"))
                {
                    if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) == 0)
                    {
                        _isCapturing = true;
                        _cancellationTokenSource = new();
                        _ = Task.Run(async () =>
                        {
                            foreach (var job in _jobs)
                            {
                                job.Status = JobStatus.NotStarted;
                            }

                            await _thumbnailCapturer.SetUpCaptureAsync();

                            for (int i = 0; i < _jobs.Count; i++)
                            {
                                if (_jobs[i].Status == JobStatus.NotStarted)
                                {
                                    _jobs[i].Status = JobStatus.Running;

                                    // TODO: Implement capture!
                                    //await Task.Delay(TimeSpan.FromSeconds(1.0f));

                                    await _thumbnailCapturer.CaptureAssetAsync(_jobs[i].AssetPath, _configuration.OutputDirectory, _jobs[i].PathHash);

                                    _jobs[i].Status = JobStatus.Complete;
                                }

                                if (_cancellationTokenSource.IsCancellationRequested)
                                {
                                    break;
                                }
                            }

                            await _thumbnailCapturer.TearDownCaptureAsync();

                            _isCapturing = false;
                            _operationInProgress = 0;
                        });
                    }
                    else
                    {
                        _cancellationTokenSource.Cancel();
                    }
                }
            }
            ImGui.SameLine();
            if (_isCapturing && totalJobs > 0)
            {
                ImGui.ProgressBar((float)finishedJobs / totalJobs, new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()), $"{finishedJobs} / {totalJobs}");
            }
        }

        _dialogManager.Draw();
    }

    private void EnqueuePath(string gamePath)
    {
        var hasher = new Crc32Hasher();
        hasher.AdvanceASCII(gamePath);

        _jobs.Add(new(gamePath, ((int)hasher.Value).ToString("X8"), JobStatus.NotStarted));
    }

    private void EnqueuePathsFromFile(string filename)
    {
        try
        {
            var lines = File.ReadAllLines(filename);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    EnqueuePath(trimmed);
                }
            }
        }
        catch (IOException exception)
        {
            Console.WriteLine(exception.ToString());
        }
    }

    public void Dispose()
    {

    }
}
