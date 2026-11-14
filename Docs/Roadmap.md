# Roadmap (2026-11-14)

Progress: Phase 0, Phase 1 and Phase 2 are done (2026-11-14, see each phase). Next: Phase 3 needs the user (GitHub).

Everything still open, in the order it'll be done. Each phase ends with a play-test, a build-number bump and a commit.
"You" = the user; everything else is done here.

## Decisions already made

- **Shoulder screws:** sizes and head colors come from RoboSource's shoulder screws (their store pages are the reference).
- **License plates:** the letter notches stay (they're in VEX's model).
- **Sensor mounting holes:** found from the models: every place people put a screw to mount a sensor gets a hole
  (line sensor bar, ultrasonic, optical shaft encoder, Line Tracker V2, and the rest of the sensors checked the same way).
- **Online:** each computer simulates its own robot and what it touches; the host settles pieces and scores once.

## Phase 0: housekeeping (short) - DONE

1. Commit Build 23 (ownership sync, reports, vote kick).
2. Find what changes the 12" shaft template to 6" while the app runs (a part generator writing to the prefab asset
   instead of its copy) and stop it.

## Phase 1: parts (1 session) - DONE (shoulder screws colored + 2.50", sensor holes from openings, Double bogie rocker, zip ties, plate undo, labels apart; spacer case re-checked: hole walls match the model to 0.001")

1. **Shoulder screws:** read RoboSource's shoulder screw listings (lengths, shoulder lengths, head colors), add the
   missing sizes as parts and color each head by RoboSource's code.
2. **Sensor mounting holes:** go through every sensor model (V5 and 3-wire) and add holes where screws go: the line
   sensor bar, ultrasonic, optical shaft encoder, Line Tracker V2 first.
3. **Double bogie rocker:** its two wheels' arm pivots on the middle pin (in the sandbox too).
4. **Small gaps:** zip ties for tubing, undo for license-plate text, motor labels not overlapping on a 6-motor drive.
5. **Re-check:** the spacer-on-a-screw-inside-a-C-channel case (believed fixed).

## Phase 2: online matches ready for real players (2-3 sessions) - DONE except the laptop test (you) and a real Discord report

1. **Start setup online:** each player places their robot within the legal start (the same wall lock, slide, turn,
   Line up and Measure as Match practice) before the match; the host starts when everyone says ready.
2. **Alliance scores:** red and blue scored separately (Override's match rules), shown on the HUD and in the reveal.
3. **Rule checks:** pinning (red outline on the pinning robot, the manual's count, then disabled), disabling a robot,
   DQ; the host decides, everyone sees it.
4. **Rejoin:** a player who left gets their robot back (same account, same match).
5. **Tests:**
   - **Two robots in contact:** shoving each other, and a fight over one pin.
   - **A full 2v2:** four copies.
   - **A real report:** one test report sent to Discord, labelled as a test.
   - **You:** join a match from your laptop on another network (a phone hotspot is enough) and tell me how driving feels.

## Phase 3: putting the app out (1-2 sessions)

1. **GitHub:** a repository with the part meshes and field in Git LFS (they're already marked); a README.
   - **You:** create the repository (or say which account and whether it's public) and run `gh auth login` once.
2. **Installer and updates (Velopack):**
   - The app checks for a new version at start and offers to update.
   - It downloads only what changed, then restarts.
   - Releases go on GitHub Releases.
3. **Links:** the "download the latest version" link (version mismatch) and the bug-report page point at the GitHub
   page.

## Phase 4: accounts, stats and the skills leaderboard (2-3 sessions)

1. **Accounts:**
   - Sign-in through Unity (anonymous accounts upgrade to a username).
   - Stats kept in Cloud Save, written only by Cloud Code, so players can't edit them.
2. **Skills leaderboard:**
   - A run is checked by Cloud Code before it counts: the robot fits the size rules, and the score is recomputed from the recorded positions.
   - Replays come from the recorded positions (small files).
   - Reports and DQ (major rule) / warnings (minor), with appeals, go through the Discord channel.
3. **Match stats:** wins, points, matches played, kept per player.
   - **You:** switch on Cloud Code, Cloud Save and Leaderboards in the Unity dashboard (I'll give the exact clicks).

## Phase 5: parts menu (1 session)

- **Layout:** the two-panel menu like the original Protobot: the main list, then a smaller panel beside it with the
  sub-categories and sizes.
- **Pictures:** flat gray orthographic part pictures (spacers #CCCCCC), with new ones for click-on spacers, couplers,
  the rubber link, shoulder screws, brackets, license plates, wheels, ball bearings, fittings, cylinder mounts and field parts.

## Phase 6: demo robot (1 session)

- **The robot:** one that plays Override in Play game: picks up pins and cups (raised intake and hopper) and flips the
  Toggles (an arm reaching about 13").
- **Uses:** the sample build, the multiplayer test robot and the trailer picture.

## Phase 7: controller screen (1 session)

- **The screen:** an on-screen V5 controller: click a button or stick, press a key or a gamepad button to bind it (like
  Steam's controller screen).
- **Where it's saved:** per robot, with presets for tank, arcade and split arcade.

## Phase 8: voice chat (1 session)

- **Channels:** Vivox alliance channels (you hear only your partner): push-to-talk or open mic, a mute per player, and
  no microphone needed to play.
  - **You:** switch on Vivox in the Unity dashboard.

## Phase 9: robot code (the biggest: 4+ sessions)

1. **Simulated V5 hardware:** motors, sensors and the controller, with the robot's ports from its wiring.
2. **Robot code in the app:**
   - Python (VEXcode / PROS-like API) first, then C++ (PROS / VEXcode API) through a sandboxed interpreter.
   - Blocks after that.
3. **Code editor:** a VS Code-style editor tab, with "open in my own VS Code" and file watching.
4. **Autonomous:** the period in matches and skills runs the robot's code.
5. **Tuning:** PID and other tuning carry over to the real robot (close, not identical).

## Checks still owed (done when their phase comes round)

- **Lighter models:** frame times before and after on the Benchmark tab, and memory in a built app (Phase 0 or 3).
- **Saw on cleaned parts:** a saw cut on a part with its hidden insides removed shows it hollow; decide whether to fill
  cut faces (Phase 1).
- **Alignment tool:** lining up against a real alignment jig, and loading a saved group as the tool (Phase 2, during
  online start setup).
