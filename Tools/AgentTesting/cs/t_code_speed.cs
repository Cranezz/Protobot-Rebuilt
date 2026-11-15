var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var sb = new System.Text.StringBuilder();
sb.Append("body speed ").Append(rb.linearVelocity.magnitude.ToString("0.0")).Append(" in/s; ");
foreach (var m in Protobot.Motion.MotorDrive.Live()) sb.Append(m.name.Substring(0, System.Math.Min(6, m.name.Length))).Append(" cmd ").Append(m.Command.ToString("0.00")).Append(" lvl ").Append(m.Level.ToString("0.00")).Append("; ");
var p = Protobot.RobotCode.RobotProgram.Current;
sb.Append(" t=").Append(p != null ? p.Time.ToString("0.0") : "-");
// wheels: angular speed of bodies with omni name
foreach (var b in Protobot.Motion.Sandbox.Instance.Bodies()) if (b.nodes.Any(n => n != null && n.name.Contains("OMNI") || n != null && n.name.Contains("Omni"))) sb.Append(" wheel w=").Append((b.rb.angularVelocity.magnitude * 60 / (2 * Mathf.PI)).ToString("0")).Append("rpm");
return sb.ToString();
