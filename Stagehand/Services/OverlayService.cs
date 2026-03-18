using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Stagehand.Windows;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Services;

/// <summary>
/// Provides methods used to draw 3D overlays over the FFXIV scene.
/// </summary>
public interface IOverlayDrawContext
{
    /// <summary>
    /// Draws a 3D circle around the given center point on the plane formed by the two given axes.
    /// </summary>
    /// <param name="centerPoint">The center point of the circle, in 3D space.</param>
    /// <param name="axisOne">The first axis of the plane to draw the circle on.</param>
    /// <param name="axisTwo">The second axis of the plane to draw the circle on.</param>
    /// <param name="radius">The radius of the circle, in world space.</param>
    /// <param name="thickness">The thickness to draw the circle, in pixels.</param>
    /// <param name="color">The color to draw the circle.</param>
    void DrawCircle(Vector3 centerPoint, Vector3 axisOne, Vector3 axisTwo, float radius, float thickness, Vector4 color);

    /// <summary>
    /// Draws a 3D line from the given start point to the given end point.
    /// </summary>
    /// <param name="startPoint">The starting point of the line, in 3D space.</param>
    /// <param name="endPoint">The end point of the line, in 3D space.</param>
    /// <param name="thickness">The thickness to draw the line, in pixels.</param>
    /// <param name="color">The color to draw the line.</param>
    void DrawLine(Vector3 startPoint, Vector3 endPoint, float thickness, Vector4 color);

    /// <summary>
    /// Draws a 3D cross with the given transform.
    /// </summary>
    /// <param name="transform">The transform to draw the cross at.</param>
    /// <param name="thickness">The thickness to draw the cross lines with, in pixels.</param>
    /// <param name="color">The color to draw the cross.</param>
    void DrawCross(Matrix4x4 transform, float thickness, Vector4 color);

    /// <summary>
    /// Draws a box in 3D space with the given transform and half extents.
    /// </summary>
    /// <param name="transform">The transform of the box.</param>
    /// <param name="halfExtents">Half of the size of the box along its X, Y, and Z axes.</param>
    /// <param name="thickness">The thickness to draw the box lines with, in pixels.</param>
    /// <param name="color">The color to draw the box.</param>
    void DrawBox(Matrix4x4 transform, Vector3 halfExtents, float thickness, Vector4 color);

    /// <summary>
    /// Draws an interactive gizmo for manipulating the given transform.
    /// </summary>
    /// <param name="id">A unique identifier for this gizmo.</param>
    /// <param name="translation">The translation of the transform.</param>
    /// <param name="rotation">The rotation of the transform.</param>
    /// <param name="scale">The scale of the transform.</param>
    /// <param name="operation">The kind of gizmo to draw.</param>
    /// <param name="mode">Whether the gizmo is oriented in local or world space.</param>
    /// <returns>Whether the given transform components were modified.</returns>
    bool DrawGizmo(ImU8String id, ref Vector3 translation, ref Quaternion rotation, ref Vector3 scale, ImGuizmoOperation operation, ImGuizmoMode mode);
}

/// <summary>
/// Provides a callback that can be used for drawing 3D overlays over the FFXIV scene.
/// </summary>
public interface IOverlayService
{
    /// <summary>
    /// Raised when it is time to draw overlays, with the context to be used for drawing.
    /// </summary>
    event Action<IOverlayDrawContext> DrawOverlays;

    /// <summary>
    /// Whether the overlay is hit testable.
    /// </summary>
    /// <remarks>
    /// This is a HACK.
    /// </remarks>
    bool IsPicking { get; set; }
}

internal class OverlayService : IOverlayService
{
    private class OverlayDrawContext : IOverlayDrawContext
    {
        private readonly IGameGui _gameGui;

        public ImDrawListPtr DrawListPtr { get; set; }
        public Matrix4x4 ViewMatrix;
        public Matrix4x4 ProjectionMatrix;

        public OverlayDrawContext(IGameGui gameGui)
        {
            _gameGui = gameGui;
        }

        public void DrawBox(Matrix4x4 transform, Vector3 halfExtents, float thickness, Vector4 color)
        {
            //// Normalize the transform's scale
            //transform = transform.WithRow(0, new Vector4(Vector3.Normalize(transform.GetRow(0).AsVector3()), 0.0f));
            //transform = transform.WithRow(1, new Vector4(Vector3.Normalize(transform.GetRow(1).AsVector3()), 0.0f));
            //transform = transform.WithRow(2, new Vector4(Vector3.Normalize(transform.GetRow(2).AsVector3()), 0.0f));

            Span<Vector3> corners = stackalloc Vector3[]
            {
                halfExtents,
                new Vector3(halfExtents.X, halfExtents.Y, -halfExtents.Z),
                new Vector3(halfExtents.X, -halfExtents.Y, -halfExtents.Z),
                new Vector3(halfExtents.X, -halfExtents.Y, halfExtents.Z),

                new Vector3(-halfExtents.X, halfExtents.Y, halfExtents.Z),
                new Vector3(-halfExtents.X, halfExtents.Y, -halfExtents.Z),
                -halfExtents,
                new Vector3(-halfExtents.X, -halfExtents.Y, halfExtents.Z),
            };

            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = Vector3.Transform(corners[i], transform);
            }

            Span<Vector2> screenCorners = stackalloc Vector2[corners.Length];
            Span<bool> isInView = stackalloc bool[corners.Length];

            for (int i = 0; i < corners.Length; i++)
            {
                isInView[i] = _gameGui.WorldToScreen(corners[i], out screenCorners[i], out _);
            }

            Span<int> indices = stackalloc int[]
            {
                0, 1,
                1, 2,
                2, 3,
                3, 0,

                4, 5,
                5, 6,
                6, 7,
                7, 4,

                0, 4,
                1, 5,
                2, 6,
                3, 7,
            };

            for (int i = 0; i < indices.Length; i += 2)
            {
                if (isInView[indices[i]] && isInView[indices[i + 1]])
                {
                    DrawListPtr.AddLine(screenCorners[indices[i]], screenCorners[indices[i + 1]], ImGui.GetColorU32(color), thickness);
                }
            }
        }

        public void DrawCircle(Vector3 centerPoint, Vector3 axisOne, Vector3 axisTwo, float radius, float thickness, Vector4 color)
        {
            int segmentCount = 32;

            for (int i = 0; i <= segmentCount; i++)
            {
                int endIndex = i + 1;
                float angleRads = ((float)i / segmentCount) * MathF.PI * 2.0f;

                var startPoint = centerPoint + (MathF.Cos(angleRads) * axisOne + MathF.Sin(angleRads) * axisTwo) * radius;

                var isVisible = _gameGui.WorldToScreen(startPoint, out Vector2 startPointScreenPos, out _);

                if (isVisible)
                {
                    DrawListPtr.PathLineTo(startPointScreenPos);
                }
            }

            DrawListPtr.PathStroke(ImGui.GetColorU32(color), thickness);
            DrawListPtr.PathClear();
        }

        public void DrawCross(Matrix4x4 transform, float thickness, Vector4 color)
        {
            DrawLine(transform.Translation - transform.X.AsVector3(), transform.Translation + transform.X.AsVector3(), thickness, color);
            DrawLine(transform.Translation - transform.Y.AsVector3(), transform.Translation + transform.Y.AsVector3(), thickness, color);
            DrawLine(transform.Translation - transform.Z.AsVector3(), transform.Translation + transform.Z.AsVector3(), thickness, color);
        }

        public void DrawLine(Vector3 startPoint, Vector3 endPoint, float thickness, Vector4 color)
        {
            bool startInView = _gameGui.WorldToScreen(startPoint, out var startPointScreenPos, out _);
            bool endInView = _gameGui.WorldToScreen(endPoint, out var endPointScreenPos, out _);

            if (startInView && endInView)
            {
                DrawListPtr.AddLine(startPointScreenPos, endPointScreenPos, ImGui.GetColorU32(color), thickness);
            }
        }

        public unsafe bool DrawGizmo(ImU8String id, ref Vector3 translation, ref Quaternion rotation, ref Vector3 scale, ImGuizmoOperation operation, ImGuizmoMode mode)
        {
            bool result = false;

            ImGuizmo.SetID((int)ImGui.GetID(id));
            ImGuizmo.Enable(true);

            Matrix4x4 translationMatrix = Matrix4x4.CreateTranslation(translation);
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(rotation);
            Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(scale);
            Matrix4x4 matrix = new Matrix4x4();

            matrix = scaleMatrix * rotationMatrix * translationMatrix;

            Vector3 snap = Vector3.Zero;

            result = ImGuizmo.Manipulate(ref ViewMatrix.M11, ref ProjectionMatrix.M11, operation, mode, ref matrix.M11);
            if (result)
            {
                Vector3 rotationXYZ = Vector3.Zero;
                Matrix4x4.Decompose(matrix, out scale, out rotation, out translation);
            }

            ImGuizmo.SetID(-1);

            return result;
        }
    }

    private readonly IGameGui _gameGui;
    // Drawing is not reentrant and only happens on one thread, so reuse the same draw context each time
    private readonly OverlayDrawContext _recycledDrawContext;

    public event Action<IOverlayDrawContext>? DrawOverlays;

    public bool IsPicking { get; set; }

    public OverlayService(IGameGui gameGui)
    {
        _gameGui = gameGui;
        _recycledDrawContext = new OverlayDrawContext(gameGui);
    }

    public unsafe void Draw()
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
        {
            ImGuiHelpers.ForceNextWindowMainViewport();
            ImGuiHelpers.SetNextWindowPosRelativeMainViewport(new Vector2(0, 0));

            ImGui.Begin("Stagehand Overlay", 
                (IsPicking ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoInputs)
                | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);

            var displaySize = ImGui.GetIO().DisplaySize;
            ImGui.SetWindowSize(displaySize);

            // Set up the ImGuizmo matrices
            var renderCamera = CameraManager.Instance()->CurrentCamera->RenderCamera;

            var projection = renderCamera->ProjectionMatrix;
            var view = renderCamera->ViewMatrix;

            // Yoinked with love from Burning Down the House
            var far = renderCamera->FarPlane;
            var near = renderCamera->NearPlane;
            var clip = far / (far - near);

            projection.M43 = -(clip * near);
            projection.M33 = -((far + near) / (far - near));
            view.M44 = 1.0f;

            _recycledDrawContext.ViewMatrix = view;
            _recycledDrawContext.ProjectionMatrix = projection;

            var drawList = ImGui.GetBackgroundDrawList();
            ImGuizmo.SetDrawlist(drawList);
            ImGuizmo.SetOrthographic(renderCamera->IsOrtho);
            ImGuizmo.SetRect(ImGui.GetWindowViewport().Pos.X, ImGui.GetWindowViewport().Pos.Y, ImGui.GetWindowViewport().Size.X, ImGui.GetWindowViewport().Size.Y);
            ImGuizmo.BeginFrame();
            _recycledDrawContext.DrawListPtr = drawList;
            DrawOverlays?.Invoke(_recycledDrawContext);

            ImGui.End();
        }
    }
}
