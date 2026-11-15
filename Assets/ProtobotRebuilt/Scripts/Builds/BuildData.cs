using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Protobot.Builds {
    [Serializable]
    public class BuildData {
        public string name;
        public string fileName; //the name used to determine path

        public ObjectData[] parts = Array.Empty<ObjectData>();
        public CameraData camera;

        public string lastWriteTime;

        public string version = AppData.Version;

        // V2: the drivetrain's control (Motion.Controls.DriveMode), "" or null when the build never chose one.
        [System.Runtime.Serialization.OptionalField] public string drive;

        // V2: the robot's V5 controller bindings (Devices.V5Controller), "" or null when it uses the player's default.
        [System.Runtime.Serialization.OptionalField] public string controller;

        // V2 (Phase 9): the robot's program (RobotCode.CodeStore: Python source), "" or null when it has none.
        [System.Runtime.Serialization.OptionalField] public string code;

        public bool CompareData(BuildData data) {
            if (data.parts.Length != parts.Length) {
                return false;
            }
            if ((data.drive ?? "") != (drive ?? "")) return false;   // V2
            if ((data.controller ?? "") != (controller ?? "")) return false;   // V2
            if ((data.code ?? "") != (code ?? "")) return false;   // V2 (Phase 9)

            foreach (ObjectData part in parts) {
                if (!data.parts.Contains(part))
                    return false;
            }

            return true;
        }
    }
}