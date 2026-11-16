# Protobot Rebuilt V2, Beta 1.9 (build 50)

- **Robot code** (Code in the top bar): write your robot's program in Python, VEXcode V5 Python style, and run it on the
  simulated robot. Line numbers, colors, auto-indent, Ctrl+/ to comment, F5 to run, errors shown on their line.
  "VS Code" opens it in your own VS Code (with autocomplete); saving there updates the app. The program is saved with
  the build.
- **Insert devices:** one click writes your robot's configuration at the top of the program: every motor on its port
  with its cartridge and direction, the drive's motor groups and a drivetrain measured from your build (wheel size,
  track width, gear ratio), sensors and pneumatics.
- **Brain screen:** the program can print and draw on the V5 Brain's screen (rectangles, circles, lines, colors, fonts)
  and you can tap it (autonomous selectors work). The controller's screen shows beside it, and rumble shakes it (and
  your gamepad).
- **Tuning graph:** the Graph tab plots the motors your program drives (speed or position against what it asked for)
  and anything it prints as `name: number`, for tuning PID. The same code runs on the real robot.
- **Motion library** (`from motion import *`): odometry and autonomous moves with LemLib's names: move_to_point,
  turn_to_heading, move_to_pose (boomerang), swing turns, pure pursuit along a path, motion chaining, tracking wheels or
  drive motors plus the Inertial sensor, and LemLib's PID settings and exit conditions.
- **Examples** (12): competition template, tank/arcade drive, PID tuning, odometry and motions, an autonomous selector
  on the Brain's screen, controller screen and rumble, and more. They fill in your own robot's devices.
- **Play game:** match autonomous runs your program, then its driver control. New mode **Programming Skills**: a skills
  run driven only by your program, with its own leaderboard (the program goes with the shared replay).
- **Smoother:** the game screens run much faster (on an RTX 3070 Ti at 1440p, 250+ FPS while driving on the full
  field, no frame drops). Replays no longer drop to 30 FPS.
- **Driving feels truer:** omni wheels grip under full power (they used to spin), and robots don't spin slowly after
  coasting to a stop.
