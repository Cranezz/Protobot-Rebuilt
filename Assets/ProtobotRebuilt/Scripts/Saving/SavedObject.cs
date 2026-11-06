using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    public class SavedObject : MonoBehaviour {
        public string id;
        public string nameId => id.Split('-')[0];
        public string state;

        // V2: counts parts made, shown, hidden or removed, so caches of the build (gear trains, sprockets) know
        // when to look again.
        public static int Changes { get; private set; }
        private void OnEnable() => Changes++;
        private void OnDisable() => Changes++;
    }
}
