using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Protobot.Transformations;

namespace Protobot {
    public class MoveToolManager : MonoBehaviour {
        [SerializeField] private Placement placement;
        [SerializeField] private ObjectLink objectLink;

        private Image image;

        private void Start() {
            image = GetComponent<Image>();
        }

        // V2: the selection last seen, so a new one is noticed.
        private SelectionSystem.ISelection seenSelection;

        private void Update() {
            if (image != null && objectLink.active)
                image.enabled = (objectLink.obj.layer != Placement.PLACEMENT_LAYER);
            GrabClickedHole();
            GrabClickedThread();
        }

        // V2: clicking a pneumatic cylinder's thread picks the cylinder up by it: over a hole or a nut, the thread goes
        // into it as a screw would.
        private Motion.RodThread pendingThread;

        private void GrabClickedThread() {
            if (Interface.ToolRail.Current != "Move" || placement.placing) {
                pendingThread = null;
                return;
            }
            if (pendingThread == null && ThreadPlaceDisplacement.Clicked != null && Time.frameCount - ThreadPlaceDisplacement.ClickedFrame < 10) {
                pendingThread = ThreadPlaceDisplacement.Clicked;
                pendingFrame = ThreadPlaceDisplacement.ClickedFrame;
                ThreadPlaceDisplacement.Clicked = null;
            }
            if (pendingThread != null) {
                if (objectLink.active && objectLink.obj == pendingThread.cylinder) {
                    ThreadPlaceDisplacement.Grabbed = pendingThread;
                    pendingThread = null;
                    StartMoving();
                    Interface.Interface.Say("Holding the cylinder by its thread: click a hole or a nut to put the thread in (Esc lets go)");
                }
                else if (Time.frameCount > pendingFrame + 10) pendingThread = null;
            }
        }

        private int pendingFrame;

        // V2: in the Move tool, clicking a hole grabs the part by it straight away (the Move button doesn't need a
        // second click): the part follows the mouse onto the next hole you point at, and a click puts it there.
        private void GrabClickedHole() {
            if (!(objectLink is SelectionObjectLink link) || link.Manager == null) return;
            var current = link.Manager.current;
            // The click that places the part selects the hole it went into: that one isn't grabbed.
            if (placement.placing || Interface.ToolRail.Current != "Move") {
                seenSelection = current;
                return;
            }
            if (current == seenSelection) return;
            seenSelection = current;
            if (current is SelectionSystem.HoleSelection && objectLink.active) {
                StartMoving();
                Interface.Interface.Say("Now click the hole it goes into (Esc lets go)");
            }
        }

        public void StartMoving() {
            placement.StartPlacing(new GameObjectPlacementData(objectLink.obj, placement.transform));
        }
    }
}

