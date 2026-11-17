// Writes the VS Code headers to the folder given in PlayerPrefs "t_hdr_dir" (set here to the agent's scratch folder).
UnityEngine.PlayerPrefs.SetString("t_hdr_dir", @"C:\Users\kille\AppData\Local\Temp\claude\5b0c4ec0a185a9e3\8d21650f-8a3d-4b32-911f-851c0b975844\scratchpad\hdr");
System.IO.Directory.CreateDirectory(UnityEngine.PlayerPrefs.GetString("t_hdr_dir"));
return "ok";
