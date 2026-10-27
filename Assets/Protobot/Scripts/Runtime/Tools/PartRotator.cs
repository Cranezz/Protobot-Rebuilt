using System;
using Protobot.Audio;
using Protobot.Core;
using Protobot.Menus;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools
{
    /// <summary>
    /// Rotate mode for one part (R with empty hands). The view stops and the mouse pointer comes back; drag
    /// anywhere to turn the part about one axis, shown as a coloured ring around it (X red, Y green, Z blue).
    /// R cycles the axis, Alt switches between the part's own axes (which turn with it) and the room's,
    /// Ctrl snaps to 15° steps, Esc finishes. The part hangs still while you turn it, about its middle.
    /// </summary>
    public class PartRotator : MonoBehaviour
    {
        const float SnapDegrees = 15f;
        static readonly Color[] AxisColors = { new(0.95f, 0.3f, 0.25f), new(0.35f, 0.85f, 0.35f), new(0.3f, 0.55f, 1f) };
        static readonly string[] AxisNames = { "X", "Y", "Z" };

        Rigidbody body;
        bool wasKinematic;
        Action<Rigidbody> finished;
        Camera view;
        Hud hud;
        int axis;
        bool global;
        int startFrame;

        bool dragging;
        Quaternion startRotation;
        Vector3 startPosition;
        Vector3 pivot;
        Vector3 dragPivot;
        Vector3 dragAxis;
        float lastPointerAngle;
        float turned;
        LineRenderer ring;
        LineRenderer axisLine;

        public bool IsActive => body != null;

        /// <summary>Starts turning <paramref name="part"/>; <paramref name="done"/> runs when the player finishes.</summary>
        public void Begin(Rigidbody part, Camera camera, Hud display, Action<Rigidbody> done)
        {
            body = part;
            view = camera;
            hud = display;
            finished = done;
            wasKinematic = part.isKinematic;
            part.isKinematic = true;
            axis = 1;
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
            if (body == null)
                return;
            var part = body;
            body = null;
            dragging = false;
            if (part)
            {
                part.isKinematic = wasKinematic;
                part.WakeUp();
            }
            MenuState.Close(this);
            hud.HideBanner();
            if (ring != null)
                ring.gameObject.SetActive(false);
            Sounds.Play(Sounds.Tick, 0.3f);
            finished?.Invoke(part);
        }

        void Update()
        {
            if (body == null)
                return;
            if (!body)
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
            if (keyboard.rKey.wasPressedThisFrame && !dragging && Time.frameCount != startFrame)
            {
                axis = (axis + 1) % 3;
                Sounds.Play(Sounds.Tick, 0.3f);
                ShowHelp();
            }
            if ((keyboard.leftAltKey.wasPressedThisFrame || keyboard.rightAltKey.wasPressedThisFrame) && !dragging)
            {
                global = !global;
                Sounds.Play(Sounds.Tick, 0.3f);
                ShowHelp();
            }

            if (!dragging)
                pivot = Centre();
            var worldAxis = WorldAxis();
            var pointer = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
            {
                dragging = true;
                startRotation = body.rotation;
                startPosition = body.position;
                dragPivot = pivot;
                dragAxis = worldAxis;
                lastPointerAngle = PointerAngle(pointer);
                turned = 0f;
            }
            if (dragging)
            {
                if (!mouse.leftButton.isPressed)
                {
                    dragging = false;
                }
                else
                {
                    Drag(pointer, keyboard.ctrlKey.isPressed);
                }
            }
            UpdateGizmo(dragging ? dragAxis : worldAxis);
        }

        void Drag(Vector3 pointer, bool snap)
        {
            // Circling the pointer around the part turns it the same way on screen. When the ring is seen
            // edge-on that doesn't work, so then sideways movement turns it instead.
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
            body.rotation = delta * startRotation;
            body.position = dragPivot + delta * (startPosition - dragPivot);
            body.transform.SetPositionAndRotation(body.position, body.rotation);
        }

        float PointerAngle(Vector2 pointer)
        {
            Vector2 centre = view.WorldToScreenPoint(pivot);
            var d = pointer - centre;
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        Vector3 WorldAxis()
        {
            var a = Vector3.zero;
            a[axis] = 1f;
            return global ? a : body.rotation * a;
        }

        /// <summary>Middle of the part's shape (parts' own origins are often at one end).</summary>
        Vector3 Centre()
        {
            var renderers = body.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                return body.worldCenterOfMass;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                if (!r.name.StartsWith("Tool Preview"))
                    bounds.Encapsulate(r.bounds);
            return bounds.center;
        }

        float Radius()
        {
            var renderers = body.GetComponentsInChildren<MeshRenderer>();
            var radius = 0.03f;
            foreach (var r in renderers)
                radius = Mathf.Max(radius, r.bounds.extents.magnitude);
            return Mathf.Min(radius * 0.75f + 0.01f, 0.4f);
        }

        void ShowHelp()
        {
            var color = ColorUtility.ToHtmlStringRGB(AxisColors[axis]);
            hud.ShowBanner($"ROTATE  <color=#{color}>{AxisNames[axis]} AXIS</color>  ({(global ? "room" : "part's own")})",
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
            var color = AxisColors[axis];
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
            ring.widthMultiplier = dragging ? 0.003f : 0.002f;
        }

        void OnDisable() => End();
    }
}
