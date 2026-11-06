using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using UnityEditor;

namespace Protobot.Builds {
    public static class SceneBuild {
        public static Action<BuildData> OnGenerateBuild;

        public static BuildData DefaultBuild => new BuildData {
            name = "DefaultBuild",
            camera = new CameraData {
                xPos = 0,
                yPos = 0,
                zPos = 0,
                xRot = 30,
                yRot = 45,
                zRot = 0,
                zoom = -15,
                isOrtho = false
            },
            lastWriteTime = DateTime.Now.ToString("MMMM dd, yyyy h:mm tt")
        };

        /// <summary>
        /// Generates all the objects into the scene using given BuildData
        /// </summary>
        public static void GenerateBuild(BuildData buildData) {
            Debug.Log("Generating a build with " +
                      (buildData.parts == null ? 0 : buildData.parts.Length) + " parts");

            PartsManager.DestroyLoadedObjects();
            Motion.Controls.LoadDrive(buildData.drive);   // V2: the build's drive control

            //Camera Data
            CameraData camData = buildData.camera;

            Vector3 savedCamPos = new Vector3((float)camData.xPos, (float)camData.yPos, (float)camData.zPos);
            Vector3 savedCamAngle = new Vector3((float)camData.xRot, (float)camData.yRot, (float)camData.zRot);

            PivotCamera.Main.SetTransform(savedCamPos, savedCamAngle, (float)camData.zoom);

            var projectionSwitcher = PivotCamera.Main.GetComponent<ProjectionSwitcher>();

            if (camData.isOrtho)
                projectionSwitcher.SwitchToOrtho(0);
            else
                projectionSwitcher.SwitchToPers(0);

            //Parts
            if (buildData.parts != null) {
                List<ObjectData> connectingObjects = new();

                foreach (ObjectData part in buildData.parts) {
                    if (part.partId == "Error") continue; //this is jank but I'm avoiding editing main scene
                    if (PartsManager.GetPartType(part.partId).connectingPart) {
                        connectingObjects.Add(part);
                    }
                    else
                        GenerateObject(part, buildData);
                }

                foreach (ObjectData part in connectingObjects)
                    GenerateObject(part, buildData);

            }
            
            OnGenerateBuild?.Invoke(buildData);

            if (AppPlatform.OnMac)
            {
                try
                {
                    GameObject.Find("Mac Support Check").GetComponent<MacRebind>().RebindCheck();
                }
                catch (Exception ex)
                {
                    Debug.LogError("An error occurred while performing Mac rebind check: " + ex.Message);
                }
            }
        }

        private static GameObject GenerateObject(ObjectData objectData, BuildData buildData)
        {
            GameObject generatedObject = PartsManager.GeneratePart(objectData.partId, objectData.GetPos(), objectData.GetRot());
            //there are only 2 versions of Protobot legacy publicly released that I could find most before Beta 1.3.1 are just guesses
            string[] versionsNoColor = new string[] {"1.0", "1.1", "1.1.1", "Beta 1.2", "Beta 1.3", "Beta 1.3.1", "1.3.2", "1.3.3", "1.3.4" };
            //Debug.Log(buildData.version);    
            if(!versionsNoColor.Contains(buildData.version) && buildData.version != null)
            { 
                // V2: saved colors have no alpha: see-through parts (polycarbonate) keep theirs.
                var renderer = generatedObject.GetComponent<Renderer>();
                var color = objectData.GetColor();
                color.a = renderer.material.color.a;
                renderer.material.color = color;
                // V2: the other colors of a part made of several.
                if (!string.IsNullOrEmpty(objectData.colors)) {
                    var codes = objectData.colors.Split(',');
                    var materials = renderer.materials;
                    for (var m = 0; m < materials.Length && m < codes.Length; m++)
                        if (ColorUtility.TryParseHtmlString("#" + codes[m], out var partColor)) {
                            partColor.a = materials[m].color.a;
                            materials[m].color = partColor;
                        }
                }
            }
            // V2: edits kept in the part's state (drilled holes) are made again.
            if (!string.IsNullOrEmpty(objectData.states) && generatedObject.TryGetComponent(out SavedObject savedObject)) {
                savedObject.state = objectData.states;
                Tools.PartDrilling.ApplySaved(generatedObject);
            }
            return generatedObject;
        }
            


        /// <summary>
        /// Generates an empty build with default camera data into the scene
        /// </summary>
        public static BuildData GenerateDefault(string buildName) {
            var newBuild = DefaultBuild;
            newBuild.name = buildName;

            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            newBuild.fileName = new string(buildName.Where(m => !invalidChars.Contains(m)).ToArray<char>());

            GenerateBuild(newBuild);

            return newBuild;
        }

        /// <summary>
        /// Converts scene objects into BuildData
        /// </summary>
        /// <remarks>Does not contain lastWriteTime, createTime, fileName, or name</remarks>
        public static BuildData ToBuildData() {
            //Camera
            PivotCamera cam = PivotCamera.Main;
            ProjectionSwitcher projectionSwitcher = cam.GetComponent<ProjectionSwitcher>();

            CameraData newCameraData = new CameraData {
                xPos = cam.focusPosition.x,
                yPos = cam.focusPosition.y,
                zPos = cam.focusPosition.z,

                xRot = cam.lookAngle.x,
                yRot = cam.lookAngle.y,
                zRot = cam.lookAngle.z,

                zoom = cam.focusDistance,

                isOrtho = projectionSwitcher.isOrtho
            };

            //Parts
            List<GameObject> sceneObjs = PartsManager.FindLoadedObjects();

            ObjectData[] newParts = new ObjectData[sceneObjs.Count];

            for (int i = 0; i < newParts.Length; i++) {
                Transform tForm = sceneObjs[i].transform;
                SavedObject savedData = tForm.GetComponent<SavedObject>();
                Renderer savedColor = tForm.GetComponent<Renderer>();

                var position = tForm.position;
                var eulerAngles = tForm.eulerAngles;
                newParts[i] = new ObjectData {
                    partId = savedData.id,
                    states = savedData.state,

                    xPos = position.x,
                    yPos = position.y,
                    zPos = position.z,

                    xRot = eulerAngles.x,
                    yRot = eulerAngles.y,
                    zRot = eulerAngles.z,

                    rColor = savedColor.material.color.r,
                    bColor = savedColor.material.color.b,
                    gColor = savedColor.material.color.g,
                    colors = savedColor.sharedMaterials.Length > 1
                        ? string.Join(",", savedColor.sharedMaterials.Select(m => m != null ? ColorUtility.ToHtmlStringRGB(m.color) : "FFFFFF"))
                        : null,
                };
            }


            return new BuildData {
                camera = newCameraData,
                parts = newParts,
                drive = Motion.Controls.DriveToken,   // V2
            };
        }
    }
}