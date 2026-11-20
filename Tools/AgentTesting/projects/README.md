# Testing robot code with real team projects

The user's old team code (VEXcode V5 C++ and PROS + LemLib 0.4.7 / 0.5 pre-release projects, Spin Up and Over Under) is in
their Google Drive ("VEX PROGRAMS SPIN UP", shared with them). It isn't in the repository: download it into the
scratchpad when needed.

1. List the project folders (one per line) in a text file; `cs/t_projset.cs` stores its path (edit the path in it).
2. `cs/t_proj1.cs`: translates and compiles every listed project (errors and warnings).
3. `remap_vexcode.py <root> <out> <list>` / `remap_pros.py <out> <list>`: point each project's robot-config at the
   6-motor drive (left 4,5,6 / right 1,2,3 reversed, 3.25" wheels, 60:36, IMU on port 7 after `cs/t_ez_imu.cs`, other
   devices moved off ports 1-7; PROS: no tracking wheels). Writes `<out>/<n>.cpp` (PROS: a project with static/ files).
4. `bash cs/t_run.sh <n> <seconds> [a|d] [limit]` runs `<out>/<n>.cpp` (path set in cs/t_run.sh) and prints where the
   robot went; `bash cs/t_track.sh <n> <seconds>` prints the pose every second; `cs/t_sticks.cs` (+ PlayerPrefs
   "t_sticks" = "lx,ly,rx,ry") drives driver control.
5. `cs/t_projfolder.cs`: CodeProject.FromFolder on some project folders (files, translation, split/join round trip).
