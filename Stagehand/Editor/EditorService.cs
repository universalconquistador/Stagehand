using Dalamud.Interface.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stagehand.Definitions;
using Stagehand.Editor.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Stagehand.Editor;

/// <summary>
/// Coordinates opening &amp; closing Stage definition editors.
/// </summary>
public interface IEditorService
{
    /// <summary>
    /// The editor window that is currently open, if any.
    /// </summary>
    IEditorWindow? OpenEditorWindow { get; }

    /// <summary>
    /// Attempts to open the editor for the Stage definition at the given path.
    /// </summary>
    /// <remarks>
    /// Attempting to open a file that does not exist or cannot be parsed or while
    /// an editor is already open will result in failure.
    /// </remarks>
    /// <param name="definitionFilename">The filename of the Stage definition to open.</param>
    /// <returns>True for success, or false otherwise.</returns>
    bool TryOpenEditor(string definitionFilename);

    /// <summary>
    /// Raised when an editor has been opened for the Stage definition at the given path.
    /// </summary>
    event Action<string> EditorOpened;

    /// <summary>
    /// Raised when an editor was closed for the Stage definition at the given path.
    /// </summary>
    event Action<string> EditorClosed;

    /// <summary>
    /// Raised when an editor saved the Stage definition at the given path.
    /// </summary>
    event Action<string> EditorSaved;
}

internal class EditorService : IEditorService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WindowSystem _windowSystem;
    private readonly ILogger _logger;

    private EditorWindow? _editorWindow;

    public IEditorWindow? OpenEditorWindow { get; private set; }
    
    public event Action<string>? EditorOpened;
    public event Action<string>? EditorClosed;
    public event Action<string>? EditorSaved;

    public EditorService(IServiceProvider serviceProvider, WindowSystem windowSystem, ILogger<EditorService> logger)
    {
        _serviceProvider = serviceProvider;
        _windowSystem = windowSystem;
        _logger = logger;
    }

    public bool TryOpenEditor(string definitionFilename)
    {
        if (OpenEditorWindow != null)
        {
            return false;
        }

        try
        {
            using (var stream = new FileStream(definitionFilename, FileMode.Open, FileAccess.Read))
            {
                if (StageDefinition.TryParseJSONStream(stream, out var defintiion))
                {
                    var scope = _serviceProvider.CreateScope();
                    var newWindow = new EditorWindow(scope, definitionFilename, defintiion);
                    newWindow.Closed += () =>
                    {
                        _windowSystem.RemoveWindow(newWindow);
                        newWindow.Dispose();
                        _editorWindow = null;
                        OpenEditorWindow = null;
                        EditorClosed?.Invoke(definitionFilename);
                    };
                    newWindow.Saved += () => EditorSaved?.Invoke(definitionFilename);
                    _editorWindow = newWindow;
                    _windowSystem.AddWindow(newWindow);
                    newWindow.IsOpen = true;

                    OpenEditorWindow = newWindow;
                    EditorOpened?.Invoke(definitionFilename);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to parse Stage definition file {path}!", definitionFilename);
                    return false;
                }
            }
        }
        catch (IOException ioException)
        {
            _logger.LogWarning(ioException, "Failed to open Stage definition file {path}!", definitionFilename);
            return false;
        }
    }

    public void Dispose()
    {
        _editorWindow?.IsOpen = false;
        _editorWindow?.OnClose();
    }
}
