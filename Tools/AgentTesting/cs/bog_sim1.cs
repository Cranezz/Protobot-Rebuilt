Protobot.PartsManager.DestroyLoadedObjects();
Protobot.PartsManager.GeneratePart("VXWHEELS-Bogie Wheel Double", new Vector3(0f, 2f, 0f), Quaternion.Euler(90f, 0f, 0f));
Protobot.Motion.Sandbox.Begin();
return "sandbox " + Protobot.Motion.Sandbox.Running;
