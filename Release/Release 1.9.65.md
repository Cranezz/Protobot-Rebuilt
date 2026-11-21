# Protobot Rebuilt V2, Beta 1.9 (build 65)

- **C++ robot code:** write PROS (with LemLib or EZ-Template) or VEXcode V5 C++ and run it on the simulated robot, the
  same code you'd download to the real one. Python and VEXcode Blocks work too.
- **EZ-Template:** PID drive, turn and swing, exit conditions, motion chaining, odometry with pure pursuit and
  boomerang moves, and the autonomous selector on the Brain screen. v3 and v2 names both work.
- **LemLib:** 0.4, 0.5 and today's versions, including path files from path.jerryio.
- **Whole projects:** Code > Project > Open a project folder loads a PROS or VEXcode project with all its files.
  Errors name the file and line.
- **VEXcode Blocks:** drag-and-drop blocks laid out like VEXcode's: My Blocks, lists, comments. "Show the Python" or
  "Convert to a text program" when you're ready.
- **Debugging:** the Monitor tab shows each thread, the line it's on and your variables. Pause (F6) and Step (F10)
  the robot. Console colors work.
- **VS Code:** opens your code with autocomplete for PROS, VEXcode C++, LemLib and EZ-Template.
- **Sensors simulated:** Vision Sensor (signatures, color codes, the Vision Utility), GPS Sensor (reads the field
  strip, with its quality), and the new AI Vision Sensor (colors, codes, and the game's pins and cups found by its AI
  model). The AI Vision Sensor is now in the parts list with VEX's own model.
- **Insert devices** writes the configuration for every sensor, including where the GPS is mounted.
- **Gamepads and USB joysticks** drive the robot; the controller screen shows each button and what it does.
- **Online matches:** each player's robot runs its own program in autonomous, then driver control.
- **Driving:** omni wheels slide sideways like real ones, and a robot standing still no longer creeps.
