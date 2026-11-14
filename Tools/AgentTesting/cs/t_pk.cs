var m = Protobot.Online.OnlineMatch.Current; if (m == null) return "no match";
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var t = m.GetType();
var packets = (System.Collections.IDictionary)t.GetField("packets", bf).GetValue(m);
var partial = (System.Collections.IDictionary)t.GetField("partial", bf).GetValue(null);
var sb = new System.Text.StringBuilder("packets " + packets.Count + " keys:");
foreach (var k in packets.Keys) sb.Append(" " + k);
sb.Append(" | partial " + partial.Count + ":");
foreach (System.Collections.DictionaryEntry e in partial) { var arr = (byte[][])e.Value; int got = 0; foreach (var a in arr) if (a != null) got++; sb.Append(" " + e.Key + " " + got + "/" + arr.Length); }
sb.Append(" | net clients " + Unity.Netcode.NetworkManager.Singleton.ConnectedClientsIds.Count);
return sb.ToString();
