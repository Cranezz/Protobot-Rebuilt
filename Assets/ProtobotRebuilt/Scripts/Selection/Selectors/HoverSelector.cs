using System;
using UnityEngine;

namespace Protobot.SelectionSystem {
    public class HoverSelector : Selector {
        public override event Action<ISelection> setEvent;
        public override event Action clearEvent;
        [SerializeField] private MouseCast mouseCast = null;
        [SerializeField] private bool checkPrevObj;

        private GameObject prevObj;

        /// <summary>V2: bumped when what's under the mouse changed shape (a hole just drilled): every hover shows
        /// it again at once, instead of keeping the old outline until the mouse moved off and back.</summary>
        public static int Changes;
        private int seenChanges;

        public void Update() {
            // V2: the mouse cast goes through the interface, so parts behind the parts list lit up and stayed lit;
            // over the interface nothing is hovered.
            GameObject mouseCastObj = MouseInput.overUI ? null : mouseCast.gameObject;
            mouseCastObj = SelectAs.Resolve(mouseCastObj);  // V2: a cylinder's rod and thread select the cylinder

            if (mouseCastObj != null) {
                if (prevObj != mouseCastObj || !checkPrevObj || seenChanges != Changes) {
                    if (seenChanges != Changes) clearEvent?.Invoke();  // V2
                    seenChanges = Changes;
                    var selection = new ObjectSelection {
                        gameObject = mouseCastObj,
                        selector = this
                    };

                    setEvent?.Invoke(selection);
                }
            }
            else
                clearEvent?.Invoke();

            prevObj = mouseCastObj;
        }

        // V2: a hover manager switched off (placing, another tool) let go of its outline.
        private void OnDisable() {
            clearEvent?.Invoke();
            prevObj = null;
        }
    }
}