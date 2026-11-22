// Checks Online.NameFilter: names that must pass and (rot13, so this file reads clean) names that must be refused.
System.Func<string,string> Rot = s => { var c = s.ToCharArray(); for (int i = 0; i < c.Length; i++) { var x = c[i]; if (x >= 'a' && x <= 'z') c[i] = (char)('a' + (x - 'a' + 13) % 26); else if (x >= 'A' && x <= 'Z') c[i] = (char)('A' + (x - 'A' + 13) % 26); } return new string(c); };
var good = new[] { "Speedy Gear 42", "Cockpit", "Assembly Bot", "Grapefruit", "Nigeria Robotics", "Bob's Robot", "Classic", "Dickens", "Sussex", "Scrapheap", "Canal", "Basement", "Raccoon", "Titan", "Cranezz", "11777C", "Mr. Spock", "Hancock", "Therapist", "Shiitake", "Pass Bot", "Spice Rack", "Cumulus", "Grape", "Shellfish", "Analysis" };
var bad = new[] { "shpx", "SHPXre", "s.h.p.x", "Fuvgg", "o1gpu", "Ovt Nff Obg", "nff_obg", "NffObg", "n f f", "Puvax", "c0ea", "Uvgyre Snaf", "phag", "frkl ebobg", "Encr", "avttre", "s4tt0g" };
var sb = new System.Text.StringBuilder();
int wrong = 0;
foreach (var g in good) if (!Protobot.Online.NameFilter.Allowed(g)) { wrong++; sb.Append("REFUSED good: " + g + "\n"); }
foreach (var b in bad) { var n = Rot(b); if (Protobot.Online.NameFilter.Allowed(n)) { wrong++; sb.Append("ALLOWED bad #" + System.Array.IndexOf(bad, b) + "\n"); } }
sb.Append(wrong == 0 ? "all " + (good.Length + bad.Length) + " right" : wrong + " wrong");
return sb.ToString();
