var token = Unity.Services.Authentication.AuthenticationService.Instance.AccessToken;
var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
while (part.Length % 4 != 0) part += "=";
var json = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(part));
var i = json.IndexOf("\"aud\"");
return json.Substring(i, System.Math.Min(260, json.Length - i));
