using System.Collections.Generic;
using Protobot.Audio;
using Protobot.Building;
using Protobot.Core;
using Protobot.Interaction;
using Protobot.Parts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools
{
    /// <summary>
    /// Empty hands. Click a part to pick it up, click again to drop it. A held part is pulled towards a point
    /// in front of the camera with physics, so it still bumps into things; let go and it falls (or flies, if you
    /// swung it). While holding: the mouse wheel moves it closer or further, holding the wheel down turns it
    /// freely with the mouse, right click gives it a quarter turn, and F freezes it in mid air (F on a frozen
    /// part, or picking it up, unfreezes it). R opens rotate mode for the held part or the one you aim at.
    ///
    /// Building: the hole nearest where you grabbed a part is the one it attaches by (Q/E pick another). Aim at
    /// a hole on another part and the held part snaps there, faces flush and the two hole grids lined up; the
    /// wheel turns it about the hole, right click flips it over, click places it. A placed part is tacked on
    /// (it stays, but pulls off when you grab it) until a screw and nut fasten it. Hold a screw over a hole to
    /// seat it and click to put it in; hold a nut over a screw to thread it on. Fastened parts are one piece:
    /// grabbing any of them picks up the lot (Shift + click always picks up the whole assembly).
    /// </summary>
    public class HandsTool : PlayerTool
    {
        [SerializeField] Transform head;

        [Header("Holding")]
        [SerializeField] float minHoldDistance = 0.25f;
        [SerializeField] float maxHoldDistance = 1.6f;
        [SerializeField] float scrollStep = 0.04f;
        [Tooltip("How quickly the part follows the cursor (higher = snappier).")]
        [SerializeField] float followStrength = 0.5f;
        [SerializeField] float maxCarrySpeed = 5f;
        [SerializeField] float maxTurnSpeed = 15f;
        [Tooltip("If something blocks the part this far from where it should be, it is dropped.")]
        [SerializeField] float breakDistance = 0.8f;
        [Tooltip("Degrees the held part turns per pixel of mouse movement while the wheel is held down.")]
        [SerializeField] float freeTurnSpeed = 0.35f;

        [Header("Building")]
        [SerializeField] float snapReach = 2.2f;
        [Tooltip("How close to a hole the aim has to be for a part to snap to it (m).")]
        [SerializeField] float snapRadius = 0.012f;

        static readonly Color TargetColor = new(1f, 0.82f, 0.2f);
        static readonly Color AnchorColor = new(0.3f, 0.85f, 1f);

        enum SnapKind { None, Part, Screw, Nut }

        Rigidbody held;
        HoverTarget heldTarget;
        Vector3 localGrabPoint;
        Quaternion rotationFromView;
        float holdDistance;
        bool heldUsedGravity;
        float heldDrag;
        float heldAngularDrag;
        bool freeTurning;
        PartRotator rotator;

        // Building
        PartBody heldPart;
        PartRole heldRole;
        readonly List<Hole> anchors = new();   // the held part's holes, in the held body's space
        int anchorIndex = -1;
        Vector3 heldCentre;                    // middle of the held part, in the held body's space
        HoleMarker targetMarker, anchorMarker;
        SnapKind snapKind;
        PartBody snapPart;
        ScrewJoint snapScrew;
        Vector3 snapPosition;
        Quaternion snapRotation;
        bool snapFlip;
        float snapTwist;
        string snapProblem;
        readonly List<Collider> triggered = new();

        public bool IsHolding => held != null;
        bool Snapped => snapKind != SnapKind.None;

        public override string ControlsHint => !IsHolding
            ? "Left click: pick up (Shift: whole assembly)   ·   R: rotate a part   ·   F: unfreeze a frozen part"
            : snapKind == SnapKind.Part
                ? "Left click: place   ·   Wheel: turn 45° (Shift: 15°)   ·   Right click: flip   ·   Q/E: other hole   ·   F: freeze"
                : snapKind == SnapKind.Screw ? "Left click: put the screw in"
                : snapKind == SnapKind.Nut ? "Left click: thread the nut on"
                : "Left click: drop   ·   Wheel: closer / further   ·   Hold wheel: turn   ·   Q/E: hole to attach by   ·   R: rotate mode   ·   F: freeze   ·   Right click: 90°";

        public override void PrimaryDown()
        {
            if (held != null)
            {
                if (Snapped)
                {
                    Place();
                    return;
                }
                if (snapProblem != null)
                {
                    Hud.ShowError(snapProblem);
                    return;
                }
                Release();
                Sounds.Play(Sounds.Release, 0.4f);
                return;
            }
            var target = Cursor.Current;
            if (target == null || !Cursor.CurrentInReach)
                return;
            if (target.Cursor != CursorKind.Grab)
                return;
            var part = target.GetComponentInParent<PartBody>();
            if (part != null && Assembly.Of(part) != null && !Keyboard.current.shiftKey.isPressed)
                TakeOff(part);
            if (target.GetComponentInParent<Rigidbody>() is not { } body)
                return;
            if (body.isKinematic)
                Unfreeze(body, false);  // picking up a frozen part unfreezes it
            Grab(body, target, Cursor.CurrentHit.point, Cursor.CurrentHit.distance);
            Sounds.Play(Sounds.Pickup, 0.45f);
        }

        /// <summary>
        /// Grabbing a part that's in an assembly: a nut unscrews, a screw without a nut comes out, a tacked part
        /// pulls off. A fastened part stays, and the whole assembly is picked up.
        /// </summary>
        void TakeOff(PartBody part)
        {
            if (FindNutJoint(part) is { } nutJoint)
            {
                nutJoint.RemoveNut();
                Hud.ShowToast("Nut off: the screw only holds the parts lined up now", 2f);
                return;
            }
            if (part.TryGetComponent(out ScrewJoint screw))
            {
                if (screw.Nut != null)
                    return;  // take the nut off first; meanwhile it's part of the assembly
                screw.Remove();
                Hud.ShowToast("Screw out", 1.2f);
                return;
            }
            if (Assembly.Detach(part))
                Hud.ShowToast("Pulled off", 1f);
        }

        static ScrewJoint FindNutJoint(PartBody nut)
        {
            var assembly = Assembly.Of(nut);
            if (assembly == null)
                return null;
            foreach (var part in assembly.Parts())
                if (part.TryGetComponent(out ScrewJoint joint) && joint.Nut == nut)
                    return joint;
            return null;
        }

        public override void OnUnequip()
        {
            Release();
            HideMarkers();
        }

        public override void OnSuspend()
        {
            Release();
            HideMarkers();
        }

        public override void Scroll(int notches)
        {
            if (held == null)
                return;
            if (snapKind == SnapKind.Part)
            {
                snapTwist += notches * (Keyboard.current.shiftKey.isPressed ? 15f : 45f);
                Sounds.Play(Sounds.Tick, 0.25f);
                return;
            }
            holdDistance = Mathf.Clamp(holdDistance + notches * scrollStep, minHoldDistance, maxHoldDistance);
        }

        public override void SecondaryTap()
        {
            if (held == null)
                return;
            if (snapKind == SnapKind.Part)
                snapFlip = !snapFlip;
            else
                rotationFromView = Quaternion.Euler(0f, 90f, 0f) * rotationFromView;
            Sounds.Play(Sounds.Tick, 0.3f);
        }

        public override void Tick()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            // Hold the wheel down to turn the held part with the mouse (the view stays still meanwhile).
            var turning = held != null && mouse.middleButton.isPressed;
            if (turning != freeTurning)
            {
                freeTurning = turning;
                InputLocks.LookSuppressed = turning;
            }
            if (turning)
            {
                var delta = mouse.delta.ReadValue() * freeTurnSpeed;
                rotationFromView = Quaternion.AngleAxis(-delta.x, Vector3.up) * Quaternion.AngleAxis(delta.y, Vector3.right) * rotationFromView;
            }

            if (held != null && anchors.Count > 1 && (keyboard.qKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame))
            {
                anchorIndex = (anchorIndex + (keyboard.eKey.wasPressedThisFrame ? 1 : anchors.Count - 1)) % anchors.Count;
                Sounds.Play(Sounds.Tick, 0.3f);
            }

            if (keyboard.fKey.wasPressedThisFrame)
                ToggleFreeze();
            if (keyboard.rKey.wasPressedThisFrame)
                StartRotateMode();

            if (held != null)
                UpdateSnap();
            else
                ShowHoveredHole();
        }

        public override bool DescribeTarget(HoverTarget target, bool inReach, ref CursorView view)
        {
            if (target == null || !inReach || held != null)
                return false;
            var part = target.GetComponentInParent<PartBody>();
            if (part == null)
                return false;
            if (Assembly.Of(part) != null)
            {
                view.hint = (string.IsNullOrEmpty(view.hint) ? "" : view.hint + "   ·   ")
                    + (part.TryGetComponent(out ScrewJoint screw) ? screw.Nut != null ? "Screw (take the nut off first)" : "Click: take the screw out"
                        : FindNutJoint(part) != null ? "Click: unscrew the nut"
                        : Assembly.IsFastened(part) ? "Fastened: click picks up the whole assembly" : "Tacked on: click pulls it off");
                return true;
            }
            if (target.GetComponentInParent<Rigidbody>() is not { isKinematic: true })
                return false;
            view.hint = (string.IsNullOrEmpty(view.hint) ? "" : view.hint + "   ·   ") + "Frozen in place (F unfreezes)";
            return true;
        }

        // ---------------------------------------------------------------- building

        /// <summary>Not holding anything: outline the hole under the aim, so holes read as things you can use.</summary>
        void ShowHoveredHole()
        {
            var target = Cursor.Current;
            if (target != null && Cursor.CurrentInReach && target.GetComponentInParent<PartHoles>() is { } holes
                && holes.TryNearest(Cursor.CurrentHit.point, snapRadius * 0.7f, out var hole))
            {
                HoleSnap.NearFace(hole, head.position, out var face, out var outward);
                Marker(ref targetMarker, "Hole Marker", TargetColor).Show(hole, face, outward);
            }
            else
            {
                targetMarker?.Hide();
            }
            anchorMarker?.Hide();
        }

        /// <summary>While holding: find what the held thing would go on, and put it there (kinematically).</summary>
        void UpdateSnap()
        {
            snapProblem = null;
            var kind = SnapKind.None;
            HoleAt targetHole = default;
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            ScrewJoint screw = null;
            var hand = ViewYaw() * rotationFromView;

            if (!freeTurning && FindTarget(out var part, out var hit))
            {
                var holes = part.GetComponent<PartHoles>();
                if (PartRoles.IsScrew(heldRole) && heldPart.TryGetComponent(out Fastener screwData))
                {
                    if (holes != null && holes.TryNearest(hit.point, snapRadius, out targetHole))
                    {
                        HoleSnap.PlaceScrew(screwData, hand, targetHole, head.position, out position, out rotation);
                        kind = SnapKind.Screw;
                    }
                }
                else if (heldRole == PartRole.Nut && heldPart.TryGetComponent(out Fastener nutData))
                {
                    screw = FindScrew(hit.point);
                    if (screw != null && screw.TryGetComponent(out Fastener onScrew))
                    {
                        if (screw.Nut != null)
                            snapProblem = "That screw already has a nut";
                        else if (!screw.FitsNut(nutData.Length))
                            snapProblem = "That screw is too short for a nut: use a longer screw";
                        else
                        {
                            HoleSnap.PlaceNut(nutData, hand, onScrew, screw.StackEnd, out position, out rotation);
                            targetHole = new HoleAt { part = screw.GetComponent<PartBody>(), center = onScrew.WorldSeat + onScrew.WorldAxis * screw.StackEnd, axis = onScrew.WorldAxis, local = new Hole { size = nutData.Diameter } };
                            kind = SnapKind.Nut;
                        }
                    }
                }
                else if (anchorIndex >= 0 && holes != null && holes.TryNearest(hit.point, snapRadius, out targetHole))
                {
                    HoleSnap.PlacePart(anchors[anchorIndex], heldCentre, hand, targetHole, head.position, snapFlip, snapTwist, 45f, out position, out rotation);
                    kind = SnapKind.Part;
                }
                snapPart = part;
            }

            if (kind == SnapKind.None)
            {
                EndSnap();
                targetMarker?.Hide();
                ShowAnchor();
                Cursor.HoldingHint = snapProblem;
                return;
            }
            if (!Snapped)
                Sounds.Play(Sounds.Tick, 0.35f);
            BeginSnap();
            snapKind = kind;
            snapScrew = screw;
            snapPosition = position;
            snapRotation = rotation;
            if (kind == SnapKind.Nut)
                Marker(ref targetMarker, "Hole Marker", TargetColor).Show(targetHole, targetHole.center, -targetHole.axis);
            else
            {
                HoleSnap.NearFace(targetHole, head.position, out var face, out var outward);
                Marker(ref targetMarker, "Hole Marker", TargetColor).Show(targetHole, face, outward);
            }
            anchorMarker?.Hide();
            Cursor.HoldingHint = kind == SnapKind.Part ? "Click: place it here   ·   Wheel: turn   ·   Right click: flip"
                : kind == SnapKind.Screw ? "Click: put the screw in" : "Click: thread the nut on";
        }

        /// <summary>The first part along the aim that isn't what's in the hand.</summary>
        bool FindTarget(out PartBody part, out RaycastHit hit)
        {
            part = null;
            hit = default;
            var hits = Physics.RaycastAll(head.position, head.forward, snapReach, Cursor.RaycastMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.rigidbody != null && h.rigidbody == held)
                    continue;
                part = h.collider.GetComponentInParent<PartBody>();
                hit = h;
                return part != null;
            }
            return false;
        }

        /// <summary>
        /// An inserted screw on the aim line (even behind the first thing it hits, e.g. a flange), or the one
        /// closest to the aim line near where it hits (aiming at the hole a screw sticks out of).
        /// </summary>
        ScrewJoint FindScrew(Vector3 point)
        {
            var hits = Physics.RaycastAll(head.position, head.forward, snapReach, Cursor.RaycastMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.rigidbody != null && h.rigidbody == held)
                    continue;
                if (h.collider.GetComponentInParent<PartBody>() is { } part && part.TryGetComponent(out ScrewJoint onLine))
                    return onLine;
            }
            ScrewJoint best = null;
            var bestDistance = 0.012f;
            foreach (var collider in Physics.OverlapSphere(point, 0.03f, ~0, QueryTriggerInteraction.Ignore))
            {
                var joint = collider.GetComponentInParent<ScrewJoint>();
                if (joint == null || !joint.TryGetComponent(out Fastener fastener))
                    continue;
                // Closest the screw comes to the aim line (sampled along the screw).
                for (var i = 0; i <= 4; i++)
                {
                    var p = fastener.WorldSeat + fastener.WorldAxis * (fastener.Length * i / 4f);
                    var toPoint = p - head.position;
                    var distance = (toPoint - head.forward * Vector3.Dot(toPoint, head.forward)).magnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = joint;
                    }
                }
            }
            return best;
        }

        void ShowAnchor()
        {
            if (anchorIndex < 0 || held == null || heldRole == PartRole.Nut || PartRoles.IsScrew(heldRole))
            {
                anchorMarker?.Hide();
                return;
            }
            var anchor = anchors[anchorIndex];
            var hole = new HoleAt
            {
                part = heldPart, local = anchor,
                center = held.transform.TransformPoint(anchor.center), axis = held.transform.TransformDirection(anchor.axis).normalized,
            };
            HoleSnap.NearFace(hole, head.position, out var face, out var outward);
            Marker(ref anchorMarker, "Anchor Marker", AnchorColor).Show(hole, face, outward);
        }

        /// <summary>Snapped: the held thing goes exactly where it'll be placed, and passes through what it touches.</summary>
        void BeginSnap()
        {
            if (Snapped || held == null)
                return;
            triggered.Clear();
            foreach (var c in held.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger)
                    continue;
                c.isTrigger = true;
                triggered.Add(c);
            }
            held.isKinematic = true;
        }

        void EndSnap()
        {
            if (!Snapped)
                return;
            snapKind = SnapKind.None;
            foreach (var c in triggered)
                if (c != null)
                    c.isTrigger = false;
            triggered.Clear();
            if (held != null)
            {
                held.isKinematic = false;
                held.WakeUp();
            }
        }

        void Place()
        {
            var kind = snapKind;
            var part = heldPart;
            var target = snapPart;
            var screw = snapScrew;
            var body = held;
            var position = snapPosition;
            var rotation = snapRotation;
            Release();
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();

            switch (kind)
            {
                case SnapKind.Part:
                    Assembly.Join(part, target, LinkKind.Tack);
                    Sounds.Play(Sounds.Clack, 0.45f);
                    Hud.ShowToast("Tacked on: it stays put until you screw it (grab it to pull it off)", 2.5f);
                    break;
                case SnapKind.Screw:
                    var joint = ScrewJoint.Insert(part);
                    if (joint == null)
                    {
                        Assembly.Join(part, target, LinkKind.Tack);
                        Hud.ShowToast("Screw in", 1.2f);
                    }
                    else
                    {
                        Hud.ShowToast(joint.Fastened
                            ? $"Screwed into a threaded hole: {joint.StackParts.Count} parts fastened"
                            : $"Screw through {joint.StackParts.Count} {(joint.StackParts.Count == 1 ? "part" : "parts")}: thread a nut on the end to fasten", 2.5f);
                    }
                    Sounds.Play(Sounds.Drill, 0.25f);
                    break;
                case SnapKind.Nut:
                    screw.AddNut(part);
                    Sounds.Play(Sounds.Drill, 0.3f);
                    Hud.ShowSuccess($"Fastened: {screw.StackParts.Count + 2} parts are one piece now");
                    break;
            }
        }

        HoleMarker Marker(ref HoleMarker marker, string name, Color color) => marker ??= new HoleMarker(name, color);

        void HideMarkers()
        {
            targetMarker?.Hide();
            anchorMarker?.Hide();
        }

        /// <summary>The held part's holes in the held body's space, and the one nearest the grab point.</summary>
        void PrepareAnchors(Rigidbody body, Vector3 grabPoint)
        {
            anchors.Clear();
            anchorIndex = -1;
            snapFlip = false;
            snapTwist = 0f;
            heldPart = heldTarget != null ? heldTarget.GetComponentInParent<PartBody>() : body.GetComponentInParent<PartBody>();
            heldRole = heldPart != null ? PartRoles.Of(heldPart.PartId) : PartRole.Other;
            if (heldPart == null || !heldPart.TryGetComponent(out PartHoles holes))
                return;
            heldCentre = body.transform.InverseTransformPoint(heldPart.transform.TransformPoint(PartShape.LocalBounds(heldPart.gameObject).center));
            var best = float.MaxValue;
            foreach (var hole in holes.Current())
            {
                var inBody = hole;
                inBody.center = body.transform.InverseTransformPoint(heldPart.transform.TransformPoint(hole.center));
                inBody.axis = body.transform.InverseTransformDirection(heldPart.transform.TransformDirection(hole.axis)).normalized;
                anchors.Add(inBody);
            }
            // In reading order (along the part's rows), so Q/E step to neighbouring holes.
            anchors.Sort((a, b) =>
            {
                for (var i = 0; i < 3; i++)
                {
                    var d = Mathf.Round(a.center[i] / 0.003f) - Mathf.Round(b.center[i] / 0.003f);
                    if (d != 0f)
                        return d < 0f ? -1 : 1;
                }
                return 0;
            });
            var localGrab = body.transform.InverseTransformPoint(grabPoint);
            for (var i = 0; i < anchors.Count; i++)
            {
                var d = (anchors[i].center - localGrab).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    anchorIndex = i;
                }
            }
        }

        // ---------------------------------------------------------------- freezing

        void ToggleFreeze()
        {
            if (held != null)
            {
                var body = held;
                Release();
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                Sounds.Play(Sounds.Freeze, 0.45f);
                Hud.ShowToast("Frozen in mid air: F on it (or picking it up) unfreezes it", 2.5f);
                return;
            }
            var target = Cursor.Current;
            if (target != null && Cursor.CurrentInReach && target.GetComponentInParent<PartBody>() != null
                && target.GetComponentInParent<Rigidbody>() is { isKinematic: true } frozen)
            {
                Unfreeze(frozen, true);
                return;
            }
            Hud.ShowError("Hold a part and press F to freeze it in mid air");
        }

        void Unfreeze(Rigidbody body, bool announce)
        {
            body.isKinematic = false;
            body.WakeUp();
            if (!announce)
                return;
            Sounds.Play(Sounds.Release, 0.4f);
            Hud.ShowToast("Unfrozen", 1.2f);
        }

        // ---------------------------------------------------------------- rotate mode

        void StartRotateMode()
        {
            var wasHeld = held != null;
            Rigidbody body = held;
            HoverTarget target = heldTarget;
            if (body == null)
            {
                target = Cursor.Current;
                if (target != null && Cursor.CurrentInReach && target.GetComponentInParent<PartBody>() != null)
                    body = target.GetComponentInParent<Rigidbody>();
            }
            if (body == null)
            {
                Hud.ShowError("Hold a part or aim at one, then press R to rotate it");
                return;
            }
            var distance = holdDistance;
            var localGrab = localGrabPoint;
            Release();
            if (rotator == null && !TryGetComponent(out rotator))
                rotator = gameObject.AddComponent<PartRotator>();
            rotator.Begin(body, View, Hud, part =>
            {
                // Back into the hand if it was being carried.
                if (wasHeld && part != null && isActiveAndEnabled && Belt.Current == this)
                    Grab(part, target, part.transform.TransformPoint(localGrab), distance);
            });
        }

        // ---------------------------------------------------------------- carrying

        void Grab(Rigidbody body, HoverTarget target, Vector3 grabPoint, float distance)
        {
            held = body;
            heldTarget = target;
            localGrabPoint = body.transform.InverseTransformPoint(grabPoint);
            holdDistance = Mathf.Clamp(distance, minHoldDistance, maxHoldDistance);
            rotationFromView = Quaternion.Inverse(ViewYaw()) * body.rotation;

            heldUsedGravity = body.useGravity;
            heldDrag = body.linearDamping;
            heldAngularDrag = body.angularDamping;
            body.useGravity = false;
            body.linearDamping = 2f;
            body.angularDamping = 6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.WakeUp();
            Cursor.Holding = target;
            PrepareAnchors(body, grabPoint);
        }

        public void Release()
        {
            if (freeTurning)
            {
                freeTurning = false;
                InputLocks.LookSuppressed = false;
            }
            EndSnap();
            HideMarkers();
            if (Cursor != null)
                Cursor.HoldingHint = null;
            if (held == null)
                return;
            if (held)
            {
                held.useGravity = heldUsedGravity;
                held.linearDamping = heldDrag;
                held.angularDamping = heldAngularDrag;
                // Don't let a fast swing launch parts across the room.
                held.linearVelocity = Vector3.ClampMagnitude(held.linearVelocity, 2.5f);
            }
            held = null;
            heldTarget = null;
            heldPart = null;
            anchors.Clear();
            anchorIndex = -1;
            Cursor.Holding = null;
        }

        void FixedUpdate()
        {
            if (held == null)
                return;
            if (!held || !held.gameObject.activeInHierarchy)
            {
                Release();
                return;
            }
            if (Snapped)
            {
                held.MovePosition(snapPosition);
                held.MoveRotation(snapRotation);
                return;
            }

            // Move the grabbed point towards the spot in front of the camera.
            var target = head.position + head.forward * holdDistance;
            var grabPoint = held.transform.TransformPoint(localGrabPoint);
            var offset = target - grabPoint;
            if (offset.magnitude > breakDistance)
            {
                Release();
                Hud.ShowError("Dropped it: something was in the way");
                return;
            }
            var velocity = offset / Time.fixedDeltaTime * followStrength;
            held.linearVelocity = Vector3.ClampMagnitude(velocity, maxCarrySpeed);

            // Keep the part's orientation relative to where the player faces (it turns with you).
            var wanted = ViewYaw() * rotationFromView;
            var delta = wanted * Quaternion.Inverse(held.rotation);
            delta.ToAngleAxis(out var angle, out var axis);
            if (angle > 180f)
                angle -= 360f;
            if (Mathf.Abs(angle) > 0.01f && !float.IsNaN(axis.x))
                held.angularVelocity = Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime * followStrength), maxTurnSpeed);
            else
                held.angularVelocity = Vector3.zero;
        }

        Quaternion ViewYaw() => Quaternion.Euler(0f, head.eulerAngles.y, 0f);

        void OnDisable() => Release();
    }
}
