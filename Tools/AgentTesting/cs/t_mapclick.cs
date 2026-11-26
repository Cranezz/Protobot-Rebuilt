// (Play, match setup) clicks the setup panel's field map at field point Temp/mapclick.txt "X Y" (inches, field frame).
var a = System.IO.File.ReadAllText("Temp/mapclick.txt").Trim().Split(' ');
var p = new Vector2(float.Parse(a[0]), float.Parse(a[1]));
var pic = GameObject.Find("Game Setup").transform.GetComponentsInChildren<RectTransform>(true);
RectTransform map = null; foreach (var r in pic) if (r.name == "Picture" && r.GetComponent<UnityEngine.UI.RawImage>() != null) map = r;
if (map == null) return "no map";
var local = p / 152f * map.rect.width;
var screen = RectTransformUtility.WorldToScreenPoint(null, map.TransformPoint(local));
var e = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = screen, button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
UnityEngine.EventSystems.ExecuteEvents.Execute(map.gameObject, e, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler); UnityEngine.EventSystems.ExecuteEvents.Execute(map.gameObject, e, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
var g = Protobot.Game.SkillsGame.Current;
return "clicked " + p + " at screen " + screen + " -> quadrant " + g.StartQuadrant + " robot at (" + g.x.ToString("F1") + ", " + g.y.ToString("F1") + ") spot " + (g.SpotNow() ?? "-") + " legal " + g.Legal;
