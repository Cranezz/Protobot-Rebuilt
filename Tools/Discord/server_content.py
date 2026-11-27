"""The Protobot Discord server's layout and text: roles, categories and channels (with who may do what), forum
tags, the welcome/rules/FAQ/download messages, the tutorial guides and the pinned "how to" posts. setup_server.py
applies it; edit here and run it again to update (messages are edited in place, not posted twice)."""

BLUE = 0x1E96DC
RELEASES = "https://github.com/Cranezz/Protobot-Rebuilt/releases/latest"
REPO = "https://github.com/Cranezz/Protobot-Rebuilt"
PRIVACY = REPO + "/blob/main/PRIVACY.md"
TERMS = REPO + "/blob/main/TERMS.md"

# Roles, top to bottom (Proto Bot's own role sits just under Admin). "perms" are guild-wide permissions on top of
# @everyone's; channel overwrites add the rest. Nobody below Admin can manage roles, channels, webhooks or the server,
# so nobody can give themselves more power; the self-assigned roles (pings, VEX team) carry no permissions at all.
ROLES = [
    {"key": "admin", "name": "Admin", "color": 0xE5484D, "hoist": True,
     "perms": ["ADMINISTRATOR"]},
    {"key": "developer", "name": "Developer", "color": 0x8E6CEF, "hoist": True, "perms": []},
    {"key": "mod", "name": "Moderator", "color": 0xF5A524, "hoist": True,
     "perms": ["KICK_MEMBERS", "BAN_MEMBERS", "MODERATE_MEMBERS", "MANAGE_MESSAGES", "MANAGE_THREADS",
               "MANAGE_NICKNAMES", "VIEW_AUDIT_LOG", "MUTE_MEMBERS", "DEAFEN_MEMBERS", "MOVE_MEMBERS"]},
    {"key": "helper", "name": "Helper", "color": 0x30A46C, "hoist": True, "perms": ["MANAGE_THREADS"]},
    # Runs community events: makes, edits and deletes channels inside the 🏆 EVENTS category only (category overwrites,
    # setup_server.event), and Discord scheduled events. No server-wide channel or role powers.
    {"key": "event", "name": "Event Coordinator", "color": 0xE8A13A, "hoist": True, "perms": ["CREATE_EVENTS"]},
    {"key": "tester", "name": "Beta Tester", "color": 0x12A594, "hoist": False, "perms": []},
    {"key": "team", "name": "VEX Team Member", "color": 0x3E8ED0, "hoist": False, "perms": []},
    {"key": "ping_announce", "name": "Announcement Pings", "color": 0, "hoist": False, "perms": []},
    {"key": "ping_updates", "name": "Update Pings", "color": 0, "hoist": False, "perms": []},
    {"key": "ping_events", "name": "Event Pings", "color": 0, "hoist": False, "perms": []},
]

# What every member may do anywhere (no mentions of everyone, no TTS, no external apps posting for them).
EVERYONE = ["VIEW_CHANNEL", "SEND_MESSAGES", "SEND_MESSAGES_IN_THREADS", "CREATE_PUBLIC_THREADS", "EMBED_LINKS",
            "ATTACH_FILES", "ADD_REACTIONS", "USE_EXTERNAL_EMOJIS", "USE_EXTERNAL_STICKERS", "READ_MESSAGE_HISTORY",
            "CONNECT", "SPEAK", "USE_VAD", "STREAM", "CHANGE_NICKNAME", "USE_APPLICATION_COMMANDS",
            "SEND_VOICE_MESSAGES", "REQUEST_TO_SPEAK", "CREATE_INSTANT_INVITE", "SEND_POLLS", "USE_SOUNDBOARD"]

# Channel access kinds (see setup_server.overwrites):
#  public    everyone talks
#  readonly  everyone reads and reacts; staff post (Admin; Moderators too when "mods_post")
#  guides    a forum everyone reads and comments in; Helpers and staff start posts
#  support   a forum everyone posts in; Helpers can tidy it
#  staff     Moderators and Helpers only
#  mods      Moderators only
#  reports   Moderators read; only Protobot's webhooks post
#  admin     Admins only
CATEGORIES = [
    {"key": "start", "name": "📌 START HERE", "channels": [
        {"key": "welcome", "name": "welcome", "kind": "readonly", "topic": "What Protobot is and where everything is."},
        {"key": "rules", "name": "rules", "kind": "readonly", "topic": "The server rules. By chatting here you agree to them."},
        {"key": "announcements", "name": "announcements", "kind": "readonly", "news": True, "bot_pings": True,
         "topic": "News about Protobot. Follow this channel to get it in your own server."},
        {"key": "releases", "name": "releases", "kind": "readonly", "news": True, "bot_pings": True,
         "topic": "Every new version and what changed. Protobot updates itself: Help > Check for updates."},
        {"key": "faq", "name": "faq", "kind": "readonly", "topic": "Answers to the questions people ask most."},
    ]},
    {"key": "learn", "name": "📚 LEARN PROTOBOT", "channels": [
        {"key": "download", "name": "download", "kind": "readonly", "topic": "Get Protobot for Windows: " + RELEASES},
        {"key": "tutorials", "name": "tutorials", "kind": "guides", "forum": True,
         "topic": "Step-by-step guides. Ask questions about a guide in its post.",
         "tags": [("Start here", "🚀"), ("Building", "🔩"), ("Tools", "🪚"), ("Motion", "⚙️"), ("Simulation", "🏎️"),
                  ("Robot code", "💻"), ("Game", "🏆"), ("Online", "🌐")]},
        {"key": "tips", "name": "tips-and-tricks", "kind": "public", "topic": "Share a shortcut, a trick or a neat build technique."},
    ]},
    {"key": "community", "name": "💬 COMMUNITY", "channels": [
        {"key": "general", "name": "general", "kind": "public", "topic": "Talk about Protobot, robots and VEX. Be kind!"},
        {"key": "leaderboard", "name": "skills-leaderboard", "kind": "readonly",
         "topic": "The live Robot Skills leaderboards from Protobot (Play game > Robot Skills, signed in). Updated as soon as a new best is posted."},
        {"key": "showcase", "name": "show-your-robot", "kind": "support", "forum": True,
         "topic": "Show off a robot you built in Protobot: a picture, a video or the .pbb file.",
         "tags": [("Drivetrain", "🛞"), ("Intake", "🌀"), ("Lift / Arm", "🦾"), ("Full robot", "🤖"), ("Robot code", "💻"),
                  ("Override", "🏆")]},
        {"key": "code", "name": "robot-code", "kind": "public", "topic": "Python, C++ (PROS, VEXcode, LemLib, EZ-Template) and Blocks in Protobot."},
        {"key": "vex", "name": "vex-competition", "kind": "public", "topic": "The V5RC game, strategy and your team's season."},
        {"key": "lfm", "name": "find-a-match", "kind": "public", "slowmode": 30,
         "topic": "Looking for players? Post your match's invite code (Multiplayer > Host a match > Copy invite)."},
        {"key": "offtopic", "name": "off-topic", "kind": "public", "topic": "Anything else (keep it friendly)."},
    ]},
    {"key": "support", "name": "🛠️ SUPPORT", "channels": [
        {"key": "help", "name": "help", "kind": "support", "forum": True, "require_tag": True,
         "topic": "Stuck? Make a post. Say what you tried and add a picture or your .pbb if you can.",
         "tags": [("Installing", "📥"), ("Building", "🔩"), ("Motion", "⚙️"), ("Simulation", "🏎️"), ("Robot code", "💻"),
                  ("Game", "🏆"), ("Online", "🌐"), ("Performance", "🐢"), ("Solved", "✅")]},
        {"key": "bugs", "name": "bug-reports", "kind": "support", "forum": True, "require_tag": True,
         "topic": "Found a bug? Help > Report a bug in Protobot sends your logs privately; or post it here.",
         "tags": [("Crash", "💥"), ("Building", "🔩"), ("Tools", "🪚"), ("Simulation", "🏎️"), ("Robot code", "💻"),
                  ("Online", "🌐"), ("Graphics", "🖥️"), ("Mac", "🍎"), ("Confirmed", "🔍"), ("Fixed", "✅"), ("Can't reproduce", "❔")]},
        {"key": "ideas", "name": "feature-requests", "kind": "support", "forum": True,
         "topic": "Ideas for Protobot. One idea per post; react 👍 to the ones you want.",
         "tags": [("Building", "🔩"), ("Tools", "🪚"), ("Simulation", "🏎️"), ("Robot code", "💻"), ("Game", "🏆"),
                  ("Online", "🌐"), ("Planned", "🗓️"), ("Done", "✅"), ("Not planned", "⛔")]},
        {"key": "parts", "name": "part-requests", "kind": "support", "forum": True,
         "topic": "A VEX part that's missing from the parts list? Ask for it here (or Help > Request a part).",
         "tags": [("Structure", "🧱"), ("Motion", "⚙️"), ("Electronics", "🔌"), ("Pneumatics", "💨"), ("Added", "✅")]},
    ]},
    {"key": "voice", "name": "🔊 VOICE", "channels": [
        {"key": "vc_general", "name": "General", "kind": "public", "voice": True},
        {"key": "vc_build", "name": "Build Together", "kind": "public", "voice": True},
        {"key": "vc_match1", "name": "Match 1", "kind": "public", "voice": True, "limit": 4},
        {"key": "vc_match2", "name": "Match 2", "kind": "public", "voice": True, "limit": 4},
    ]},
    {"key": "staff", "name": "🔒 STAFF", "channels": [
        {"key": "staff_chat", "name": "staff-chat", "kind": "staff", "topic": "Moderators and Helpers. Read the pinned guide."},
        {"key": "mod_log", "name": "mod-log", "kind": "mods", "topic": "AutoMod alerts, joins and Discord's safety alerts."},
        {"key": "discord_updates", "name": "discord-updates", "kind": "mods", "topic": "Messages from Discord about the server."},
    ]},
    {"key": "reports", "name": "🧾 PROTOBOT REPORTS", "channels": [
        {"key": "app_bugs", "name": "app-bug-reports", "kind": "reports",
         "topic": "Bug reports sent from inside Protobot (with logs). Private: never share their contents."},
        {"key": "player_reports", "name": "player-reports", "kind": "reports",
         "topic": "Reports of players and leaderboard runs sent from Protobot. Private."},
        {"key": "appeals", "name": "appeals", "kind": "reports", "topic": "Appeals sent from Protobot (Multiplayer > Account > Appeal). Private."},
        {"key": "dev_log", "name": "dev-log", "kind": "admin", "topic": "Developer notes and release drafts."},
        {"key": "console", "name": "player-messages", "kind": "admin",
         "topic": "Message the players online: !ask <question>, !notify <text>, !list, !stop <id>, !help. Answers come back here."},
    ]},
]

WEBHOOKS = {"app_bugs": "bug", "player_reports": "report", "appeals": "appeal"}


def ch(ids, key):
    return f"<#{ids[key]}>" if key in ids else "#" + key


def welcome(ids):
    return [{
        "title": "Welcome to the Protobot server! 👋",
        "color": BLUE,
        "description": (
            "**Protobot Rebuilt V2** is a free VEX V5 robot design tool: build robots from full-detail VEX parts, "
            "make them move with motors, gears, chains and pneumatics, write robot code, and drive them on the "
            "competition field, alone or online with friends.\n\n"
            "This is the official place to learn Protobot, get help, report bugs and share what you build."),
        "fields": [
            {"name": "🚀 New here?", "value": f"Read {ch(ids, 'rules')}, then grab Protobot in {ch(ids, 'download')} and "
                                              f"follow the guides in {ch(ids, 'tutorials')}.", "inline": False},
            {"name": "🛠️ Need help?", "value": f"Check {ch(ids, 'faq')} first, then make a post in {ch(ids, 'help')}.", "inline": False},
            {"name": "🐞 Found a bug?", "value": f"Use **Help > Report a bug** inside Protobot (it sends your logs privately), "
                                                 f"or post in {ch(ids, 'bugs')}.", "inline": False},
            {"name": "💡 Have an idea or need a part?", "value": f"{ch(ids, 'ideas')} and {ch(ids, 'parts')}.", "inline": False},
            {"name": "💬 Hang out", "value": f"{ch(ids, 'general')}, {ch(ids, 'showcase')}, {ch(ids, 'code')}, "
                                             f"{ch(ids, 'lfm')} for online matches.", "inline": False},
            {"name": "📢 Stay up to date", "value": f"{ch(ids, 'announcements')} and {ch(ids, 'releases')}. Pick ping roles "
                                                    f"in **Channels & Roles** at the top of the channel list.", "inline": False},
        ],
        "footer": {"text": "Protobot is a fan-made tool, not affiliated with or endorsed by VEX Robotics or the REC Foundation."},
    }]


def rules(ids):
    items = [
        ("Be respectful", "No harassment, hate speech, slurs, threats or bullying. Disagree about ideas, not people."),
        ("Keep it appropriate", "No NSFW, gore or shocking content, in messages, names, pictures or voice. Many members are students."),
        ("Keep personal information private", "Don't share your own or anyone else's full name, address, school, phone number, "
                                              "email or passwords. Staff will never ask for your password."),
        ("No spam or advertising", "No flooding, chain messages, unrelated server invites or advertising, in channels or in members' DMs."),
        ("Use the right channel", f"Questions go in {ch(ids, 'help')}, bugs in {ch(ids, 'bugs')}, ideas in {ch(ids, 'ideas')}. "
                                  "Search before posting; one topic per post."),
        ("Play fair", "No cheating, modified versions of Protobot or exploiting bugs in online matches or on the leaderboard. "
                      "Found an exploit? Report it privately with Help > Report a bug."),
        ("Respect people's work", "Credit others when you share or build on their robots, code or guides."),
        ("Follow Discord's rules", "Discord's Terms of Service and Community Guidelines apply here (you must be 13 or older to use Discord)."),
        ("Listen to staff", "Moderators keep the server safe. If you disagree with a decision, message a Moderator privately."),
    ]
    text = "\n\n".join(f"**{i + 1}. {t}**\n{d}" for i, (t, d) in enumerate(items))
    return [{
        "title": "📜 Server rules",
        "color": BLUE,
        "description": text + f"\n\nBreaking the rules can get a warning, a timeout, a kick or a ban, depending on how serious it is.\n\n"
                              f"Protobot's own [Terms of Use]({TERMS}) and [Privacy Policy]({PRIVACY}) cover the app and online play.",
    }]


def faq(ids):
    qa = [
        ("What is Protobot?", "A free VEX V5 robot design tool for Windows: build with the full VEX-CAD part library, make parts move, "
                              "write robot code and drive on the V5RC field, alone or online."),
        ("Is it free? Is it official?", f"Free and open source ([GPLv3]({REPO})). It's a fan-made tool, not an official VEX product."),
        ("What computer do I need?", "Windows 10 or 11 (64-bit). Low-end PCs work too: Edit > Graphics has presets from Very Low to "
                                     "Extreme, and the light field model is for weaker PCs."),
        ("Windows says it protected my PC when installing?", "Protobot isn't code-signed yet, so SmartScreen asks once. Click "
                                                             "**More info > Run anyway**. Only download it from the link in "
                                                             f"{ch(ids, 'download')}."),
        ("How do I update?", "Protobot checks for updates when it starts (and Help > Check for updates), downloads only what changed "
                             f"and restarts. What changed is in {ch(ids, 'releases')} and Help > Patch notes."),
        ("Can I open builds from the original Protobot?", "Yes. Old .pbb files open, and Protobot offers to upgrade them to the new "
                                                          "format (a backup of the old file is kept)."),
        ("Where are my files?", "Builds are saved wherever you save them. Autosaves, custom parts, replays and logs are in "
                                "Protobot's data folder: Help > Open bug log folder opens its Logs folder; go up one folder "
                                "for the rest."),
        ("Which robot code can I use?", "VEXcode-style Python, C++ (PROS with LemLib or EZ-Template, and VEXcode C++) and Blocks, run "
                                        "on the simulated robot. Open the Code window from the top bar."),
        ("How do I play online?", f"Home menu > Multiplayer: host a match and share its invite code, or join one. Post codes in "
                                  f"{ch(ids, 'lfm')}."),
        ("Why can't I hear voice chat?", "Players under 13 play online without voice chat (children's privacy law), and anyone can switch "
                                         "it off. A crossed-out speaker by a name means that player can't hear it."),
        ("How do I report a bug?", "Help > Report a bug in Protobot: it adds your logs and (if you want) your build, so it can be "
                                   f"fixed fast. You can also post in {ch(ids, 'bugs')}."),
        ("A part is missing!", f"Ask in {ch(ids, 'parts')} or with Help > Request a part."),
    ]
    return [{
        "title": "❓ Frequently asked questions",
        "color": BLUE,
        "description": "\n\n".join(f"**{q}**\n{a}" for q, a in qa),
    }]


def download(ids):
    return [{
        "title": "📥 Download Protobot",
        "color": BLUE,
        "url": RELEASES,
        "description": (
            f"**[Get the latest version]({RELEASES})** and run **ProtobotRebuiltV2-win-Setup.exe**. It installs in a few seconds "
            "and keeps itself up to date.\n\n"
            "**Portable:** the same page has a zip you can unpack and run without installing.\n\n"
            "**Needs:** Windows 10 or 11, 64-bit. Any graphics card from the last ~10 years; pick a lower graphics preset "
            "in Edit > Graphics on older PCs.\n\n"
            "**SmartScreen:** Windows may say it protected your PC (Protobot isn't code-signed yet): click **More info > Run anyway**.\n\n"
            "🍎 **Mac (test version):** the same page has **ProtobotRebuiltV2-mac.zip** for Intel and Apple Silicon Macs. Unzip it, "
            "then right-click the app and choose **Open** the first time (it isn't notarized by Apple). If macOS says it's damaged, run "
            "`xattr -cr \"Protobot Rebuilt V2.app\"` in Terminal. Macs don't update by themselves: download new versions here. "
            f"It's new and untested on real Macs: please report what works and what doesn't in {ch(ids, 'bugs')} with the **Mac** tag.\n\n"
            f"⚠️ Only download Protobot from this link. Nobody from the team will ever send you a different download in DMs.\n\n"
            f"Then start with the guides in {ch(ids, 'tutorials')}."),
    }]


def staff_guide(ids):
    return [{
        "title": "🛡️ Staff guide",
        "color": 0xF5A524,
        "description": (
            "**Moderators** can time out, kick and ban members, delete messages, lock and close threads, and see the audit log. "
            "**Helpers** answer questions and can close or lock posts in the support forums.\n\n"
            "**What you can't do (on purpose):** nobody below Admin can change roles, channels, webhooks or server settings, "
            "so a lost or stolen staff account can't take the server over. Ask an Admin for role changes.\n\n"
            "**Steps:** a friendly reminder, then a warning, then a timeout (Right-click the member > Time Out), then a kick or ban "
            "for repeated or serious problems. Ban straight away for scams, NSFW, threats or hate.\n\n"
            f"**AutoMod** blocks slurs, spam, mass mentions and scam links and posts what it caught in {ch(ids, 'mod_log')}.\n\n"
            f"**Reports from Protobot** land in {ch(ids, 'app_bugs')}, {ch(ids, 'player_reports')} and {ch(ids, 'appeals')}. They can hold "
            "players' logs and builds: never share them outside the staff channels. Leaderboard runs are removed with the Skills "
            "Leaderboard Moderation tool (developer).\n\n"
            "**Turn on two-factor authentication** on your Discord account: moderator actions need it."),
    }]


TUTORIALS = [
    ("Getting started: install and make your first build", "Start here", [
        f"**1. Install.** Download Protobot from the download channel and run the setup. It updates itself from then on.",
        "**2. The home menu.** Create New starts an empty build; Open loads a .pbb file; Play game opens the competition field; "
        "Multiplayer is for online matches. Your recent builds are listed too.",
        "**3. Add parts.** The parts list on the right has categories (Hardware, Structure, Motion, Electronics, Pneumatics...) "
        "and a search box at the top. Click a part, then click in the scene to place it. Holes snap to holes.",
        "**4. Move around.** Orbit, pan and zoom with the mouse (the view cube at the top right turns the camera to a side). "
        "F focuses the camera on what's under the mouse.",
        "**5. Save.** File > Save writes a .pbb file. Autosave (Edit > Preferences) keeps a copy every minute.",
        "Next: *Placing parts and fastening*.",
    ]),
    ("Placing parts and fastening (screws, nuts, spacers)", "Building", [
        "**Placing:** pick a part in the parts list; over a hole it turns to fit. R shows the rotate ring, T puts the turn back. "
        "Ctrl + mouse wheel changes a metal part's length (hole count) while placing.",
        "**Quick screw (C):** point at a hole and press C: Protobot puts a screw of the right length through it and the hole "
        "touching it, with a nut. Shift+C goes through every hole in line.",
        "**Screw ghost (hold F):** hold F over a hole to see a screw; the mouse wheel changes its length; let go to place it.",
        "**Spacers (hold E):** hold E over a screw or hole for a nylon spacer; the wheel changes its length.",
        "**Moving parts:** the Transform tool's arrows and rings move and turn a part (or the group screwed to it). Click a hole to "
        "pick a part up by it, then click the hole it should go to.",
        "**Mirror (M):** copies a part or group to the other side of the robot.",
    ]),
    ("Cutting, drilling and bending (Saw, Drill and Bend tools)", "Tools", [
        "**Saw (4):** click a part; a cut plane appears with its distance from the end. Drag the arrow (1/16\" steps), click the "
        "number to type a length, or drag the coloured dot onto a hole or edge to measure from there. Enter cuts.",
        "**Drill (5):** pick a bit size and click where the hole goes; a round hole can be drilled bigger.",
        "**Bend (7):** on high-strength shafts and standoffs: the blue ring sets the angle, the green one the direction.",
        "**Measure (6):** click two points (holes, corners, edges, faces) to see the distance and how far apart they are in X, Y and Z.",
        "Every cut, hole and bend can be undone (Ctrl+Z) and is saved in your build.",
    ]),
    ("Motors, gears, chains and the Devices panel", "Motion", [
        "**Motors:** click a motor to control it and set its keys; hold the key and its whole gear train turns. Cartridges: "
        "100/200/600 rpm (red/green/blue).",
        "**Gears** mesh when they're on parallel shafts at the right distance. **Chains:** use the Chain tool (parts list > "
        "Sprockets & chain > Chain), click the sprockets in order, then Make chain.",
        "**Pneumatics:** cylinders extend and retract (right-click), tubing runs from fittings (E over a port).",
        "**Devices (Edit > Devices…):** every motor, sensor and solenoid with its port, live readings and settings, like a V5 Brain. "
        "Wiring: E over a free port on the Brain, a motor or a sensor runs a cable.",
    ]),
    ("Simulate and drive your robot", "Simulation", [
        "**Simulate** (top bar) turns your build into a physics robot on the floor. Esc or Stop puts the build back as it was.",
        "**Driving:** set which motors are the left and right drive (Devices panel), choose Tank or Arcade, and drive with the "
        "keyboard or a gamepad. Edit > Controller… shows and changes every binding.",
        "**Camera:** free, follow the robot, or ride on a part.",
        "Weights come from the real parts, motors follow the real V5 motor curves and gravity is real, so a robot that tips "
        "or stalls here probably will in real life too.",
    ]),
    ("Robot code: Python, C++ and Blocks", "Robot code", [
        "Open the **Code** window from the top bar. Pick an example from the menu to start.",
        "**Python** works like VEXcode V5 Python. **C++** works with PROS (with LemLib or EZ-Template) and VEXcode C++. "
        "**Blocks** are VEXcode-style blocks.",
        "**Insert devices** writes the configuration for your robot (motors, sensors, drivetrain) from the build.",
        "**Run** starts the simulation and your program. The Monitor tab shows variables and threads; the Graph tab plots "
        "values for tuning PID; the Brain screen and controller screen are shown too.",
        "Prefer VS Code? The window's VS Code button opens your program there and keeps it in sync.",
    ]),
    ("Robot Skills and match practice on the Override field", "Game", [
        "**Play game** (top bar or home menu) puts your robot on the V5RC Override field. Your build needs a Brain.",
        "**Robot Skills:** 1:00 of driving from the Skills start. **Programming Skills:** 1:00 of your program only. "
        "**Match practice:** 0:15 autonomous then 1:45 driver control.",
        "**Setup:** slide and turn the robot along its wall, place the Preload, and check the start rules in the checklist.",
        "At the end the score is counted like the real game and shown piece by piece. Legal Skills runs go on the online "
        "leaderboard, with a replay others can watch.",
    ]),
    ("Playing online with friends", "Online", [
        "Home menu > **Multiplayer**. The first time, Protobot asks your birth month and year and for you to agree to the terms.",
        "**Host a match:** choose the mode, then share the invite code (Copy invite). Friends join with the code.",
        "**Lobby:** pick an alliance, open the robot you'll bring, press Ready. The host starts the match.",
        "**Voice chat:** you hear your alliance partner (push to talk with T, open mic or off). Players under 13 play without voice chat.",
        "**Account:** optional; keeps your stats and leaderboard scores on any computer.",
    ]),
    ("Custom parts and polycarbonate", "Building", [
        "**Import a model:** File > Import part… (STL, OBJ, 3MF, GLB, PLY): set its material and holes, and it becomes a part you can "
        "place, cut and share in builds.",
        "**Polycarbonate:** the sketcher (Plastic > New polycarbonate piece…) works like a CAD sketch: lines, arcs, dimensions and relations. Make piece "
        "extrudes it; Plastic > Plastic sheets… shows how your pieces fit the season's plastic rules.",
        "**Sketch on a part:** right-click a plastic piece > Edit sketch on the robot, or start one on any flat face.",
    ]),
    ("Keyboard shortcuts", "Start here", [
        "Every shortcut is listed (and can be changed) in **Edit > Keyboard shortcuts…**.",
        "Some to know: **C** screw through a hole, **F** (hold) screw ghost, **E** (hold) spacer, **M** mirror, **4** saw, "
        "**5** drill, **6** measure, **7** bend, **R** rotate while placing, **Ctrl+Z / Ctrl+Y** undo and redo, **Esc** back to the "
        "Transform tool.",
    ]),
]

GUIDE_POSTS = {
    "help": ("📌 How to get help fast", [
        "**Search first:** your question may already be answered here or in the FAQ.",
        "**Make one post per problem** with a clear title (\"Chain won't connect two sprockets\", not \"help\").",
        "**Say:** what you're trying to do, what happens instead, and what you already tried. Add your Protobot version "
        "(bottom of the home menu).",
        "**Show it:** a screenshot or your .pbb file helps a lot.",
        "**When it's solved,** add the ✅ Solved tag so others can find the answer.",
    ]),
    "bugs": ("📌 How to report a bug", [
        "**Best way:** Help > Report a bug inside Protobot. It sends your session log and (if you want) your build privately, "
        "so the developer can see exactly what happened.",
        "**Posting here instead?** Use this template:\n"
        "• **What happened:**\n• **What you expected:**\n• **Steps to make it happen again:** 1. 2. 3.\n"
        "• **Version:** (bottom of the home menu)\n• **Screenshot or .pbb:**",
        "Pick a tag (Crash, Building, Tools...). Don't post logs that show your name or personal details.",
        "Security problems or exploits: report them privately with Help > Report a bug, not here.",
    ]),
    "ideas": ("📌 How to suggest a feature", [
        "**One idea per post,** with a title that says what it is.",
        "**Say why:** what you're trying to do and how the idea helps. Pictures and examples from real robots help.",
        "**Vote** with 👍 on ideas you want; staff tag them Planned, Done or Not planned.",
    ]),
    "parts": ("📌 Requesting a part", [
        "Search the parts list first (the search box covers every part).",
        "Give the part's name and **VEX part number** (like 276-xxxx) and a link to its page if you can.",
        "You can also use Help > Request a part inside Protobot.",
    ]),
    "showcase": ("📌 Sharing your robot", [
        "One post per robot. Add a picture (the Picture button in the top bar saves one) or a short video, and the .pbb if you want others to "
        "open it.",
        "Say what it's for and what you're proud of. Be kind in the comments: everyone starts somewhere.",
    ]),
}
