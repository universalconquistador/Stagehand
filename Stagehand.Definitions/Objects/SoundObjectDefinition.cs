using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Definitions.Objects;

/// <summary>
/// The definition of a sound object in a Stage definition.
/// </summary>
public class SoundObjectDefinition : ObjectDefinition
{
    /// <summary>
    /// The game path of the .scd resource to play.
    /// </summary>
    public string SoundGamePath { get; set; } = "";

    /// <summary>
    /// The index of the Sound in the sound resource to play.
    /// </summary>
    public int SoundIndex { get; set; } = 0;

    /// <summary>
    /// How loud to play the sound, with 1.0 being the original volume of the sound.
    /// </summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// How long the sound should fade in for, or 0 to not fade in.
    /// </summary>
    public float FadeInDurationSeconds { get; set; } = 0.0f;

    /// <summary>
    /// The speed to play the sound at, with 1.0 being the original speed of the sound.
    /// </summary>
    public float Speed { get; set; } = 1.0f;

    /// <summary>
    /// Whether to play the sound as though it is coming from the location of this sound object.
    /// </summary>
    public bool IsPositional { get; set; } = true;

    /// <inheritdoc />
    public override ObjectDefinition Clone()
    {
        var result = new SoundObjectDefinition();
        CopyTo(result);
        return result;
    }

    /// <inheritdoc />
    public override void CopyTo(ObjectDefinition other)
    {
        base.CopyTo(other);

        if (other is SoundObjectDefinition otherSound)
        {
            otherSound.SoundGamePath = SoundGamePath;
            otherSound.Volume = Volume;
            otherSound.FadeInDurationSeconds = FadeInDurationSeconds;
            otherSound.Speed = Speed;
            otherSound.SoundIndex = SoundIndex;
        }
    }

    /// <inheritdoc />
    public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
    {
        return TVisitor.VisitSoundObjectDefinition(this, ref param);
    }
}
