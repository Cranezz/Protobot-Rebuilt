using UnityEngine;
using Protobot.SelectionSystem;

namespace Protobot {
    public class SelectionObjectLink : ObjectLink {
        public override GameObject obj => selectionManager.current?.gameObject;
        [SerializeField] private SelectionManager selectionManager;
        public SelectionManager Manager => selectionManager; // V2: the Move tool watches what gets selected
    }
}