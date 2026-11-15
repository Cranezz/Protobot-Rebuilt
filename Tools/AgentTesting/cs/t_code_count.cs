var all = UnityEngine.Object.FindObjectsByType<Protobot.Motion.Sandbox>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
return "sandboxes " + all.Length + " fixedDelta " + Time.fixedDeltaTime + " simMode " + Physics.simulationMode;
