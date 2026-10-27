using System;
using System.Collections.Generic;
using Protobot.Audio;
using Protobot.Building;
using Protobot.Core;
using Protobot.Menus;
using Protobot.Parts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools
{
    /// <summary>
    /// Rotate mode (R). The view stops and the mouse pointer comes back; drag anywhere to turn the part about
    /// one axis, shown as a coloured ring (X red, Y green, Z blue, a hole's axis yellow). Turning happens about
    /// a hole when there is one: the hole the part is held by, the hole you aimed at, or the hole a tacked
    /// part is placed on (then only about that hole's axis, so it stays on). R cycles the axis, Alt switches
    /// between the part's own axes and the room's, Ctrl snaps to 15° steps, Esc finishes.
    /// </summary>
    public class PartRotator : MonoBehaviour
    {
        const float SnapDegrees = 15f;
        static readonly Color[] AxisColors = { new(0.95f, 0.3f, 0.25f), new(0.35f, 0.85f, 0.35f), new(0.3f, 0.55f, 1f) };
        static readonly Color HoleColor = new(1f, 0.82f, 0.2f);
        static readonly string[] AxisNames = { "X", "Y", "Z" };

        /// <summary>What to turn, and about what.</summary>
        public struct Request
        {
            /// <summary>A whole body: a free part or an assembly.</summary>
            public Rigidbody body;
            /// <summary>Or parts inside an assembly, turned on their hinge.</summary>
            public Assembly assembly;
            public List<PartBody> parts;
            /// <summary>Pivot and axis are in this transform's space; null turns about the middle.</summary>
            public Transform pivotSpace;
            public Vector3 pivot;
            public Vector3 axis;
            public bool hasAxis;
            /// <summary>Only about the hole's axis (a tacked part stays on its hole).</summary>
            public bool hingeOnly;
            public string about;   // "about the hole it's on", for the banner
        }

        Request request;
        bool active;
        bool assemblyWasKinematic;
        bool wasKinematic;
        Action finished;
        Camera view;
        Hud hud;
        int axis;          // 0..2 = X/Y/Z, 3 = the hole's axis
        bool global;
        int startFrame;

        bool dragging;
        readonly List<(Transform t, Vector3 position, Quaternion rotation)> start = new();
        Vector3 pivot;
        Vector3 dragPivot;
        Vector3 dragAxis;
        float lastPointerAngle;
        float turned;
        LineRenderer ring;
        LineRenderer axisLine;

        public bool IsActive => active;

        /// <summary>Starts turning; <paramref name="done"/> runs when the player finishes.</summary>
        public void Begin(Request what, Camera camera, Hud display, Action done)
        {
            request = what;
            active = true;
            view = camera;
            hud = display;
            finished = done;
            if (what.body != null)
            {
                wasKinematic = what.body.isKinematic;
                what.body.isKinematic = true;
            }
            if (what.assembly != null && what.assembly.Body != null)
            {
                assemblyWasKinematic = what.assembly.Body.isKinematic;
                what.assembly.Body.isKinematic = true;
            }
            axis = what.hasAxis ? 3 : 1;
            global = false;
            dragging = false;
            startFrame = Time.frameCount;  // the R that started this isn't also "next axis"
            MenuState.Open(this);
            Sounds.Play(Sounds.Tick, 0.35f);
            BuildGizmo();
            ShowHelp();
        }

        public void End()
        {
            if (!active)
                return;
            active = false;
            dragging = false;
            var what = request;
            request = default;
            if (what.body)
            {
                what.body.isKinematic = wasKinematic;
                what.body.WakeUp();
            }
            if (what.assembly)
            {
                what.assembly.Recompute();
                if (what.assembly.Body != null)
                    what.assembly.Body.isKinematic = assemblyWasKinematic;
            }
            Physics.SyncTransforms();
            MenuState.Close(this);
            hud.HideBanner();
            if (ring != null)
                ring.gameObject.SetActive(false);
            Sounds.Play(Sounds.Tick, 0.3f);
            finished?.Invoke();
        }

        void Update()
        {
            if (!active)
                return;
            if (!StillThere())
            {
                End();
                return;
            }
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard.escapeKey.wasPressedThisFrame && MenuState.ChangedFrame != Time.frameCount)
            {
                End();
                return;
            }
            if (keyboard.rKey.wasPressedThisFrame && !dragging && Time.frameCount != startFrame && !request.hingeOnly)
            {
                axis = request.hasAxis ? (axis + 1) % 4 : (axis + 1) % 3;
                Sounds.Play(Sounds.Tick, 0.3f);
                ShowHelp();
            }
            if ((keyboard.leftAltKey.wasPressedThisFrame || keyboard.rightAltKey.wasPressedThisFrame) && !dragging && !request.hingeOnly)
            {
                global = !global;
                Sounds.Play(Sounds.Tick, 0.3f);
                ShowHelp();
            }

            if (!dragging)
                pivot = Pivot();
            var worldAxis = WorldAxis();
            var pointer = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
            {
                dragging = true;
                start.Clear();
                foreach (var t in Movers())
                    start.Add((t, t.position, t.rotation));
                dragPivot = pivot;
                dragAxis = worldAxis;
                lastPointerAngle = PointerAngle(pointer);
                turned = 0f;
            }
            if (dragging)
            {
                if (!mouse.leftButton.isPressed)
                    dragging = false;
                else
                    Drag(pointer, keyboard.ctrlKey.isPressed);
            }
            UpdateGizmo(dragging ? dragAxis : worldAxis);
        }

        bool StillThere()
        {
            if (request.body != null)
                return request.body;
            if (request.assembly == null || request.parts == null)
                return false;
            foreach (var part in request.parts)
                if (part == null || Assembly.Of(part) != request.assembly)
                    return false;
            return true;
        }

        IEnumerable<Transform> Movers()
        {
            if (request.body != null)
            {
                yield return request.body.transform;
                yield break;
            }
            foreach (var part in request.parts)
                yield return part.transform;
        }

        void Drag(Vector3 pointer, bool snap)
        {
            // Circling the pointer around the pivot turns the part the same way on screen. When the ring is
            // seen edge-on that doesn't work, so then sideways movement turns it instead.
            var facing = Vector3.Dot(dragAxis, view.transform.forward);
            float step;
            if (Mathf.Abs(facing) > 0.25f)
            {
                var angle = PointerAngle(pointer);
                step = Mathf.DeltaAngle(lastPointerAngle, angle) * (facing < 0f ? -1f : 1f);
                lastPointerAngle = angle;
            }
            else
            {
                step = Mouse.current.delta.ReadValue().x * 0.5f;
            }
            turned += step;
            var applied = snap ? Mathf.Round(turned / SnapDegrees) * SnapDegrees : turned;
            var delta = Quaternion.AngleAxis(applied, dragAxis);
            foreach (var (t, position, rotation) in start)
            {
                var newRotation = delta * rotation;
                var newPosition = dragPivot + delta * (position - dragPivot);
                if (request.body != null && t == request.body.transform)
                {
                    request.body.rotation = newRotation;
                    request.body.position = newPosition;
                }
                t.SetPositionAndRotation(newPosition, newRotation);
            }
            Physics.SyncTransforms();
        }

        float PointerAngle(Vector2 pointer)
        {
            Vector2 centre = view.WorldToScreenPoint(pivot);
            var d = pointer - centre;
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        Vector3 WorldAxis()
        {
            if (axis == 3)
                return request.pivotSpace.TransformDirection(request.axis).normalized;
            var a = Vector3.zero;
            a[axis] = 1f;
            if (global)
                return a;
            var frame = request.body != null ? request.body.transform : request.parts[0].transform;
            return frame.rotation * a;
        }

        Vector3 Pivot() => request.pivotSpace != null ? request.pivotSpace.TransformPoint(request.pivot) : Centre();

        /// <summary>Middle of the shape (parts' own origins are often at one end).</summary>
        Vector3 Centre()
        {
            var first = true;
            var bounds = new Bounds();
            foreach (var t in Movers())
            {
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.name.StartsWith(PartShape.PreviewName))
                        continue;
                    if (first)
                        bounds = r.bounds;
                    else
                        bounds.Encapsulate(r.bounds);
                    first = false;
                }
            }
            return first ? (request.body != null ? request.body.worldCenterOfMass : Vector3.zero) : bounds.center;
        }

        float Radius()
        {
            var radius = 0.03f;
            foreach (var t in Movers())
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
                    radius = Mathf.Max(radius, r.bounds.extents.magnitude);
            // A hole at one end of a long part: the ring stays a handy size around the hole.
            return Mathf.Min(radius * 0.75f + 0.01f, request.pivotSpace != null ? 0.12f : 0.4f);
        }

        void ShowHelp()
        {
            var about = request.about ?? (request.pivotSpace != null ? "about the hole" : "about its middle");
            if (axis == 3)
            {
                var color = ColorUtility.ToHtmlStringRGB(HoleColor);
                hud.ShowBanner($"ROTATE  <color=#{color}>HOLE AXIS</color>  ({about})",
                    request.hingeOnly
                        ? "Drag to turn it on its hole   ·   Ctrl: 15° steps   ·   Esc: done"
                        : "Drag to turn   ·   R: next axis   ·   Ctrl: 15° steps   ·   Esc: done");
                return;
            }
            var axisColor = ColorUtility.ToHtmlStringRGB(AxisColors[axis]);
            hud.ShowBanner($"ROTATE  <color=#{axisColor}>{AxisNames[axis]} AXIS</color>  ({(global ? "room" : "part's own")}, {about})",
                "Drag to turn   ·   R: next axis   ·   Alt: part / room axes   ·   Ctrl: 15° steps   ·   Esc: done");
        }

        void BuildGizmo()
        {
            if (ring == null)
            {
                ring = NewLine("Rotate Ring", 64, true);
                axisLine = NewLine("Rotate Axis", 2, false);
                axisLine.transform.SetParent(ring.transform, false);
            }
            ring.gameObject.SetActive(true);
        }

        LineRenderer NewLine(string name, int points, bool loop)
        {
            var go = new GameObject(name);
            if (ring != null)
                go.transform.SetParent(ring.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = points;
            line.widthMultiplier = 0.002f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        void UpdateGizmo(Vector3 worldAxis)
        {
            var color = axis == 3 ? HoleColor : AxisColors[axis];
            var material = ToolMaterials.Unlit(color);
            ring.sharedMaterial = material;
            axisLine.sharedMaterial = material;
            var radius = Radius();
            var u = Vector3.Cross(worldAxis, Mathf.Abs(worldAxis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(worldAxis, u);
            for (var i = 0; i < ring.positionCount; i++)
            {
                var a = i * Mathf.PI * 2f / ring.positionCount;
                ring.SetPosition(i, pivot + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius);
            }
            axisLine.SetPosition(0, pivot - worldAxis * radius * 1.2f);
            axisLine.SetPosition(1, pivot + worldAxis * radius * 1.2f);
            // Thin lines up close, so the ring doesn't hide a small part.
            var width = Mathf.Clamp(Vector3.Distance(view.transform.position, pivot) * 0.004f, 0.0006f, 0.003f);
            ring.widthMultiplier = dragging ? width * 1.5f : width;
            axisLine.widthMultiplier = width;
        }

        void OnDisable() => End();
    }
}
