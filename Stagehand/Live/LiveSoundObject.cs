using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Sound;
using FFXIVClientStructs.FFXIV.Common.Math;
using Stagehand.Definitions.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMJIFarmManagement;

namespace Stagehand.Live;

// Sounds aren't Scene objects like most other LiveObjects.
internal sealed unsafe class LiveSoundObject : ILiveObject
{
    private readonly IFramework _framework;

    private SoundData* SoundDataPtr { get; set; }
    public string SoundGamePath { get; }
    public ILiveModpack? Modpack { get; }

    public Vector3 Position
    {
        get => new(SoundDataPtr->PosX, SoundDataPtr->PosY, SoundDataPtr->PosZ);
        set => SoundDataPtr->SetPosition(SoundDataPtr->IsPositional, value.X, value.Y, value.Z);
    }

    public LiveSoundObject(IFramework framework, SoundData* soundData, string soundGamePath, ILiveModpack? modpack)
    {
        _framework = framework;

        SoundDataPtr = soundData;
        SoundGamePath = soundGamePath;
        Modpack = modpack;

        _framework.Update += this.OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Force loop non-looping sounds
        // This is based on the check that the SoundManager uses to identify completed auto-releasing sounds in its update
        bool isFinished = !SoundDataPtr->GetIsLoadingSoundResource()
            && !SoundDataPtr->IsPlaying()
            && SoundDataPtr->FadeOutDuration <= 0.0f;
        if (isFinished)
        {
            var volume = SoundDataPtr->GetVolume();
            var fadeIn = SoundDataPtr->GetFadeInDuration();
            var positionX = SoundDataPtr->GetPositionX();
            var positionY = SoundDataPtr->GetPositionY();
            var positionZ = SoundDataPtr->GetPositionZ();
            var speed = SoundDataPtr->GetSpeed();
            var soundIndex = SoundDataPtr->GetSoundNumber();
            var isPositional = SoundDataPtr->GetIsPositional();

            DestroySoundData();

            SoundDataPtr = SoundManager.Instance()->PlaySound(SoundGamePath, volume, fadeIn, positionX, positionY, positionZ, speed, 0, soundIndex, autoRelease: false, SoundVolumeCategory.BypassVolumeRules, false, midiNote: -1, false, defaultFadeOut: false, isPositional, false);
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;

        DestroySoundData();
    }

    private void DestroySoundData()
    {
        SoundDataPtr->Stop(20);
        bool resetSucceeded = SoundDataPtr->TryReset();
        if (resetSucceeded)
        {
            var resourceHandle = SoundDataPtr->GetSoundResourceHandle();
            if (resourceHandle != null)
            {
                resourceHandle->DecRef();
            }
            SoundManager.Instance()->ReleaseSoundData(SoundDataPtr);
        }
        else
        {
            // We'll let the SoundManager release the SoundData in its Update loop once the sound has finished fading out or whatever it's doing
            SoundDataPtr->IsAutoReleaseEnabled = true;
        }
    }

    public bool TryGetOrientedBounds(out OrientedBounds orientedBounds)
    {
        // Sounds don't really have physical bounds.
        // We can update this to return a sort of bounds for the 'widget', but really
        // live objects don't have anything widget related.
        orientedBounds = default;
        return false;
    }

    public bool TryUpdate(ObjectDefinition definition, ILiveModpack? modpack)
    {
        if (definition.IsDisabled)
        {
            return false;
        }

        // If we are adding or removing a modpack or making a material change to the modpack, we can't update in place
        if ((modpack == null) != (Modpack == null)
            || (Modpack != null && modpack != null && Modpack.EffectsHash != modpack.EffectsHash))
        {
            return false;
        }

        if (definition is SoundObjectDefinition soundDefinition)
        {
            // It looks like neither the scd resource nor the sound index are meant to be changed on a playing SoundData
            var finalGamePath = soundDefinition.SoundGamePath;
            if (modpack != null)
            {
                finalGamePath = ResourceRedirectionHelpers.MakeModpackPath(finalGamePath, modpack);
            }

            if (finalGamePath != SoundGamePath || SoundDataPtr->SoundNumber != (uint)soundDefinition.SoundIndex)
            {
                return false;
            }
            else
            {
                SoundDataPtr->SetVolume(soundDefinition.Volume);
                SoundDataPtr->SetFadeInEnabled(soundDefinition.FadeInDurationSeconds > 0.0f);
                SoundDataPtr->SetFadeInDuration((uint)(MathF.Max(0.0f, soundDefinition.FadeInDurationSeconds) * 1000));
                if (soundDefinition.Speed != SoundDataPtr->Speed)
                {
                    SoundDataPtr->SetSpeed(soundDefinition.Speed, 0);
                }

                if (soundDefinition.IsPositional)
                {
                    if (!SoundDataPtr->IsPositional)
                    {
                        SoundDataPtr->GetSoundController()->SetIsNonPositional(false);
                    }
                    var position = new Vector4(soundDefinition.Position.X, soundDefinition.Position.Y, soundDefinition.Position.Z, 1.0f);
                    SoundDataPtr->GetSoundController()->SetPosition(&position);
                    SoundDataPtr->SetPosition(isPositional: true, soundDefinition.Position.X, soundDefinition.Position.Y, soundDefinition.Position.Z);
                }
                else if (!soundDefinition.IsPositional)
                {
                    if (SoundDataPtr->IsPositional)
                    {
                        SoundDataPtr->GetSoundController()->SetIsNonPositional(true);
                        var position = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
                        SoundDataPtr->GetSoundController()->SetPosition(&position);
                    }
                    SoundDataPtr->SetPosition(isPositional: false, 0.0f, 0.0f, 0.0f);
                }

                return true;
            }
        }
        else
        {
            return false;
        }
    }
}
