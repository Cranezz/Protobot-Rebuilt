using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    public static class GroupManager {
        private static Dictionary<GameObject, HashSet<GameObject>> Graph = new();

        /// <summary>
        /// Forms a connection between a connecting part and a list of given parts
        /// </summary>
        /// <param name="connectingPart"></param>
        /// <param name="objs"></param>
        public static void FormConnection(ConnectingPart connectingPart, List<GameObject> objs) {
            GameObject connectingObj = connectingPart.gameObject;

            Graph.TryAdd(connectingObj, new HashSet<GameObject>());

            foreach (GameObject obj in objs) {
                Graph[connectingObj].Add(obj);
                
                Graph.TryAdd(obj, new HashSet<GameObject>());
                Graph[obj].Add(connectingObj);
            }
        }
        
        /// <summary>V2: holds two parts together without a screw (a slide truck in its slide member).</summary>
        public static void Link(GameObject a, GameObject b) {
            if (a == null || b == null) return;
            Graph.TryAdd(a, new HashSet<GameObject>());
            Graph.TryAdd(b, new HashSet<GameObject>());
            Graph[a].Add(b);
            Graph[b].Add(a);
        }

        public static void Unlink(GameObject a, GameObject b) {
            if (a != null && Graph.ContainsKey(a)) Graph[a].Remove(b);
            if (b != null && Graph.ContainsKey(b)) Graph[b].Remove(a);
        }

        /// <summary>
        /// Destroys the connection formed between these two objects
        /// </summary>
        public static void BreakConnection(ConnectingPart connectingPart, GameObject obj) {
            GameObject connectingObj = connectingPart.gameObject;

            if (Graph.ContainsKey(connectingObj))
                Graph[connectingObj].Remove(obj);
            
            if (Graph.ContainsKey(obj))
                Graph[obj].Remove(connectingObj);
        }
        
        /// <summary>
        /// Removes a given object and all of its connections from the group graph
        /// </summary>
        public static void Remove(GameObject obj) {
            if (Graph.ContainsKey(obj)) {
                foreach (GameObject c in Graph[obj]) {
                    Graph[c].Remove(obj);
                }

                Graph.Remove(obj);
            }
        }

        private static HashSet<GameObject> visited = new();
        
        /// <summary>
        /// Visits all objects connected to the given obj, populates the visited list
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        private static void GetGroup(GameObject obj) {
            visited.Add(obj);

            foreach (GameObject c in Graph[obj]) {
                if (!c.IsDeleted() && !visited.Contains(c)) {
                    GetGroup(c);
                }
            }
        }

        /// <summary>V2: what one object is fastened to directly (moving parts follow these links).</summary>
        public static IEnumerable<GameObject> Neighbours(GameObject obj) =>
            obj != null && Graph.TryGetValue(obj, out var set) ? set.ToList() : new List<GameObject>();

        public static bool TryGetGroup(this GameObject obj, out List<GameObject> objs) {
            if (Graph.ContainsKey(obj) && Graph[obj].Count > 0) {
                visited.Clear();
                GetGroup(obj);
                objs = visited.ToList();
                
                return true;
            }
            
            objs = null;
            return false;
        }
    }
}