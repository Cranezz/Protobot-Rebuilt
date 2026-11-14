# Online play: the plan

Everything runs on players' computers plus Unity Gaming Services (UGS) free tiers. There are no servers of our own.
If the game grows past the free tiers, the provider can be swapped (Epic Online Services is the free fallback), so the
online code sits behind our own classes (`Scripts/Online`).

| Piece | Service | Free tier (check Unity's pricing page) |
|---|---|---|
| Connecting players to the host's computer | Unity Relay | ~50 average concurrent players a month |
| Server list, invite codes, passwords | Unity Lobby (through the Multiplayer Services "sessions") | included with Relay use |
| Sign-in, accounts | Unity Authentication | free |
| Stats players can't edit, replays | Cloud Save (protected data, written only by Cloud Code) | 5 GiB stored, 1M writes, 1M reads a month |
| Checking runs and results on the server side | Cloud Code | 1M calls, 20 compute hours a month |
| Skills leaderboard | Leaderboards | listed as free for now |
| Team voice chat | Vivox | free up to 5,000 peak concurrent users |

## 1. Lobby (done)

Play game > Play online…, or File > Play online….
- **Your name.** Kept on this computer.
- **Server list.** Each match shows its name, host, mode, players and a password tag. Matches on another version
  show "Newer version" / "Older version" instead of Join, and clicking it explains. The list refreshes every 15 s.
- **Invite code.** Paste a code and press Join. There's no domain needed: the code is the invite.
- **Host a match:**
  - name
  - mode: 2v2, 1v1 or Skills race
  - password: optional, 8+ characters, checked by Unity's lobby service
  - invite only (not in the server list)
- **Match lobby:**
  - the invite code, big, with a **Copy invite** button (a message to paste in Discord)
  - players, each with their alliance, robot and ready
  - pick Red/Blue, pick your robot (the build open in Protobot), Ready
  - the host can remove players, and Start is enabled when everyone is ready
- **Versions.** Each match stores the host's build number. Joining another version is refused with a message saying
  who needs to update. `MatchSessions.DownloadUrl` adds a "Download the latest version" button once there's a download
  page (GitHub Releases works, no domain needed).
- **Before it works:** the Unity project must be linked to a Unity Cloud project with Relay and Lobby turned on.

## 2. Accounts and the skills leaderboard

- **Accounts.** A username and password through Unity Authentication, so a player keeps their scores on any
  computer. Google/Steam/Unity sign-in can be added later. The anonymous sign-in moves into the account.
- **Stats players can't edit.** Each player's stats live in Cloud Save's *protected* data:
  - players can read them, but only our Cloud Code scripts can write them
  - stats: matches played, won, lost and disqualified, ranking points, best skills score
- **Online skills run:**
  1. The robot is checked before the run: size at the start (18" × 18" × 18"), the game's other robot rules we can
     measure, and the build's fingerprint (hash).
  2. The run is recorded.
  3. At the end, the score, the recording and the build's hash are sent to Cloud Code. It checks them (version, time,
     the score recomputed from the recording's final positions, the size) before it posts the score to the
     leaderboard.
- **Replays (possible within the free tier).** A replay is not a video: it's the poses of everything that moved,
  30 times a second, compressed.
  - Size: a 60 s run with ~15 moving bodies is about 150-300 KB, plus the robot's build.
  - Only the top 100 runs per leaderboard keep their replay (about 30-50 MB in all).
  - Watching one rebuilds the robot and plays the poses back on the field, with a free camera.
- **Reporting a run.** A Report button on each leaderboard row:
  - it sends the run, its replay and the reason to the Discord channel through the bug report webhook
  - the run is marked "under review"
  - you decide in Discord, and a small Cloud Code admin script sets it Disqualified or OK
- **Rule checks during a run.** Each rule is major or minor:
  - **Major** (e.g. the robot too big at the start, a banned part): the run is disqualified at once.
  - **Minor** (e.g. holding too many game pieces, expanding too far): a timer runs while it's broken. A moment
    (under ~1 s) or no points gained during it: just a warning in the run's log. Points scored while breaking it:
    disqualified.
  - **Appeals:** a disqualified run keeps its replay and can be appealed (it goes to Discord the same way).
- **Protecting the leaderboard (decided).** Protobot is open source, so anyone can change their copy. The aim is
  that nobody can just *edit a score or a run file*; a run cheated inside the game is left to players' reports.
  - The app never writes to the leaderboard. Unity's Leaderboards access control blocks writes from players, and only
    our Cloud Code script can post a score.
  - The run goes up straight from memory when it ends: the replay (inputs + poses) and the build. A replay saved on
    disk is only for watching and is never sent.
  - Cloud Code works out the score itself from the replay's final positions, with the same scoring rules as the game.
    A score number sent with the run is ignored, so changing it does nothing.
  - Cloud Code also checks that the replay hangs together: 60 s long, nothing jumping or moving impossibly fast, the
    robot's size at the start, one version. A hand-made or edited replay fails these.
  - What's left (a modified copy that plays a "real" run with a stronger robot or a cheat) is caught by players:
    Report on the run, the replay goes to Discord, you decide.

## 3. 2v2 matches

**Built (2026-11-14, first version, `Scripts/Online/OnlineMatch.cs`):**
- The host presses Start match: seats by Alliance and join order (red Left then Bottom, blue Right then Top).
- Every computer opens Match practice round its own robot, sends its build (gzipped JSON, in 1 KB pieces) and loads the
  others' robots at their default starts. The host simulates everything; the others don't simulate and follow the
  host's poses (20 a second, in the field's frame, smoothed); their controls go to the host 30 times a second.
- Preloads and Match Loads are made by the host and announced. The host takes the score and sends the final
  positions, so every computer shows the same score; then everyone goes back to the lobby.
- A player who leaves mid-match: their robot stays, switched off (no controls arrive). The host leaving ends it.
- Tested with two copies on one computer: both robots end within 0.01" of each other on both screens, every body is
  followed, two matches in a row, a player quitting mid-match, a Match Load from the joining player.

**Ownership (built 2026-11-14):** each player's computer simulates their own robot and the pieces it touches (the host
settles who owns a piece), so driving and picking up have no delay for anyone, the host included; the others follow at
the owner's positions carried on by their speeds. Robot vs robot: you feel the other robot as solid at once, it moves
when its owner's computer gets the push. Final positions come from each owner; the host scores once. The host checks
robot moves (impossible speeds, jumps) and records them for reports. Player reports (to the Discord channel, with the
match record) and vote kick (3+ players, more than half of the others) are built.

**Not built yet:** setting up your start in an online match (everyone starts at the default), alliance scores (red
vs blue: the score is Match practice's for now), autonomous (needs robot code), Rejoin, rules (pinning, disabling),
stats.

The plan:

- **Host authority.** The host's computer runs the field and all four robots (`Sandbox` with every robot):
  - Each player sends their controls about 60 times a second.
  - The host sends the poses of moving bodies about 30 times a second.
  - Your own robot is predicted locally so driving feels immediate.
  - Expected traffic: about 20-40 KB/s per player.
- **Match flow:** lobby, then robots placed at their starts, 15 s autonomous, 1:45 driver control, then the score.
  `OverrideScoring` already scores the field.
- **Rules during a match:**
  - **Major:** the robot is disabled (it stops, can be pushed around, and its driver watches).
  - **Minor:** a warning with a timer. For example, pinning: the pinning robot gets a red outline and a countdown
    (5 s, from the manual), and must back away. If it doesn't, it's disabled.
  - **DQ:** a disqualified team gets no points from the match.
- **A player who leaves mid-match.** Their robot stays on the field where it is, switched off: no driving, motors and
  cylinders hold still, and it can be pushed around like any object. The match goes on and the alliance keeps playing
  with one robot. If they come back (Rejoin, same account) before the end, they get their robot back.
- **Results and stats.** Every player's copy sends the final result to Cloud Code, which only counts it when they
  agree, so a cheating host can't simply report a win. Cloud Code then updates each player's protected stats.
- **Voice (optional).** Vivox team channels: you hear only your alliance partner, with push-to-talk or open mic, a
  mute per player, and no microphone needed to play.

## 4. Controllers

- An on-screen V5 controller: click a button or stick, then press a key to bind it, like Steam's controller
  settings.
- Any gamepad works through Unity's Input System: Xbox, PlayStation, generic.
- Several layouts can be saved, and the layout used in a match is the one shown in its control card.

## 5. Your robot's real code

The goal: the code from your real robot runs on the simulated robot (autonomous, PID, odometry), with the sensors it
uses.

- **C++ (PROS, VEXcode).** The player's project is compiled on their PC with a compiler bundled with Protobot,
  against our own versions of the PROS and VEX libraries (`pros/api.h`, `vex.h`) that read and drive the simulated
  robot:
  - **Libraries:** LemLib, EZ-Template and JAR-Template compile from source on top of those libraries.
  - **Sandboxing:** the code is compiled to WebAssembly and run in a sandbox, so code can't touch the computer.
    In matches, each player's code runs on their own PC and only sends motor commands, like a controller.
- **Python (VEXcode Python):** an embedded interpreter with the same libraries.
- **Blocks (VEXcode):** VEXcode can show a blocks project as Python or C++, and that code would be used.
- **Simulated hardware:**
  - motors as the V5 firmware runs them: velocity/position control, current limit, brake modes
  - rotation sensors (tracking wheels), the inertial sensor with drift and noise, distance and optical sensors,
    pneumatics
  - the 10 ms loop and tasks
  - Ports come from the wiring already in Protobot: a motor on port 11 is `pros::Motor(11)`.
- **Code editor:**
  - An in-app tab shows and edits the code (syntax colors, line numbers, errors from the compiler).
  - Or edit in your own VS Code: Protobot watches the files and rebuilds when you save.
  - A real VS Code look (Monaco) would need a paid embedded-browser plugin, so the built-in editor would be our own,
    simpler one.
- **Honest limit.** It behaves like the real robot as closely as the physics model allows (motors, friction, weight).
  PID gains tuned in Protobot will be close to the real robot's, not identical.

## Order

1. Lobby (done; linked to Unity Cloud and tested with two copies).
2. Accounts, the skills leaderboard, rule checks, replays, reports.
3. Robots synced over the network, 2v2 match flow (first version done), rules in matches, stats.
4. Voice chat.
5. The controller screen.
6. Robot code: the libraries and simulated hardware, the compiler and sandbox, the editor tab, file watching.
