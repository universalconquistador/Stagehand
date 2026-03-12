using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using System.Numerics;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Stagehand.Live;

internal abstract unsafe class LiveDrawObject : LiveObject
{
    protected DrawObject* DrawObjectPtr => (DrawObject*)ObjectPtr;

    public override Vector3 Position { get => base.Position; set { base.Position = value; DrawObjectPtr->NotifyTransformChanged(); } }
    public override Quaternion Rotation { get => base.Rotation; set { base.Rotation = value; DrawObjectPtr->NotifyTransformChanged(); } }
    public override Vector3 Scale { get => base.Scale; set { base.Scale = value; DrawObjectPtr->NotifyTransformChanged(); } }

    public bool IsVisible { get => DrawObjectPtr->IsVisible; set => DrawObjectPtr->IsVisible = value; }
    public bool IsCoveredFromRain { get => DrawObjectPtr->IsCoveredFromRain; set => DrawObjectPtr->IsCoveredFromRain = value; }

    public LiveDrawObject(DrawObject* drawObjectPtr)
        : base((Object*)drawObjectPtr)
    { }
}
