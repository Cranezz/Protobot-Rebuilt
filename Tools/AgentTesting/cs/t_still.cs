// Where the drive's frame is now in the simulation (x, z, heading) and its speed: run twice a few seconds apart to see
// if a robot standing still creeps. Also each body's speed over 0.05 in/s.
var frame = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(s => s.id == "CCHL-1x2-23").OrderBy(s => s.transform.position.z).Skip(1).First().gameObject;
var p = Protobot.Devices.Pose.Point(frame, Vector3.zero);
var f = Protobot.Devices.Pose.Dir(frame, frame.transform.InverseTransformDirection(Vector3.forward));   // (the build's forward: the part's own forward points down)
var sb = new System.Text.StringBuilder("t " + (Protobot.RobotCode.RobotProgram.Current != null ? Protobot.RobotCode.RobotProgram.Current.Time.ToString("0.00") : "-") + " pos " + p.x.ToString("0.0000") + ", " + p.z.ToString("0.0000") + " yaw " + (Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg).ToString("0.0000"));
foreach (var b in Protobot.Motion.Sandbox.Instance.Bodies())
    if (b.rb.linearVelocity.magnitude > 0.05f || b.rb.angularVelocity.magnitude > 0.05f)
        sb.Append(" | " + b.nodes[0].name + " v " + b.rb.linearVelocity.magnitude.ToString("0.00") + " w " + b.rb.angularVelocity.magnitude.ToString("0.00"));
return sb.ToString();
