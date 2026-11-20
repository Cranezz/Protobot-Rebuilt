using System;
using System.IO;
using UnityEngine;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9): headers for a C++ program opened in VS Code, so its IntelliSense (Microsoft's C/C++ or clangd, the
    /// PROS extension's) knows PROS, VEXcode C++, LemLib and EZ-Template as far as the simulator runs them: main.h,
    /// api.h, vex.h, lemlib/api.hpp, EZ-Template/api.hpp (Resources/Robot Code Headers), and the editor settings that
    /// point at them. Nothing is compiled: the app runs main.cpp itself.
    /// </summary>
    public static class CppHeaders {
        private const string MainH = @"// Protobot Rebuilt V2: a PROS project's main.h, written by the app (changes are replaced). The app runs main.cpp
// on the simulated robot; these headers are for VS Code's completions and checks.
#pragma once
#ifndef PROS_USE_SIMPLE_NAMES
#define PROS_USE_SIMPLE_NAMES
#endif
#ifndef PROS_USE_LITERALS
#define PROS_USE_LITERALS
#endif
#include ""api.h""
#include ""lemlib/api.hpp""
#include ""EZ-Template/api.hpp""

#ifdef __cplusplus
extern ""C"" {
#endif
void autonomous(void);
void initialize(void);
void disabled(void);
void competition_initialize(void);
void opcontrol(void);
#ifdef __cplusplus
}
#endif
";

        private const string Empty = @"// Protobot Rebuilt V2 (written by the app): the app keeps the whole program in one file, main.cpp.
#pragma once
#include ""main.h""
";

        private const string RobotConfig = @"// Protobot Rebuilt V2 (written by the app): VEXcode's robot-config. Brain and Controller1 are made by the app;
// make your devices in main.cpp (Insert devices in the code window writes them from the build).
#pragma once
#include ""vex.h""
";

        private const string CppProperties = @"{
    ""configurations"": [
        {
            ""name"": ""Protobot robot code"",
            ""includePath"": [ ""${workspaceFolder}/include"" ],
            ""defines"": [ ""PROS_USE_SIMPLE_NAMES"" ],
            ""cStandard"": ""c17"",
            ""cppStandard"": ""c++20"",
            ""intelliSenseMode"": ""${default}""
        }
    ],
    ""version"": 4
}
";

        private const string Settings = @"{
    ""files.associations"": { ""*.h"": ""cpp"", ""*.hpp"": ""cpp"" },
    ""C_Cpp.default.cppStandard"": ""c++20""
}
";

        private const string Extensions = @"{
    ""recommendations"": [ ""ms-vscode.cpptools"", ""ms-python.python"" ]
}
";

        // clangd (the PROS extension's language server) reads this.
        private const string CompileFlags = "-xc++\n-std=c++20\n-Iinclude\n-DPROS_USE_SIMPLE_NAMES\n";

        /// <summary>Writes (or refreshes) the headers and editor settings into the robot code folder.</summary>
        public static void Write(string folder) {
            try {
                var include = Path.Combine(folder, "include");
                Put(Path.Combine(include, "protobot_sim.h"), Resource("cpp_sim"));
                Put(Path.Combine(include, "api.h"), Resource("cpp_pros"));
                Put(Path.Combine(include, "main.h"), MainH);
                Put(Path.Combine(include, "vex.h"), Resource("cpp_vex"));
                Put(Path.Combine(include, "robot-config.h"), RobotConfig);
                Put(Path.Combine(include, "lemlib", "api.hpp"), Resource("cpp_lemlib"));
                Put(Path.Combine(include, "EZ-Template", "api.hpp"), Resource("cpp_ez"));
                // V2: the headers PROS and LemLib programs include one by one: each brings in the whole API.
                foreach (var h in new[] { "misc.h", "misc.hpp", "motors.h", "motors.hpp", "rtos.h", "rtos.hpp", "adi.h", "adi.hpp", "llemu.h", "llemu.hpp", "imu.h", "imu.hpp",
                                          "rotation.h", "rotation.hpp", "optical.h", "optical.hpp", "distance.h", "distance.hpp", "motor_group.hpp", "screen.h", "screen.hpp" })
                    Put(Path.Combine(include, "pros", h), "#pragma once\n#include \"api.h\"\n");
                foreach (var h in new[] { "logger/stdout.hpp", "logger/logger.hpp", "chassis/chassis.hpp", "chassis/trackingWheel.hpp", "chassis/odom.hpp", "pose.hpp", "pid.hpp", "util.hpp", "asset.hpp" })
                    Put(Path.Combine(include, "lemlib", h), "#pragma once\n#include \"lemlib/api.hpp\"\n");
                Put(Path.Combine(include, "autons.hpp"), Empty);
                Put(Path.Combine(include, "subsystems.hpp"), Empty);
                Put(Path.Combine(folder, ".vscode", "c_cpp_properties.json"), CppProperties);
                Put(Path.Combine(folder, ".vscode", "extensions.json"), Extensions);
                if (!File.Exists(Path.Combine(folder, ".vscode", "settings.json"))) Put(Path.Combine(folder, ".vscode", "settings.json"), Settings);
                Put(Path.Combine(folder, "compile_flags.txt"), CompileFlags);
            }
            catch (Exception e) {
                Debug.LogWarning("[Robot code] couldn't write the C++ headers: " + e.Message);
            }
        }

        private static string Resource(string name) {
            var asset = Resources.Load<TextAsset>("Robot Code Headers/" + name);
            return asset != null ? asset.text : "// (missing: " + name + ")\n#pragma once\n";
        }

        private static void Put(string path, string text) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            text = text.Replace("\r\n", "\n");
            if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == text) return;
            File.WriteAllText(path, text);
        }
    }
}
