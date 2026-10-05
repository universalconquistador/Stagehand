using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using InteropGenerator.Runtime;
using Microsoft.Extensions.Logging;
using System;
using System.Text;

namespace Stagehand.Live;

/// <summary>
/// Makes weapon draw objects honor modpacks by rewriting the resource paths they resolve for themselves into modpack paths.
/// </summary>
/// <remarks>
/// A weapon is created from model IDs rather than from a path, so unlike a background object there is no path for us to
/// bake a modpack into up front. Instead the game asks the weapon to resolve its own paths through virtual functions on
/// its virtual table, and those are hooked here: whenever a weapon we registered with
/// <see cref="IResourceRedirectionService.RegisterDrawObjectModpack"/> resolves a path, the modpack prefix is applied to
/// it so that the resource load lands in the same redirection machinery every other object type uses.
/// </remarks>
internal interface IWeaponPathRedirectionService
{
    /// <summary>
    /// Installs the path resolution hooks for the virtual table of the given weapon, if they are not already installed.
    /// </summary>
    /// <param name="weapon">A pointer to a live weapon whose virtual table should be hooked.</param>
    /// <remarks>
    /// All weapons share a single virtual table, so this only does work for the first weapon it is given. The virtual
    /// table has no exported address of its own, which is why an instance is needed to find it.
    /// </remarks>
    void EnsureHooksInstalled(nint weapon);
}

internal sealed unsafe class WeaponPathRedirectionService : IWeaponPathRedirectionService, IDisposable
{
    private readonly ILogger _logger;
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly IResourceRedirectionService _resourceRedirectionService;
    private readonly StagehandConfiguration _config;

    private readonly object _installLock = new();
    private bool _installed;
    private bool _installFailed;
    private bool _warnedMissingVirtualTable;

    private Hook<Weapon.Delegates.ResolveMdlPath>? _resolveMdlPathHook;
    private Hook<Weapon.Delegates.ResolveMtrlPath>? _resolveMtrlPathHook;
    private Hook<Weapon.Delegates.ResolveImcPath>? _resolveImcPathHook;

    public WeaponPathRedirectionService(ILogger<WeaponPathRedirectionService> logger, IGameInteropProvider gameInteropProvider, IResourceRedirectionService resourceRedirectionService, StagehandConfiguration config)
    {
        _logger = logger;
        _gameInteropProvider = gameInteropProvider;
        _resourceRedirectionService = resourceRedirectionService;
        _config = config;
    }

    public void EnsureHooksInstalled(nint weapon)
    {
        if (_installed || _installFailed || weapon == 0)
        {
            return;
        }

        lock (_installLock)
        {
            if (_installed || _installFailed)
            {
                return;
            }

            var virtualTable = ((Weapon*)weapon)->VirtualTable;
            if (virtualTable == null)
            {
                // Deliberately not a latch, unlike a failure to install below: that means hooking does not work at
                // all, while this means only that this particular weapon has no table to read the addresses from, and
                // the next one may. So stay retryable, but do not repeat the warning for every weapon.
                if (!_warnedMissingVirtualTable)
                {
                    _warnedMissingVirtualTable = true;
                    _logger.LogWarning("Weapon at {weapon:X} has no virtual table, so its resource paths cannot be redirected. Will try again with the next weapon.", weapon);
                }

                return;
            }

            try
            {
                _resolveMdlPathHook = _gameInteropProvider.HookFromAddress<Weapon.Delegates.ResolveMdlPath>((nint)virtualTable->ResolveMdlPath, ResolveMdlPathDetour);
                _resolveMtrlPathHook = _gameInteropProvider.HookFromAddress<Weapon.Delegates.ResolveMtrlPath>((nint)virtualTable->ResolveMtrlPath, ResolveMtrlPathDetour);
                _resolveImcPathHook = _gameInteropProvider.HookFromAddress<Weapon.Delegates.ResolveImcPath>((nint)virtualTable->ResolveImcPath, ResolveImcPathDetour);

                _resolveMdlPathHook.Enable();
                _resolveMtrlPathHook.Enable();
                _resolveImcPathHook.Enable();

                _installed = true;
                _logger.LogDebug("Installed weapon path redirection hooks from virtual table {virtualTable:X}.", (nint)virtualTable);
            }
            catch (Exception exception)
            {
                // Don't retry on every weapon we create if the hooks can't be installed at all
                _installFailed = true;
                _logger.LogError(exception, "Failed to install weapon path redirection hooks. Modpacks will not apply to weapons.");

                DisposeHooks();
            }
        }
    }

    private CStringPointer ResolveMdlPathDetour(Weapon* thisPtr, byte* pathBuffer, nuint pathBufferSize, uint slotIndex)
    {
        var resolvedPath = _resolveMdlPathHook!.OriginalDisposeSafe(thisPtr, pathBuffer, pathBufferSize, slotIndex);
        return ApplyModpack(thisPtr, pathBuffer, pathBufferSize, resolvedPath);
    }

    private CStringPointer ResolveMtrlPathDetour(Weapon* thisPtr, byte* pathBuffer, nuint pathBufferSize, uint slotIndex, byte* mtrlFileName)
    {
        var resolvedPath = _resolveMtrlPathHook!.OriginalDisposeSafe(thisPtr, pathBuffer, pathBufferSize, slotIndex, mtrlFileName);
        return ApplyModpack(thisPtr, pathBuffer, pathBufferSize, resolvedPath);
    }

    private CStringPointer ResolveImcPathDetour(Weapon* thisPtr, byte* pathBuffer, nuint pathBufferSize, uint slotIndex)
    {
        var resolvedPath = _resolveImcPathHook!.OriginalDisposeSafe(thisPtr, pathBuffer, pathBufferSize, slotIndex);
        return ApplyModpack(thisPtr, pathBuffer, pathBufferSize, resolvedPath);
    }

    /// <summary>
    /// Rewrites a path the game just resolved for a weapon into a modpack path, if that weapon belongs to a modpack.
    /// </summary>
    /// <remarks>
    /// These hooks are on the shared weapon virtual table, so they run for every weapon in the game, including the
    /// player's. Anything that isn't one of ours has to fall straight through, and cheaply.
    /// </remarks>
    private CStringPointer ApplyModpack(Weapon* weapon, byte* pathBuffer, nuint pathBufferSize, CStringPointer resolvedPath)
    {
        try
        {
            if (!resolvedPath.HasValue || pathBuffer == null)
            {
                return resolvedPath;
            }

            if (!_resourceRedirectionService.TryGetDrawObjectModpack((nint)weapon, out var modpack))
            {
                return resolvedPath;
            }

            var gamePath = resolvedPath.ToString();
            if (string.IsNullOrEmpty(gamePath) || gamePath.StartsWith(ResourceRedirectionService.StagehandPathIdentifier, StringComparison.Ordinal))
            {
                return resolvedPath;
            }

            // Another redirector may have resolved this slot before us: Penumbra hooks these same
            // virtual functions and hands back an absolute path on disk when the mod is enabled
            // in a collection, and prefixing that yields neither a modpack key nor a file. So
            // only rewrite what still looks like a game path. Paths the modpack does not carry
            // are fine to rewrite - the redirection machinery unwraps those back to the game
            // path, which is what puts the modpack in scope for whatever they load in turn.
            if (gamePath.Length < 2 || gamePath[1] == ':' || gamePath[0] == '/' || gamePath[0] == '\\')
            {
                return resolvedPath;
            }

            var modpackPath = ResourceRedirectionHelpers.MakeModpackPath(gamePath, modpack);
            var byteCount = Encoding.UTF8.GetByteCount(modpackPath);

            // We have to write back into the buffer the game gave us, so leave the path alone rather than truncate it
            if ((nuint)(byteCount + 1) > pathBufferSize)
            {
                _logger.LogWarning("Cannot redirect weapon path '{path}' to modpack {pack}: the modpack path needs {needed} bytes but the game's buffer is only {size}.",
                    gamePath, modpack.DebugName, byteCount + 1, pathBufferSize);
                return resolvedPath;
            }

            var buffer = new Span<byte>(pathBuffer, byteCount + 1);
            Encoding.UTF8.GetBytes(modpackPath, buffer);
            buffer[byteCount] = 0;

            if (_config.LogModpackResourceHandled)
            {
                _logger.LogDebug("Redirected weapon path '{path}' to '{modpackPath}'.", gamePath, modpackPath);
            }

            return pathBuffer;
        }
        catch (Exception exception)
        {
            // This runs on the game's thread inside its own call stack, so an escaping exception would take the game with it
            _logger.LogError(exception, "Failed to apply a modpack to a resolved weapon path.");
            return resolvedPath;
        }
    }

    public void Dispose()
    {
        lock (_installLock)
        {
            DisposeHooks();
            _installed = false;
        }
    }

    /// <summary>
    /// Disposes the hooks, leaving the fields in place: a detour already in flight still needs to reach
    /// <see cref="Hook{T}.OriginalDisposeSafe"/> through them rather than fault on a null.
    /// </summary>
    private void DisposeHooks()
    {
        _resolveMdlPathHook?.Dispose();
        _resolveMtrlPathHook?.Dispose();
        _resolveImcPathHook?.Dispose();
    }
}
