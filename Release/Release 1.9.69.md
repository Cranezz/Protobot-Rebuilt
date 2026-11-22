# Protobot Rebuilt V2, Beta 1.9 (build 69)

- **Odometry robot:** a new sample build with tracking-wheel pods (a Rotation sensor as the arm, a rubber band holding
  the wheel down) and an Inertial sensor. Unpowered tracking wheels now roll true (within 0.1-0.3% of the real
  distance), and tracking wheels no longer count up while the robot stands still (before a run or after a stop). A Rotation
  sensor read the moment a program starts gives its real angle (it gave the last run's).
- **New example, "Odometry: 4 points and back":** drives to four points, each with its own heading, stops 2 s at
  each and writes down how close it got (in the output and on the Brain's screen), then comes home. On the odometry
  sample robot the four points come out within about 1/4" and a fifth of a degree (home, after a minute and a
  half of driving, within about 2/3": odometry drifts a little over time, as on a real robot). It lines up with each goal's heading before driving
  in, and tries again if it misses, aiming to make up for the miss.
- **Tracking wheels stay upright:** they leaned about 1.5 degrees in their Rotation sensors while driving; now they
  sit square, as built.
- **Insert devices measures from the middle of the drive wheels:** tracking wheel and GPS offsets were measured
  from the drive motors, which can sit inches from the wheels on a geared drive.
- **Quieter motors:** V5 motors are quiet, and now they sound it. Several running motors no longer add up to a roar.
- **Python fix:** a line that continued inside brackets (a long print(...) over two lines) could break the line
  after it ("expected the end of the line").
- **Robots stop where you let go:** brake mode really brakes, so a robot doesn't keep turning after the sticks go.
- **Every sensor wired:** 3-Wire Expanders, Accelerometers and every 3-wire sensor work in Python, VEXcode C++ and
  PROS. Devices keep their ports when you reopen a build.
- **Games, the robot:** a robot needs a V5 Brain, and only what's connected to it goes on the field (stray parts
  elsewhere in the build stay behind). A Pin saved on your robot is your Preload, where you put it; more game
  elements than that must go first.
- **Games, setup:** place the Preload anywhere on the robot ("Place on the robot…": click a face, R stands it up or
  lays it down, Q/E turn it). The setup shows your robot's height. Saved starts show their names. "Clear of the
  Toggle" checks real contact. Start run stays at the bottom of the panel. No grid or axis lines on the field.
- **Loaders:** raise and lower the Loader (a robot takes Match Loads out of its bottom), and a Loader only takes what
  fits in it. Matches have 10 alliance Pins per Alliance, and Match Loads go in during driver control only.
- **Toggles** lift in their brackets and only count when they're fully seated. They keep their colors in replays.
- **Replays:** "Watch a replay" on the home menu, and double-clicking a .pbr (or .pbb) file someone sent opens it in
  Protobot.
- **Cables:** the GPS, Inertial, Vision Sensor, 3-Wire Expander and 5.5W motor have their real smart ports (cables
  went anywhere on them before).
- **Pneumatics:** a cylinder's rod moves with it when you move the cylinder, and tubing can be routed over cylinder
  mounts and rod pivots (their screw holes were taken for air ports).
- **Motors sound like V5 motors:** the gear whine follows the motor's speed, and a motor straining against a load
  whines like the real one.
- **Faster with many Vision Sensors:** cards share their picture updates, so nine open sensors don't slow the app.
- **Multiplayer:** signing in shows your account name, and your match is "<name>'s match". Report a player for
  unneeded or oversized custom parts (the report carries the parts). The background no longer shifts when you
  switch screens.
- **Home menu:** Report a bug opens over it, Patch Notes shows these notes, and the Credits have a V2 section.
