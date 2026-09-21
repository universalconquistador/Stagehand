using Stagehand.Editor.DefinitionEditors;
using Stagehand.Editor.DefinitionEditors.Objects;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Stagehand.Editor.Tools;

/// <summary>
/// Stores the selected objects and their relative positions from when a transform operation started.
/// </summary>
internal class TransformOperation
{
    public List<IObjectDefinitionEditor> SelectedObjects;
    public IObjectDefinitionEditor PrimarySelectedObject;
    public Vector3[] SelectedObjectStartingWorldTranslations;
    public Quaternion[] SelectedObjectStartingWorldRotations;
    public Vector3[] SelectedObjectStartingWorldScales;

    public Vector3 RefWorldTranslation;
    public Quaternion InverseRefWorldRotation;
    public Vector3 InverseRefWorldScale;

    public TransformOperation(IReadOnlyList<IDefinitionEditor> selectedEditors, IObjectDefinitionEditor primarySelectedEditor)
    {
        PrimarySelectedObject = primarySelectedEditor;
        SelectedObjects = selectedEditors.OfType<IObjectDefinitionEditor>().ToList();
        // If any of the selected objects have any ancestor selected, don't transform them as that would be a double transform.
        for (int i = 0; i < SelectedObjects.Count; i++)
        {
            var parentEditor = SelectedObjects[i].ParentObject;
            while (parentEditor != null)
            {
                if (SelectedObjects.Contains(parentEditor))
                {
                    SelectedObjects.RemoveAt(i);
                    i -= 1;
                    break;
                }

                parentEditor = parentEditor.ParentObject;
            }
        }

        RefWorldTranslation = PrimarySelectedObject.WorldPosition;
        InverseRefWorldRotation = Quaternion.Inverse(PrimarySelectedObject.WorldRotationQuaternion);
        InverseRefWorldScale = new Vector3(1.0f) / PrimarySelectedObject.WorldScale;

        SelectedObjectStartingWorldTranslations = new Vector3[SelectedObjects.Count];
        SelectedObjectStartingWorldRotations = new Quaternion[SelectedObjects.Count];
        SelectedObjectStartingWorldScales = new Vector3[SelectedObjects.Count];
        for (int i = 0; i < SelectedObjects.Count; i++)
        {
            SelectedObjectStartingWorldTranslations[i] = SelectedObjects[i].WorldPosition;
            SelectedObjectStartingWorldRotations[i] = SelectedObjects[i].WorldRotationQuaternion;
            SelectedObjectStartingWorldScales[i] = SelectedObjects[i].WorldScale;
        }
    }

    public void SetNewTranslation(Vector3 worldTranslation)
    {
        var worldDelta = worldTranslation - RefWorldTranslation;
        for (int i = 0; i < SelectedObjects.Count; i++)
        {
            SelectedObjects[i].WorldPosition = SelectedObjectStartingWorldTranslations[i] + worldDelta;
        }
    }

    public void SetNewRotation(Quaternion worldRotation)
    {
        var worldDelta = worldRotation * InverseRefWorldRotation;

        for (int i = 0; i < SelectedObjects.Count; i++)
        {
            SelectedObjects[i].WorldRotationQuaternion = worldDelta * SelectedObjectStartingWorldRotations[i];
            var startingTranslationDelta = SelectedObjectStartingWorldTranslations[i] - RefWorldTranslation;
            var newTranslationDelta = Vector3.Transform(startingTranslationDelta, worldDelta);
            SelectedObjects[i].WorldPosition = RefWorldTranslation + newTranslationDelta;
        }
    }

    public void SetNewScale(Vector3 worldScale)
    {
        // Non-uniform scaling is only possible when there is a single object selected that supports it
        if (SelectedObjects.Count == 1 && SelectedObjects[0].ScaleMode == ObjectScaleMode.NonUniform)
        {
            SelectedObjects[0].WorldScale = worldScale;
        }
        else
        {
            // Use the X channel of the scale
            var factor = worldScale.X * InverseRefWorldScale.X;

            for (var i = 0; i < SelectedObjects.Count; i++)
            {
                if (SelectedObjects[i].ScaleMode >= ObjectScaleMode.Uniform)
                {
                    SelectedObjects[i].WorldScale = SelectedObjectStartingWorldScales[i] * factor;
                    var startingDelta = SelectedObjectStartingWorldTranslations[i] - RefWorldTranslation;
                    var scaledDelta = startingDelta * factor;
                    SelectedObjects[i].WorldPosition = RefWorldTranslation + scaledDelta;
                }
            }
        }
    }
}
