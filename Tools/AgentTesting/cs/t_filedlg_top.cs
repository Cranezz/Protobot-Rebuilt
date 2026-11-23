// #5: needs a temporary editor class TmpDlgTest.Run() (EnumWindows/GetWindowLong P/Invokes) that opens SFB.StandaloneFileBrowserWindows.SaveFilePanel,
// and from another thread checks the "#32770" dialog has WS_EX_TOPMOST (0x8) and closes it (WM_CLOSE). Result with the fix: "dialog topmost=True".
return TmpDlgTest.Run();
