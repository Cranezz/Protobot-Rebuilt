// Host side: the editor's robot gets the online test program.
Protobot.RobotCode.CodeStore.Set(System.IO.File.ReadAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/online_test_program.py"));
return "code " + Protobot.RobotCode.CodeStore.Source.Length;
