var token = Unity.Services.Authentication.AuthenticationService.Instance.AccessToken;
var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
while (part.Length % 4 != 0) part += "=";
var json = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(part));
var keep = new System.Text.StringBuilder();
foreach (var key in new[] { "environment_id", "environment_name", "project_id", "aud", "iss" }) {
  var i = json.IndexOf("\"" + key + "\"");
  if (i >= 0) keep.Append(json.Substring(i, System.Math.Min(110, json.Length - i)).Split(new[] { ",\"" }, System.StringSplitOptions.None)[0]).Append("  ");
}
return keep.ToString();
