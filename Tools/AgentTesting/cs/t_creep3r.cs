// Drift since t_creep3.cs (a still robot in the sandbox): inches, degrees and per second.
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var s = UnityEngine.PlayerPrefs.GetString("t_creep3").Split(',').Select(float.Parse).ToArray();
var dx = rb.position.x - s[0]; var dz = rb.position.z - s[1];
var turn = UnityEngine.Mathf.DeltaAngle(s[2], rb.rotation.eulerAngles.y);
var secs = UnityEngine.Time.time - s[3];
var d = UnityEngine.Mathf.Sqrt(dx * dx + dz * dz);
return secs.ToString("0.0") + " s: moved " + d.ToString("0.000") + "\" (" + (d / secs).ToString("0.0000") + "\"/s), turned " + turn.ToString("0.00") + "°, v=" + rb.linearVelocity.ToString("F4") + " w=" + rb.angularVelocity.ToString("F4");
