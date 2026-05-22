using Dalamud.Plugin.Services;
using Microsoft.Extensions.ObjectPool;
using Stagehand.Api;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Stagehand.ApiDemo;

public class Trail : IDisposable
{
    private class TrailMark
    {
        public string ObjectId { get; set; } = null!;
        public VfxObjectDefinition Definition { get; set; } = null!;
        public DateTimeOffset CreationTime { get; set; }
    }

    private readonly Configuration _configuration;
    private readonly IFramework _framework;
    private readonly IObjectTable _objectTable;
    private readonly IStagehandApi _stagehandApi;

    private readonly string _stageId;
    private readonly StageDefinition _stageDefinition;
    private readonly List<TrailMark> _trailMarks = new();

    private ulong _nextMarkId = 0;
    private TrailMark? _lastMark = null;
    private DateTimeOffset _lastUpdate = default;

    private readonly ObjectPool<TrailMark> _markPool = ObjectPool.Create<TrailMark>();
    private readonly ObjectPool<VfxObjectDefinition> _vfxPool = ObjectPool.Create<VfxObjectDefinition>();

    public Trail(Configuration configuration, IFramework framework, IObjectTable objectTable, IStagehandApi stagehandApi)
    {
        _configuration = configuration;
        _framework = framework;
        _objectTable = objectTable;
        _stagehandApi = stagehandApi;

        _stageId = Guid.NewGuid().ToString();
        _stageDefinition = new();

        _framework.Update += OnUpdate;
        _stagehandApi.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(StageLocation location)
    {
        foreach (var mark in _trailMarks)
        {
            _stageDefinition.Objects.Remove(mark.ObjectId);
            _vfxPool.Return(mark.Definition);
            mark.Definition = null!;
            _markPool.Return(mark);
        }

        _trailMarks.Clear();
        _lastMark = null;
    }

    private void OnUpdate(IFramework _)
    {
        var currentTime = DateTimeOffset.UtcNow;
        var interval = TimeSpan.FromSeconds(_configuration.PlacementIntervalSeconds);
        var lifespan = TimeSpan.FromSeconds(_configuration.PlacementLifespanSeconds);

        var player = _objectTable.LocalPlayer;
        if (currentTime >= _lastUpdate + interval && player != null)
        {
            // Fade or remove existing marks
            for (int i = _trailMarks.Count - 1; i >= 0; i--)
            {
                var mark = _trailMarks[i];
                if (mark.CreationTime + lifespan <= currentTime)
                {
                    // Remove vfx from stage
                    _stageDefinition.Objects.Remove(mark.ObjectId);

                    // Remove mark by swapping with the last element and then removing that, saving shifting
                    _trailMarks[i] = _trailMarks[_trailMarks.Count - 1];
                    _trailMarks.RemoveAt(_trailMarks.Count - 1);

                    // Null out _lastMark if it's this mark (should never happen and interlocked is overkill but lol)
                    Interlocked.CompareExchange(ref _lastMark, null, mark);

                    // Return to pools
                    _vfxPool.Return(mark.Definition);
                    mark.Definition = null!;
                    _markPool.Return(mark);
                }
                else
                {
                    // Adjust the opacity
                    var fadeFactor = (currentTime - mark.CreationTime) / lifespan;
                    mark.Definition.Color = new Vector4(1.0f, 1.0f, 1.0f, 1.0f - (float)fadeFactor);
                }
            }

            // Create a new mark if there isn't one already or if the player has moved
            if (_configuration.EnableTrail)
            {
                var playerPosition = player.Position;
                var playerRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, player.Rotation);
                if (_lastMark == null || Vector3.DistanceSquared(playerPosition, _lastMark.Definition.Position) > 0.0125f)
                {
                    if (_lastMark == null || _lastMark.CreationTime + interval < currentTime)
                    {
                        var id = Interlocked.Increment(ref _nextMarkId);
                        var vfx = _vfxPool.Get();
                        vfx.VfxGamePath = _configuration.PlacementVFXGamePath;
                        vfx.Position = playerPosition;
                        vfx.RotationQuaternion = playerRotation;
                        vfx.Scale = new Vector3(_configuration.PlacementScale);
                        vfx.DisplayName = $"Mark {id}"; // Not strictly necessary as temporary stages will never be opened in an editor
                        vfx.Color = Vector4.One; // We're pooling the VfxObjectDefinitions, so we need to make sure to reset the color

                        var mark = _markPool.Get();
                        mark.ObjectId = $"mark-{id}";
                        mark.CreationTime = currentTime;
                        mark.Definition = vfx;
                        _stageDefinition.Objects.Add(mark.ObjectId, vfx);
                        _trailMarks.Add(mark);
                        _lastMark = mark;
                    }
                }
                else
                {
                    if (_lastMark != null)
                    {
                        // Player is standing still at the same spot as last time--just pretend the mark is new
                        _lastMark.CreationTime = currentTime;
                        _lastMark.Definition.VfxGamePath = _configuration.PlacementVFXGamePath;
                        _lastMark.Definition.Scale = new Vector3(_configuration.PlacementScale);
                    }
                }
            }

            // Send latest definition state to Stagehand
            if (_stagehandApi.CheckApiAvailability() == StagehandApiAvailability.Available)
            {
                _stagehandApi.TryCreateOrUpdateTemporaryStage(_stageDefinition.ToDefinitionString(), _stageId, "Trail (Stagehand API Demo)");
                _stagehandApi.TrySetTemporaryStageVisible(_stageId, true);
            }

            _lastUpdate = currentTime;
        }
    }

    public void Dispose()
    {
        _stagehandApi.LocationChanged -= OnLocationChanged;
        _framework.Update -= OnUpdate;
        if (_stagehandApi.CheckApiAvailability() == StagehandApiAvailability.Available)
        {
            _stagehandApi.TryDestroyTemporaryStage(_stageId);
        }
    }
}
