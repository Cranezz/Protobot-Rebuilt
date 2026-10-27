using System.Collections;
using System.Collections.Generic;
using Protobot.Audio;
using Protobot.Building;
using Protobot.Core;
using Protobot.Interaction;
using Protobot.Parts;
using Protobot.Saving;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools
{
    /// <summary>
    /// Empty hands. Click a part to pick it up, click again to drop it. A held part is pulled towards a point
    /// in front of the camera (never past the surface you look at); it passes through everything, so it can't
    /// knock other parts about, and when dropped inside something it's lifted out. While holding: the mouse
    /// wheel moves it closer or further, holding the wheel
    /// down turns it freely with the mouse, right click gives it a quarter turn, and F freezes it in mid air
    /// (F on a frozen part, or picking it up, unfreezes it). R turns the held part or the one you aim at.
    ///
    /// Building is done by holes. Aim at a hole and it's outlined; click and the part is held by that hole
    /// (Q/E hold it by the next one). H switches hole grab on, so parts can only be picked up by a hole and
    /// the nearest hole is always the one taken. Bring the held hole to a hole on another part and the part
    /// snaps there, faces flush and the hole grids lined up; the wheel turns it about the hole, right click
    /// flips it over, click places it. A placed part is tacked on: it stays, R turns it on its hole, and
    /// grabbing it pulls it off. A screw put through it and a nut (or a threaded hole) fasten it for good:
    /// fastened parts are one piece and grabbing any of them picks up the lot. Hold right click on a nut to
    /// unscrew it.
    /// </summary>
    public class HandsTool : PlayerTool
    {
        [SerializeField] Transform head;

        [Header("Holding")]
        [SerializeField] float minHoldDistance = 0.08f;
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
        [Tooltip("With hole grab on, the nearest hole within this distance of the aim is the one taken (m).")]
        [SerializeField] float holeGrabRadius = 0.03f;

        const string HoleModeKey = "Protobot.HoleGrab";
        static readonly Color TargetColor = new(1f, 0.82f, 0.2f);
        static readonly Color AnchorColor = new(0.3f, 0.85f, 1f);

        enum SnapKind { None, Part, Screw, Nut }

        Rigidbody held;
        HoverTarget heldTarget;
        Vector3 localGrabPoint;
        bool heldByHole;
        Quaternion rotationFromView;
        float holdDistance;
        bool heldUsedGravity;
        float heldDrag;
        float heldAngularDrag;
        bool freeTurning;
        PartRotator rotator;
        List<(GameObject go, int layer)> heldLayers;

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
        HoleAt snapHole;
        Vector3 snapPosition;
        Quaternion snapRotation;
        bool snapFlip;
        float snapTwist;
        string snapProblem;
        readonly List<Collider> triggered = new();

        public bool IsHolding => held != null;
        bool Snapped => snapKind != SnapKind.None;

        /// <summary>Hole grab: parts are only picked up by a hole (H turns it on and off; remembered).</summary>
        public static bool HoleMode
        {
            get => PlayerPrefs.GetInt(HoleModeKey, 0) == 1;
            set => PlayerPrefs.SetInt(HoleModeKey, value ? 1 : 0);
        }

        public override string ControlsHint => !IsHolding
            ? $"Left click: pick up (Shift: whole assembly)   ·   R: turn   ·   Hold right click: take apart   ·   H: hole grab {(HoleMode ? "ON" : "off")}   ·   B: build view"
            : snapKind == SnapKind.Part
                ? "Left click: place   ·   Wheel: turn 45° (Shift: 15°)   ·   Right click: flip   ·   Q/E: other hole   ·   F: freeze"
                : snapKind == SnapKind.Screw ? "Left click: put the screw in"
                : snapKind == SnapKind.Nut ? "Left click: thread the nut on"
                : "Left click: drop   ·   Wheel: closer / further   ·   Hold wheel: turn   ·   Q/E: hold by another hole   ·   R: turn   ·   F: freeze   ·   Right click: 90°";

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
            var byHole = FindHoveredHole(out var hole) && hole.part == part;
            if (HoleMode && part != null && !byHole)
            {
                Hud.ShowError(part.TryGetComponent(out PartHoles _)
                    ? "Hole grab is on: aim at a hole   ·   H picks parts up anywhere"
                    : "This part has no holes   ·   H turns hole grab off");
                return;
            }
            var whole = part != null && Assembly.Of(part) != null && (Keyboard.current.shiftKey.isPressed || Assembly.IsFastened(part));
            BuildManager.Record(whole ? "Move assembly" : "Move " + (part != null ? part.name : target.Title));
            if (part != null && Assembly.Of(part) != null && !Keyboard.current.shiftKey.isPressed)
                TakeOff(part);
            if (target.GetComponentInParent<Rigidbody>() is not { } body)
                return;
            if (body.isKinematic)
                Unfreeze(body, false);  // picking up a frozen part unfreezes it
            var grabPoint = Cursor.CurrentHit.point;
            if (byHole)
                HoleSnap.NearFace(hole, head.position, out grabPoint, out _);
            Grab(body, target, grabPoint, Vector3.Distance(head.position, grabPoint), byHole);
            Sounds.Play(Sounds.Pickup, 0.45f);
        }

        /// <summary>
        /// Clicking a part that's in an assembly: a loose screw comes out, a tacked part pulls off. A fastened
        /// part (a nut and screw too) stays, and the whole assembly is picked up.
        /// </summary>
        void TakeOff(PartBody part)
        {
            if (Assembly.IsFastened(part))
                return;
            if (part.TryGetComponent(out ScrewJoint screw))
            {
                screw.Remove();
                Hud.ShowToast("Screw out", 1.2f);
                return;
            }
            if (Assembly.Detach(part))
                Hud.ShowToast("Pulled off", 1f);
        }

        /// <summary>Hold right click on a part in an assembly: unscrew a nut, pull a screw, take a part off.</summary>
        public override void SecondaryHold()
        {
            if (held != null)
                return;
            var target = Cursor.Current;
            var part = target != null && Cursor.CurrentInReach ? target.GetComponentInParent<PartBody>() : null;
            if (part == null || Assembly.Of(part) == null)
                return;
            BuildManager.Record("Take apart");
            if (FindNutJoint(part) is { } nutJoint)
            {
                nutJoint.RemoveNut();
                Sounds.Play(Sounds.Drill, 0.25f);
                Hud.ShowToast("Unscrewed the nut: the screw only holds the parts lined up now", 2.5f);
                return;
            }
            if (part.TryGetComponent(out ScrewJoint screw))
            {
                // The nut first (the screw's tip is easier to aim at than a small nut), then the screw.
                if (screw.Nut != null)
                {
                    screw.RemoveNut();
                    Sounds.Play(Sounds.Drill, 0.25f);
                    Hud.ShowToast("Unscrewed the nut   ·   Hold right click again to pull the screw", 2.5f);
                    return;
                }
                screw.Remove();
                Sounds.Play(Sounds.Release, 0.4f);
                Hud.ShowToast("Screw out", 1.2f);
                return;
            }
            if (Assembly.IsFastened(part))
            {
                Hud.ShowError("Fastened: hold right click on its screw (or nut) to unscrew it first");
                return;
            }
            if (Assembly.Detach(part))
            {
                Sounds.Play(Sounds.Release, 0.4f);
                Hud.ShowToast("Took it off", 1.2f);
            }
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
            holdDistance = Mathf.Clamp(holdDistance + notches * scrollStep * Mathf.Clamp(holdDistance / 0.4f, 0.25f, 1f), minHoldDistance, maxHoldDistance);
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

            if (held != null && anchors.Count > 0 && (keyboard.qKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame))
            {
                if (heldByHole || anchorIndex < 0)
                    anchorIndex = (anchorIndex + (keyboard.eKey.wasPressedThisFrame ? 1 : anchors.Count - 1)) % anchors.Count;
                HoldByAnchor();
                Sounds.Play(Sounds.Tick, 0.3f);
            }

            if (keyboard.hKey.wasPressedThisFrame)
            {
                HoleMode = !HoleMode;
                Sounds.Play(Sounds.Tick, 0.4f);
                Hud.ShowToast(HoleMode ? "Hole grab ON: parts are picked up by the hole you aim at   ·   H turns it off" : "Hole grab off: parts are picked up anywhere", 2.5f);
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
            string action = null;
            if (Assembly.Of(part) != null)
            {
                action = part.TryGetComponent(out ScrewJoint screw)
                    ? screw.Nut != null ? "Fastened   ·   Click: pick up the assembly   ·   Hold right click: unscrew its nut" : "Loose screw   ·   Click: pull it out"
                    : FindNutJoint(part) != null ? "Fastened   ·   Click: pick up the assembly   ·   Hold right click: unscrew"
                    : Assembly.IsFastened(part) ? "Fastened   ·   Click: pick up the whole assembly   ·   Hold right click on a screw: unscrew"
                    : "Tacked on   ·   Click: pull it off   ·   R: turn it on its hole";
            }
            else if (target.GetComponentInParent<Rigidbody>() is { isKinematic: true })
            {
                action = "Frozen in place (F unfreezes)";
            }
            if (FindHoveredHole(out var hole) && hole.part == part)
                action = "Click: pick it up by this hole" + (action != null ? "   ·   " + action : "");
            else if (HoleMode && part.TryGetComponent(out PartHoles _))
                action = "Hole grab: aim at a hole to pick it up";
            if (action == null)
                return false;
            view.hint = (string.IsNullOrEmpty(view.hint) ? "" : view.hint + "   ·   ") + action;
            return true;
        }

        // ---------------------------------------------------------------- building

        /// <summary>The hole under the aim (any distance for a hole grab, close for a normal one).</summary>
        bool FindHoveredHole(out HoleAt hole)
        {
            hole = default;
            var target = Cursor.Current;
            if (target == null || !Cursor.CurrentInReach || target.GetComponentInParent<PartHoles>() is not { } holes)
                return false;
            return holes.TryNearest(Cursor.CurrentHit.point, HoleMode ? holeGrabRadius : snapRadius * 0.7f, out hole);
        }

        /// <summary>Not holding anything: outline the hole under the aim, so holes read as things you can use.</summary>
        void ShowHoveredHole()
        {
            if (FindHoveredHole(out var hole))
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
                Cursor.HoldingHint = snapProblem
                    ?? (PartRoles.IsScrew(heldRole) ? "Aim at a hole to put the screw in   ·   Click: drop"
                        : heldRole == PartRole.Nut ? "Aim at a screw to thread the nut on   ·   Click: drop"
                        : anchorIndex >= 0 ? "Aim at a hole on another part: the blue hole goes there   ·   Click: drop"
                        : null);
                return;
            }
            if (!Snapped)
                Sounds.Play(Sounds.Tick, 0.35f);
            BeginSnap();
            snapKind = kind;
            snapScrew = screw;
            snapHole = targetHole;
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

        HoleAt AnchorInWorld()
        {
            var anchor = anchors[anchorIndex];
            return new HoleAt
            {
                part = heldPart, local = anchor,
                center = held.transform.TransformPoint(anchor.center), axis = held.transform.TransformDirection(anchor.axis).normalized,
            };
        }

        void ShowAnchor()
        {
            if (anchorIndex < 0 || held == null || heldRole == PartRole.Nut || PartRoles.IsScrew(heldRole))
            {
                anchorMarker?.Hide();
                return;
            }
            var hole = AnchorInWorld();
            HoleSnap.NearFace(hole, head.position, out var face, out var outward);
            Marker(ref anchorMarker, "Anchor Marker", AnchorColor).Show(hole, face, outward);
        }

        /// <summary>Holds the part by its current anchor hole (the face towards you comes to the hand).</summary>
        void HoldByAnchor()
        {
            if (anchorIndex < 0 || held == null)
                return;
            HoleSnap.NearFace(AnchorInWorld(), head.position, out var face, out _);
            localGrabPoint = held.transform.InverseTransformPoint(face);
            heldByHole = true;
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
            var hole = snapHole;
            Release(false);
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();

            switch (kind)
            {
                case SnapKind.Part:
                    HoleSnap.NearFace(hole, head.position, out var face, out var outward);
                    Assembly.Join(part, target, LinkKind.Tack, face, outward);
                    Sounds.Play(Sounds.Clack, 0.45f);
                    Hud.ShowToast("Tacked on: screw it to fasten it   ·   R turns it on its hole   ·   Ctrl+Z undoes", 2.5f);
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
                            ? $"Screwed into a threaded hole: {joint.StackParts.Count + 1} parts fastened"
                            : $"Screw through {Names(joint.StackParts)}: thread a nut on the end to fasten", 3f);
                    }
                    Sounds.Play(Sounds.Drill, 0.25f);
                    break;
                case SnapKind.Nut:
                    screw.AddNut(part);
                    Sounds.Play(Sounds.Drill, 0.3f);
                    Hud.ShowSuccess($"Fastened: {Names(screw.StackParts)} held for good");
                    break;
            }
            if (Assembly.Of(part) is { } assembly)
                StartCoroutine(Flash(assembly.Parts()));
        }

        static string Names(IReadOnlyList<PartBody> parts)
        {
            if (parts.Count == 0)
                return "nothing";
            if (parts.Count == 1)
                return parts[0].name;
            if (parts.Count == 2)
                return $"{parts[0].name} and {parts[1].name}";
            return $"{parts.Count} parts";
        }

        /// <summary>Outlines everything that's now one piece for a moment, so you can see what got joined.</summary>
        IEnumerator Flash(List<PartBody> parts)
        {
            var targets = new List<HoverTarget>();
            foreach (var part in parts)
                if (part != null && part.TryGetComponent(out HoverTarget hover))
                {
                    hover.SetHighlighted(true);
                    targets.Add(hover);
                }
            yield return new WaitForSeconds(0.6f);
            foreach (var hover in targets)
                if (hover != null && hover != Cursor.Current)
                    hover.SetHighlighted(false);
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
                Release(false);
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
                BuildManager.Record("Unfreeze " + target.Title);
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
            if (rotator == null && !TryGetComponent(out rotator))
                rotator = gameObject.AddComponent<PartRotator>();

            if (held != null)
            {
                // About where it's held: the hole it hangs from, if it's held by one.
                var body = held;
                var target = heldTarget;
                var distance = holdDistance;
                var localGrab = localGrabPoint;
                var byHole = heldByHole;
                var request = new PartRotator.Request
                {
                    body = body, pivotSpace = body.transform, pivot = localGrab,
                    about = byHole ? "about the hole you hold it by" : "about where you hold it",
                };
                if (byHole && anchorIndex >= 0)
                {
                    request.axis = anchors[anchorIndex].axis;
                    request.hasAxis = true;
                }
                Release(false);
                rotator.Begin(request, View, Hud, () =>
                {
                    // Back into the hand.
                    if (body != null && isActiveAndEnabled && Belt.Current == this)
                        Grab(body, target, body.transform.TransformPoint(localGrab), distance, byHole);
                });
                return;
            }

            var aimed = Cursor.Current;
            var part = aimed != null && Cursor.CurrentInReach ? aimed.GetComponentInParent<PartBody>() : null;
            if (part == null)
            {
                Hud.ShowError("Hold a part or aim at one, then press R to turn it");
                return;
            }
            var assembly = Assembly.Of(part);
            var onHole = FindHoveredHole(out var hole) && hole.part == part;
            if (assembly != null && !Keyboard.current.shiftKey.isPressed && assembly.TryHinge(part, out var hinge, out var turning))
            {
                BuildManager.Record("Turn " + part.name);
                rotator.Begin(new PartRotator.Request
                {
                    assembly = assembly, parts = turning, pivotSpace = part.transform,
                    pivot = part == hinge.a ? hinge.pointA : hinge.pointB, axis = part == hinge.a ? hinge.axisA : hinge.axisB,
                    hasAxis = true, hingeOnly = true, about = "on the hole it's placed on",
                }, View, Hud, null);
                return;
            }
            var whole = assembly != null ? assembly.Body : part.Body;
            if (whole == null)
                return;
            BuildManager.Record(assembly != null ? "Turn assembly" : "Turn " + part.name);
            var free = new PartRotator.Request { body = whole };
            if (onHole)
            {
                free.pivotSpace = part.transform;
                free.pivot = hole.local.center;
                free.axis = hole.local.axis;
                free.hasAxis = true;
                free.about = assembly != null ? "the whole assembly, about the hole you aimed at" : "about the hole you aimed at";
            }
            else if (assembly != null)
            {
                free.about = Assembly.IsFastened(part) ? "the whole assembly (it's fastened)" : "the whole assembly";
            }
            rotator.Begin(free, View, Hud, null);
        }

        // ---------------------------------------------------------------- carrying

        void Grab(Rigidbody body, HoverTarget target, Vector3 grabPoint, float distance, bool byHole)
        {
            held = body;
            heldTarget = target;
            localGrabPoint = body.transform.InverseTransformPoint(grabPoint);
            heldByHole = byHole;
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
            // In the hand it passes through other parts (it would knock them flying), but not the bench.
            heldLayers = PartLayers.Move(body.gameObject, PartLayers.Held);
            Cursor.Holding = target;
            PrepareAnchors(body, grabPoint);
        }

        /// <summary>Lets go of whatever is held (it drops where it is).</summary>
        public void Release() => Release(true);

        /// <param name="unstick">Lift it out of anything it's inside (not when it's being put exactly somewhere).</param>
        void Release(bool unstick)
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
            if (heldLayers != null)
            {
                PartLayers.Restore(heldLayers);
                heldLayers = null;
            }
            if (held == null)
                return;
            if (held)
            {
                held.useGravity = heldUsedGravity;
                held.linearDamping = heldDrag;
                held.angularDamping = heldAngularDrag;
                // Don't let a fast swing launch parts across the room.
                held.linearVelocity = Vector3.ClampMagnitude(held.linearVelocity, 2.5f);
                if (unstick && !held.isKinematic)
                    PartLayers.Unstick(held);
            }
            held = null;
            heldTarget = null;
            heldPart = null;
            heldByHole = false;
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

            // Move the grabbed point towards the spot in front of the camera, but not into what's behind it: a
            // held part passes through things, so it would disappear into the bench.
            var target = head.position + head.forward * HoldDistanceNow();
            var grabPoint = held.transform.TransformPoint(localGrabPoint);
            var offset = target - grabPoint;
            if (offset.magnitude > breakDistance)
            {
                Release();
                Hud.ShowError("Dropped it: something was in the way");
                return;
            }
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

            // Physics turns a body about its centre of mass; take that swing out so it turns about the point
            // it's held by (the hole, when held by one).
            var velocity = offset / Time.fixedDeltaTime * followStrength
                - Vector3.Cross(held.angularVelocity, grabPoint - held.worldCenterOfMass);
            held.linearVelocity = Vector3.ClampMagnitude(velocity, maxCarrySpeed);
        }

        /// <summary>How far ahead the held point goes: the wheel's distance, or just short of a surface in the way.</summary>
        float HoldDistanceNow()
        {
            var hits = Physics.RaycastAll(head.position, head.forward, holdDistance + 0.01f, PartLayers.Solid, QueryTriggerInteraction.Ignore);
            var distance = holdDistance;
            foreach (var hit in hits)
                if (hit.rigidbody != held || held == null)
                    distance = Mathf.Min(distance, hit.distance - 0.006f);
            return Mathf.Max(distance, 0.03f);
        }

        Quaternion ViewYaw() => Quaternion.Euler(0f, head.eulerAngles.y, 0f);

        void OnDisable() => Release();
    }
}
