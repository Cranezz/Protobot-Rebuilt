# Events and tournaments: the plan

A community member wants to run "Robot in 1 Hour": everyone builds a robot in an hour, then they play each other in a
bracket. This plan makes that (and ordinary tournaments with robots people already have) something anyone can host
from inside Protobot, the way a real VEX event runs, without needing their own Discord server.

Nothing here is built yet. It uses what Protobot already has: online matches (Lobby + Relay), accounts, Cloud Save and
Cloud Code (the server side that can't be cheated), the notice cards (top right), replays and the leaderboards page.

## 1. What a player sees

**Joining.** The host shares an event code (or a link, pasted in Discord). In Protobot: home menu > **Events** > Join
with a code (or click an event in the public list, if the host listed it). Joining is possible any time before the
event starts, and the player can close Protobot and come back: the event remembers them.

**The event page** (one place for everything):
- the event's name, host, rules in plain words ("Build a new robot in 1 hour", "Any robot", "2v2, random partners")
- a big countdown to the start, shown in the player's own time ("Starts Saturday 3:00 PM, in 2 h 14 min")
- **Who's in**: everyone who joined, with a dot for their robot's state (no robot yet / building / ready, locked in)
  and their badges
- **Your robot**: Choose a robot... (an old build, when the event allows it) or Start building (when it wants a new one)
- the bracket, once the event has started, live: who plays who, scores, who's through
- Leave event

**When the event starts** (the countdown ends, or the host presses Start now):
- *Build events*: a build timer starts for everyone. Protobot opens a fresh, empty build tied to the event; the timer
  sits at the top. When it runs out, the robot is locked in (saved as it is). Building ends early with "I'm done".
- *Any-robot events*: you have until your first match is called to lock a robot in (Choose robot > Lock in).
- Rules are checked when a robot is locked in, with the reason shown when it fails: it has a Brain, it fits the
  18" x 18" x 18" start size, legal parts only (the host can loosen these).

**Your match is up.** A notice card (and a sound) wherever you are in Protobot: "Your match vs Alex is up next: join in
2:00". Join puts you in the match, with your locked-in robot, on your side of the field. Your Alliance is shown, like
the lobby today. Then it's a normal online match (15 s autonomous, 1:45 driver), the score is sent in by both players
and checked by the server, and the bracket moves on.

**Not ready?** Like a real tournament, the match doesn't wait forever:
- When it's called, the match waits for the **call time** (the host sets it; 2 minutes by default).
- If you still aren't in (or have no robot locked in) when the countdown starts, you're a **no-show**: the match is played
  without you. That's VEX's rule (T5: a robot that isn't at the field for the match is a no-show; there's no grace
  period).
- In the elimination rounds each player (or alliance) has **one time-out of up to 3 minutes**, as VEX allows in
  eliminations.
- The host can always wait longer, re-run a match or award it, like a head referee.

**Afterwards.** The winners' screen (the score reveal's style, with the champion's robot turning on the field), and the
event goes into **Events > Champions**: a list like the skills leaderboard, newest first: the event, the date, the
winner (and finalists), their robot's picture, the bracket, and the final's replay.

## 2. What the host sets up

Home menu > Events > **Host an event** (one window, sensible defaults, everything changeable until the start):
- **Name** and a short description.
- **When**:
  - Start now (with robots people already have).
  - Start in... (15 min, 30 min, 1 h, 2 h, or typed).
  - Start at a date and time. It's saved in UTC and shown in everyone's own time zone, so "8 PM my time" works for
    players everywhere.
- **Robots**:
  - **Any robot** (bring a saved build).
  - **Build a new one**: a build timer (1 hour by default; 30 min to 4 h). Saved groups and imported parts can be
    allowed or not.
  - Size and legal-parts checks on or off.
- **Matches**:
  - 1v1, or 2v2 with random partners or partners who join together.
  - Match length: VEX standard, or driver only.
  - Call time (2 min by default).
- **Bracket**:
  - Single elimination (quickest).
  - Double elimination (everyone plays at least twice).
  - Round robin.
  - Qualifications then eliminations (VEX style: a few qualification matches, then the top seeds into a bracket).
  - Seeding: random, or by skills score.
- **Who can join**: anyone with the code (shown on the event page, with a **Copy invite** button for Discord), and
  optionally listed in the public events list. Up to 32 players (the bracket grows to fit; byes for odd numbers).

During the event the host has a small control panel:
- Start now, or pause.
- Call the next match, or let matches run themselves (automatic: the next match is called as soon as both players are
  free).
- Re-run a match, or award it.
- Remove or disqualify a player.
- Post a message to everyone in the event; it goes out on the notice cards.

**The host doesn't have to stay online.** The event lives on the server, and matches are hosted by one of the two
players. If the host leaves, it keeps running automatically.

## 3. How it fits together (for building it)

- **The event** is a Cloud Save item on the server (custom data "events/<code>"). Only Cloud Code scripts write it:
  `CreateEvent`, `JoinEvent`, `LeaveEvent`, `LockRobot`, `StartEvent`, `CallMatch`, `ReportResult`, `HostAction`. It
  holds the settings, the roster, each player's robot state, the bracket and the results. The app reads it every few
  seconds while the event page is open, and on the presence heartbeat otherwise (that's how "your match is up" reaches
  you anywhere in Protobot).
- **Starting on time**: an event saved with a start time is started by the first app that sees its time pass (Cloud
  Code checks the clock, so nobody can start it early). A UGS schedule is the backup, for when nobody's online.
- **Build events**: the event build is a new build made at the start, carrying the event id and the start and lock
  times (`BuildData`). Locking it in sends it to the server (like the robot packet an online match sends today), so the
  robot that plays is the one locked in. Saved groups and imports are refused in it when the host said no.
- **Matches** are today's online matches, opened from the event: the higher seed's app hosts the Relay session, the other
  joins by the code the event gives it, with the robots that were locked in. Both apps send the final score.
  `ReportResult` accepts it when they agree, or flags the match for the host when they don't. Disconnects follow today's
  rule: the robot stays on the field switched off, and Rejoin gives it back.
- **No-shows and time-outs** are clocks the server keeps per match: called at, countdown at, a time-out used. The app
  shows them; the server decides.
- **Champions**: when an event ends, Cloud Code adds it to a "champions" list (public custom data). The final's replay is
  saved like the skills leaderboard's replays.
- **Players under 13** keep their made-up name and no voice chat, as everywhere online. Event names and descriptions go
  through the name filter.
- **Costs**: an event is a handful of Cloud Code calls a minute while people are on its page, well inside the free
  tier. Matches use Relay exactly as today.

## 4. In what order

1. **Events with robots people have.** Host / join by code, the event page, roster, single elimination, the "your
   match is up" card, matches through today's online play, results, the bracket, the winners' screen.
2. **Scheduled starts and build events.** Start in / at a time zone-proof time, the build timer and lock-in, rule checks.
3. **Call times, no-shows and time-outs.** The host's panel: re-run, award, disqualify, messages.
4. **Champions page.** More bracket kinds: double elimination, round robin, qualifications then eliminations, 2v2
   alliances.
5. **Extras.** Spectating a match live, and posting the bracket and winners to the Discord #event-info channel through
   Proto Bot.

## 5. Discord (done 2026-11-26)

- **Event Coordinator** role. It has no server-wide powers except making Discord scheduled events. In the
  **🏆 EVENTS** category (under COMMUNITY) it can:
  - make, edit and delete channels, and set who sees them (its "Manage Permissions" covers only that category's
    channels)
  - pin and tidy messages and threads
  - run the category's voice channels (mute, deafen, move members)
- **#event-info**: everyone reads it, coordinators post. Coordinators add whatever else the event needs (sign-ups,
  match chat, voice rooms) inside the category.
- Made by `Tools/Discord/setup_server.py roles event`.
- **Give the role**: Server Settings > Members > the member > + > Event Coordinator.
