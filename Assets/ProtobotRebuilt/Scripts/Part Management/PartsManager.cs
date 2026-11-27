using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Protobot {
    public static class PartsManager {
        public static PartType[] partTypes;

        [RuntimeInitializeOnLoadMethod]
        public static void LoadPartTypes() {
            // V2: the VEX-CAD families (ids "VX...") first, so the parts list (which stacks bottom-up) shows the
            // original's parts at the top, in the original's order, and the VEX families below them.
            partTypes = Resources.LoadAll("Part Prefabs").Select(p => (p as GameObject)?.GetComponent<PartType>())
                .OrderBy(p => !p.id.StartsWith("VX")).ToArray();
            foreach (var p in partTypes)
                p.GetComponent<PartGenerator>().InitParamValues();
            // V2: imported models and polycarbonate pieces (Custom Parts), with ids like the others.
            partTypes = partTypes.Concat(Custom.CustomPartGenerator.CreateTypes()).ToArray();
            // V2: chains (made round sprockets by the chain tool).
            partTypes = partTypes.Append(Motion.ChainPartGenerator.CreateType()).ToArray();
            partTypes = partTypes.Append(Motion.RoboSourceSpacers.CreateType()).ToArray();   // V2: RoboSource's thin washers and spacers
            // V2: rubber bands and latex tubing (loops round posts) and pneumatic tubing (routed between fittings).
            partTypes = partTypes.Append(Motion.ElasticPartGenerator.CreateType()).ToArray();
            partTypes = partTypes.Append(Motion.TubePartGenerator.CreateType()).ToArray();
            partTypes = partTypes.Append(Motion.WirePartGenerator.CreateType()).ToArray();   // V2: cables
        }

        public static PartType GetPartType(string id) {
            var idSplit = id.Split('-');
            var typeId = idSplit[0];
            return partTypes.FirstOrDefault(partType => partType.id == typeId);
        }
        
        public static GameObject GeneratePart(string id, Vector3 pos, Quaternion rot) {
            var idSplit = id.Split('-');
            var typeId = idSplit[0];

            var p1Val = "";
            var p2Val = "";

            if (idSplit.Length >= 2) p1Val = idSplit[1];
            if (idSplit.Length == 3) p2Val = idSplit[2];
    
            if (id == "NUT") { //VERY TEMPORARY ONLY FOR BETA 1.3.1 PLEASE FIX WITH VERSION CONTROL
                p1Val = "Lock";
            }

            GameObject partObj = null;
            
            foreach (var partType in partTypes) {
                if (partType.id == typeId) {
                    var gen = partType.GetComponent<PartGenerator>();
                    
                    Parameter p1 = gen.param1;
                    Parameter p2 = gen.param2;

                    gen.param1.value = p1Val;
                    gen.param2.value = p2Val;
                    
                    //TODO This is awful and should be compared against an array in another file/method to make sure legacy files work but this is a quick fix
                    if (gen.name == "Omni Wheel" || gen.name == "Traction Wheel")
                    {
                        if (p2Val == "")
                        {
                            p2.value = p1Val;
                            p1.value = "V1";
                        }
                    }
                    if (gen.name == "Motor" && p1Val == "")
                    {
                        p1.value = "11W";
                    }
                    if (gen.name == "Block Bearing" && p1Val == "")
                    {
                        p1.value = "Normal";
                    }
                    if (gen.name == "Cylinder" && p2Val == "")
                    {
                        p2.value = "Normal";
                    }
                    if (gen.name == "Ring" && p1Val == "")
                    {
                        p1.value = "Red";
                    }

                    partObj = gen.Generate(pos, rot);

                    gen.param1.value = p1.value;
                    gen.param2.value = p2.value;
                }
            }

            return partObj;
        }

        /// <summary>V2: sets a part type's generator to make the part with this id (as GeneratePart reads it), so placing
        /// can start from it (paste). Old ids' defaults as GeneratePart's.</summary>
        public static void SetParams(PartGenerator gen, string id) {
            var idSplit = id.Split('-');
            var p1Val = idSplit.Length >= 2 ? idSplit[1] : "";
            var p2Val = idSplit.Length == 3 ? idSplit[2] : "";
            if (id == "NUT") p1Val = "Lock";
            gen.param1.value = p1Val;
            gen.param2.value = p2Val;
            if ((gen.name == "Omni Wheel" || gen.name == "Traction Wheel") && p2Val == "") {
                gen.param2.value = p1Val;
                gen.param1.value = "V1";
            }
            if (gen.name == "Motor" && p1Val == "") gen.param1.value = "11W";
            if (gen.name == "Block Bearing" && p1Val == "") gen.param1.value = "Normal";
            if (gen.name == "Cylinder" && p2Val == "") gen.param2.value = "Normal";
            if (gen.name == "Ring" && p1Val == "") gen.param1.value = "Red";
        }

        /// <Summary> Returns a list of all loaded parts in the current scene </Summary>
        public static List<GameObject> FindLoadedObjects() {
            return GameObject.FindObjectsOfType<SavedObject>().Select(x => x.gameObject).ToList();
        }

        /// <Summary> Destroys all loaded parts in the current scene </Summary>
        public static void DestroyLoadedObjects() {
            // V2 (build 73): a build opened while the simulation runs: stop it first (it went on stepping the old robot's
            // destroyed motors and threw an error every physics step: a player's bug report).
            if (Motion.Sandbox.Running) Motion.Sandbox.Stop();
            // V2: switched off first: Destroy waits for the frame's end, and a build opened in the same frame (the same
            // build again, or parts in the same places) had its screws find these holes, then sort destroyed ones.
            foreach (var obj in FindLoadedObjects()) {
                obj.SetActive(false);
                GameObject.Destroy(obj);
            }
        }
    }
}
