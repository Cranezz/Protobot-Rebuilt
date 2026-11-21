# Odometry tests (tracking wheels + Inertial)

Play mode, from `Tools/AgentTesting`:

- `bash odom/setup.sh` stops Play, compiles, plays, loads the 6-motor drive and builds the odometry pods
  (`Editor/OdomRig`: two Rotation-sensor pods with 2.75" omnis on 3x3 angle brackets, rubber bands, an Inertial
  sensor). `bash odom/setup.sh play` skips the compile.
- `bash odom/run.sh odom/<program>` runs a program through `cs/t_odom_run.cs` and prints its `mark` lines with the
  truth (where the build's origin really is, start frame, x right / y forward, heading clockwise). Placeholders:
  `{config}` (Insert devices, Python), `{cppconfig}` (PROS), `{vert}` `{horiz}` `{imu}` ports, `{voff}` `{hoff}`
  (`{voffabs}` `{hoffabs}`) the wheels' offsets from the origin, `{left_ports}` `{right_ports}`. A first line
  `// period: autonomous` runs it as autonomous.
- Programs: `py_straight.py` / `py_reverse.py` (tracking wheel vs truth at 20/50/100%), `py_lemlib.py`,
  `pros_lemlib.cpp`, `ez_odom.cpp` (the same path in the three styles), `py_follow.py` (pure pursuit poses),
  `py_creep.py` (drive, stop, stand still), `py_rest.py` (still from the start), `py_sign.py`.
- `bash odom/still_run.sh "0.6,0.6"` drives with test sticks, lets go and prints the pose (`cs/t_still.cs`).
- `cs/t_rollcount.cs` (free wheels' contact/near/free steps), `cs/t_wheelprobe.cs`, `cs/t_samples.cs`,
  `cs/t_odom_kind.cs` (re-band: Temp/bandkind.txt "V32,3"), `cs/t_saveodom.cs` / `cs/t_loadodom.cs` (the sample
  build `Sample Builds/Odometry Robot (tracking wheels + IMU).pbb`), `cs/t_odom_example.cs` (Insert devices' tracking
  wheel lines), `cs/t_sens3w.cs` (every 3-wire sensor and an expander in the three languages).

Results (Build 66): tracking wheel 99.7-99.9% of true distance forward, 99.4% backward; the LemLib path within
0.05-0.15" (PROS), 0.1-0.6" (Python, EZ) after ~170" with a spin; a robot let go of holds within 0.01" / 0.03° in 10 s.
