// (after t_odom_build.cs) The pods' rubber bands (OdomRig.Finish: PlayerPrefs "t_odom_band" = "kind,wraps", default V32,3).
var a = UnityEngine.PlayerPrefs.GetString("t_odom_band", "V32,3").Split(',');
try { return Protobot.EditorTools.OdomRig.Finish(a[0], int.Parse(a[1])); } catch (System.Exception e) { return e.ToString(); }
