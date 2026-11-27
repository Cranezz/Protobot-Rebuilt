"""Sets up (or updates) the Protobot Discord server from server_content.py, through the Proto Bot account.

    python -I Tools/Discord/setup_server.py [steps...]

Steps (default: all, in this order): settings roles channels online leaderboard community automod messages guides tutorials welcome
onboarding webhooks invite check. Safe to run again: roles and channels are found by name, messages are edited in place.
Private output (webhook URLs, the invite) goes to C:/Dev/ProtobotRebuiltV2/Discord, never to the repository."""
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from discord_api import P, PRIVATE, call, image_data, load_state, perms, save_state  # noqa: E402
import server_content as C  # noqa: E402

REASON = "Protobot server setup"
ICON = os.path.join(PRIVATE, "protobot_icon.png")
state = load_state()


def log(*a):
    print(*a, flush=True)


def guild():
    if "guild" in state:
        return state["guild"]
    guilds = call("GET", "/users/@me/guilds")
    match = [g for g in guilds if g["name"] == "Protobot"]
    if len(match) != 1:
        sys.exit(f"Expected the bot in exactly one server named Protobot, found {len(match)}")
    state["guild"] = match[0]["id"]
    save_state(state)
    return state["guild"]


G = guild()
me = call("GET", "/users/@me")


# ------------------------------------------------------------------ settings

def settings():
    body = {
        "verification_level": 2,             # members need an account older than 5 minutes
        "explicit_content_filter": 2,        # scan media from everyone
        "default_message_notifications": 1,  # only @mentions notify
        "description": "The official server for Protobot Rebuilt V2, the free VEX V5 robot design tool: guides, help, bug reports and builds.",
        "preferred_locale": "en-US",
    }
    if os.path.exists(ICON):
        body["icon"] = image_data(ICON)
    call("PATCH", f"/guilds/{G}", body, REASON)
    if os.path.exists(ICON) and not state.get("bot_avatar"):
        try:
            call("PATCH", "/users/@me", {"avatar": image_data(ICON)})
            state["bot_avatar"] = True
        except RuntimeError as e:
            log("  bot avatar not changed:", str(e)[:120])
    log("settings: verification medium, media filter on, mentions-only notifications, icon")


# ------------------------------------------------------------------ roles

def roles():
    existing = {r["name"]: r for r in call("GET", f"/guilds/{G}/roles")}
    everyone = existing["@everyone"]
    call("PATCH", f"/guilds/{G}/roles/{everyone['id']}", {"permissions": str(perms(*C.EVERYONE))}, REASON)
    ids = state.setdefault("roles", {})
    for spec in C.ROLES:
        body = {"name": spec["name"], "permissions": str(perms(*spec["perms"])), "color": spec["color"],
                "hoist": spec["hoist"], "mentionable": False}
        role = existing.get(spec["name"]) or (call("GET", f"/guilds/{G}/roles/{ids[spec['key']]}", ok404=True) if spec["key"] in ids else None)
        if role:
            call("PATCH", f"/guilds/{G}/roles/{role['id']}", body, REASON)
        else:
            role = call("POST", f"/guilds/{G}/roles", body, REASON)
        ids[spec["key"]] = role["id"]
    save_state(state)
    # Order: Admin, Proto Bot (the bot's own role), Developer, Moderator, Helper, ... The bot can't move a role above
    # its own, so Admin stays just under it until the owner drags it to the top (Server Settings > Roles).
    current = {r["id"]: r for r in call("GET", f"/guilds/{G}/roles")}
    bot_role = next(r for r in current.values() if r.get("tags", {}).get("bot_id") == me["id"])
    state["bot_role"] = bot_role["id"]
    top = bot_role["position"] - 1
    order = [ids[s["key"]] for s in C.ROLES]
    call("PATCH", f"/guilds/{G}/roles", [{"id": rid, "position": max(1, top - i)} for i, rid in enumerate(order)], REASON)
    save_state(state)
    log("roles:", ", ".join(s["name"] for s in C.ROLES), "| @everyone: basic chat and voice only")


# ------------------------------------------------------------------ channels

READONLY_DENY = perms("SEND_MESSAGES", "CREATE_PUBLIC_THREADS", "CREATE_PRIVATE_THREADS", "SEND_MESSAGES_IN_THREADS",
                      "SEND_POLLS", "SEND_VOICE_MESSAGES", "USE_APPLICATION_COMMANDS")
POST = perms("VIEW_CHANNEL", "SEND_MESSAGES", "EMBED_LINKS", "ATTACH_FILES", "READ_MESSAGE_HISTORY",
             "SEND_MESSAGES_IN_THREADS", "CREATE_PUBLIC_THREADS", "MANAGE_THREADS", "MANAGE_MESSAGES", "PIN_MESSAGES")


def overwrites(kind, spec):
    r = state["roles"]
    every, bot = G, state["bot_role"]
    ow = []

    def add(target, allow=0, deny=0):
        ow.append({"id": target, "type": 0, "allow": str(allow), "deny": str(deny)})

    if kind == "public":
        add(every)
    elif kind == "readonly":
        add(every, deny=READONLY_DENY)
        if spec.get("mods_post"):
            add(r["mod"], allow=perms("SEND_MESSAGES"))
        # The bot may ping roles only where it posts news (the Update Pings role for a release); nowhere else.
        add(bot, allow=POST | (perms("MENTION_EVERYONE") if spec.get("bot_pings") else 0))
    elif kind == "guides":
        add(every, allow=perms("SEND_MESSAGES_IN_THREADS"), deny=perms("SEND_MESSAGES", "CREATE_PUBLIC_THREADS", "CREATE_PRIVATE_THREADS"))
        add(r["helper"], allow=perms("SEND_MESSAGES", "CREATE_PUBLIC_THREADS"))
        add(r["mod"], allow=perms("SEND_MESSAGES", "CREATE_PUBLIC_THREADS"))
        add(bot, allow=POST)
    elif kind == "support":
        add(every)
        add(r["helper"], allow=perms("MANAGE_MESSAGES"))
        add(bot, allow=POST)
    elif kind == "staff":
        add(every, deny=perms("VIEW_CHANNEL"))
        add(r["helper"], allow=perms("VIEW_CHANNEL"))
        add(r["mod"], allow=perms("VIEW_CHANNEL"))
        add(bot, allow=POST)
    elif kind == "mods":
        add(every, deny=perms("VIEW_CHANNEL"))
        add(r["mod"], allow=perms("VIEW_CHANNEL"))
        add(bot, allow=POST)
    elif kind == "reports":
        add(every, deny=perms("VIEW_CHANNEL"))
        add(r["mod"], allow=perms("VIEW_CHANNEL", "READ_MESSAGE_HISTORY"), deny=perms("SEND_MESSAGES", "CREATE_PUBLIC_THREADS"))
        add(bot, allow=POST | perms("MANAGE_WEBHOOKS"))
    elif kind == "admin":
        add(every, deny=perms("VIEW_CHANNEL"))
        add(bot, allow=POST)
    return ow


def emoji(e):
    return {"emoji_name": e, "emoji_id": None}


def channels():
    existing = call("GET", f"/guilds/{G}/channels")
    by = {(c["name"], c["type"]): c for c in existing}
    ids = state.setdefault("channels", {})
    cats = state.setdefault("categories", {})
    cat_pos = 0
    for cat in C.CATEGORIES:
        cat_kind = "public" if cat["key"] in ("start", "learn", "community", "support", "voice") else \
            ("staff" if cat["key"] == "staff" else "mods")
        body = {"name": cat["name"], "type": 4, "position": cat_pos, "permission_overwrites": overwrites(cat_kind, {})}
        c = by.get((cat["name"], 4))
        c = call("PATCH", f"/channels/{c['id']}", body, REASON) if c else call("POST", f"/guilds/{G}/channels", body, REASON)
        cats[cat["key"]] = c["id"]
        cat_pos += 1
        for pos, spec in enumerate(cat["channels"]):
            ctype = 2 if spec.get("voice") else 15 if spec.get("forum") else 0
            body = {"name": spec["name"], "type": ctype, "parent_id": c["id"], "position": pos,
                    "permission_overwrites": overwrites(spec["kind"], spec)}
            if ctype != 2:
                body["topic"] = spec.get("topic", "")
            if spec.get("slowmode"):
                body["rate_limit_per_user"] = spec["slowmode"]
            if ctype == 2 and spec.get("limit"):
                body["user_limit"] = spec["limit"]
            if ctype == 15:
                body["available_tags"] = [{"name": n, "emoji_name": e, "moderated": n in ("Solved", "Fixed", "Confirmed", "Can't reproduce", "Planned", "Done", "Not planned", "Added")}
                                          for n, e in spec.get("tags", [])]
                body["default_reaction_emoji"] = {"emoji_name": "👍"}
                body["default_sort_order"] = 0
                body["default_forum_layout"] = 1
                if spec.get("require_tag"):
                    body["flags"] = 1 << 4
            found = by.get((spec["name"], ctype)) or (by.get((spec["name"], 5)) if ctype == 0 else None)
            if found:
                if found["type"] == 15 and "available_tags" in body:
                    # keep existing tag ids (posts use them): match by name
                    old = {t["name"]: t["id"] for t in found.get("available_tags", [])}
                    for t in body["available_tags"]:
                        if t["name"] in old:
                            t["id"] = old[t["name"]]
                body.pop("type", None)
                ch = call("PATCH", f"/channels/{found['id']}", body, REASON)
            else:
                ch = call("POST", f"/guilds/{G}/channels", body, REASON)
            ids[spec["key"]] = ch["id"]
    save_state(state)
    # The server's starting channels and categories go (they're empty).
    keep = set(ids.values()) | set(cats.values())
    for c in call("GET", f"/guilds/{G}/channels"):
        if c["id"] not in keep and c["name"] in ("general", "General", "Text Channels", "Voice Channels"):
            call("DELETE", f"/channels/{c['id']}", reason=REASON)
            log("  removed the starting channel", c["name"])
    log("channels:", len(ids), "in", len(cats), "categories")


def online():
    """The "Players online" counter at the very top of the channel list: a voice channel nobody can join, outside every
    category. Its name is changed by Cloud Code Presence.js (the app's heartbeat, and an hourly schedule) through the bot,
    which may manage this one channel (an overwrite: it still works once the bot's role is cut back)."""
    ids = state.setdefault("channels", {})
    bot = state["bot_role"]
    ow = [{"id": G, "type": 0, "allow": str(perms("VIEW_CHANNEL")), "deny": str(perms("CONNECT", "SPEAK", "SEND_MESSAGES"))},
          {"id": bot, "type": 0, "allow": str(perms("VIEW_CHANNEL", "MANAGE_CHANNELS", "CONNECT")), "deny": "0"}]
    found = call("GET", f"/channels/{ids['online']}", ok404=True) if ids.get("online") else None
    if found:
        call("PATCH", f"/channels/{found['id']}", {"parent_id": None, "position": 0, "permission_overwrites": ow}, REASON)
    else:
        found = call("POST", f"/guilds/{G}/channels", {"name": "🟢 Players online: …", "type": 2, "position": 0,
                                                       "permission_overwrites": ow}, REASON)
        ids["online"] = found["id"]
        save_state(state)
    log("online counter:", found["name"])


CONSOLE_HELP = (
    "**Message the players who are online in Protobot.** Commands (only this channel, only Admins can see it):\n"
    "`!ask <question>`: a card with a text box; each answer comes back here as a reply to your command.\n"
    "`!notify <text>`: a card with just your message and OK.\n"
    "`!ask 2h <question>` / `!notify 30m <text>`: how long it stays up (default 24h, at most 7d). Players who open "
    "Protobot while it's up see it too; each player sees it once.\n"
    "`!test ask <question>` / `!test notify <text>`: only your own Protobot gets it (the players in Cloud Save "
    "\"devPlayers\"): try a message before sending it to everyone.\n"
    "`!list`: what's up now. `!stop <id>` / `!stop all`: take it down. `!help`: this.\n"
    "Cloud Code (Presence.js) reads this channel within about a minute while anyone is online, and every hour. "
    "Answers: Feedback.js (at most 3 per player per question).")


def console():
    """#player-messages (admins only): the developer's commands to the players online (Cloud Code Presence.js reads
    them, Feedback.js posts the answers). Its id goes to Cloud Save from Tools/AgentTesting/cs/t_discord_console.cs."""
    ids = state.setdefault("channels", {})
    spec = next(c for cat in C.CATEGORIES for c in cat["channels"] if c["key"] == "console")
    body = {"name": spec["name"], "type": 0, "topic": spec["topic"], "parent_id": state["categories"]["reports"],
            "permission_overwrites": overwrites("admin", spec)}
    found = call("GET", f"/channels/{ids['console']}", ok404=True) if ids.get("console") else None
    if found:
        call("PATCH", f"/channels/{found['id']}", body, REASON)
    else:
        found = call("POST", f"/guilds/{G}/channels", body, REASON)
        ids["console"] = found["id"]
        save_state(state)
    msgs = state.setdefault("messages", {})
    embed = {"title": "📣 Player messages", "description": CONSOLE_HELP, "color": 0x2885F4}
    mid = msgs.get("console_help")
    if mid and call("GET", f"/channels/{found['id']}/messages/{mid}", ok404=True):
        call("PATCH", f"/channels/{found['id']}/messages/{mid}", {"embeds": [embed]})
    else:
        m = call("POST", f"/channels/{found['id']}/messages", {"embeds": [embed], "allowed_mentions": {"parse": []}})
        msgs["console_help"] = m["id"]
        call("PUT", f"/channels/{found['id']}/pins/{m['id']}")
        save_state(state)
    log("player messages channel:", found["name"])


def leaderboard():
    """The live skills leaderboard: one message in #skills-leaderboard that Cloud Code DiscordLeaderboard.js keeps up to
    date (after every score posted, and hourly). Posted once here (a placeholder); its id goes to Cloud Save from
    Tools/AgentTesting/cs/t_discord_lb.cs."""
    msgs = state.setdefault("messages", {})
    channel = state["channels"]["leaderboard"]
    mid = msgs.get("leaderboard")
    if mid and call("GET", f"/channels/{channel}/messages/{mid}", ok404=True):
        log("leaderboard message: kept")
        return
    m = call("POST", f"/channels/{channel}/messages", {"embeds": [{"title": "🏆 Protobot Skills Leaderboard",
              "description": "Loading the leaderboards…", "color": 0xF5A524}], "allowed_mentions": {"parse": []}})
    msgs["leaderboard"] = m["id"]
    save_state(state)
    log("leaderboard message: posted")


def community():
    ids = state["channels"]
    g = call("GET", f"/guilds/{G}")
    features = set(g.get("features", []))
    features.add("COMMUNITY")
    call("PATCH", f"/guilds/{G}", {
        "features": sorted(features), "rules_channel_id": ids["rules"], "public_updates_channel_id": ids["discord_updates"],
        "safety_alerts_channel_id": ids["mod_log"], "system_channel_id": ids["mod_log"],
        "system_channel_flags": 1 << 1 | 1 << 3,  # no boost / setup tips spam; join messages stay (for mods)
    }, REASON)
    # Announcement channels need Community: turn them into ones people can follow.
    for cat in C.CATEGORIES:
        for spec in cat["channels"]:
            if spec.get("news"):
                c = call("GET", f"/channels/{ids[spec['key']]}")
                if c["type"] != 5:
                    call("PATCH", f"/channels/{c['id']}", {"type": 5}, REASON)
    log("community: on (rules screen, announcement channels, safety alerts in #mod-log)")


# ------------------------------------------------------------------ automod

def automod():
    ids, r = state["channels"], state["roles"]
    staff = [r["admin"], r["mod"]]
    alert = {"type": 2, "metadata": {"channel_id": ids["mod_log"]}}

    def block(msg):
        return {"type": 1, "metadata": {"custom_message": msg}}

    rules = [
        {"name": "Slurs, sexual content and profanity", "event_type": 1, "trigger_type": 4,
         "trigger_metadata": {"presets": [1, 2, 3], "allow_list": ["screw", "screws", "nut", "nuts", "shaft"]},
         "actions": [block("That message was blocked by the server's filter (rule 1 and 2)."), alert], "exempt_roles": staff},
        {"name": "Spam", "event_type": 1, "trigger_type": 3, "actions": [block("That looked like spam, so it was blocked."), alert]},
        {"name": "Mass mentions", "event_type": 1, "trigger_type": 5,
         "trigger_metadata": {"mention_total_limit": 5, "mention_raid_protection_enabled": True},
         "actions": [block("Too many mentions in one message."), alert, {"type": 3, "metadata": {"duration_seconds": 600}}],
         "exempt_roles": staff},
        {"name": "Scam links and server invites", "event_type": 1, "trigger_type": 1,
         "trigger_metadata": {
             "keyword_filter": ["*discord.gg/*", "*discord.com/invite/*", "*discordapp.com/invite/*", "*free nitro*",
                                "*nitro for free*", "*steamcommunity.com/gift*", "*steam gift*", "*airdrop*", "*crypto giveaway*",
                                "*dlscord*", "*discorcl*", "*disc0rd*", "*steamcommunlty*"],
         },
         "actions": [block("Links to other servers and gift/scam links aren't allowed here (rule 4)."), alert],
         "exempt_roles": staff + [r["helper"]]},
        {"name": "Personal information", "event_type": 1, "trigger_type": 1,
         "trigger_metadata": {"regex_patterns": [r"\b\d{3}[-. ]\d{3}[-. ]\d{4}\b", r"[\w.+-]+@[\w-]+\.[\w.]+"]},
         "actions": [block("Please don't post phone numbers or email addresses (rule 3)."), alert], "exempt_roles": staff},
        {"name": "Names", "event_type": 2, "trigger_type": 6,
         "trigger_metadata": {"keyword_filter": ["*nigg*", "*fagg*", "*hitler*", "*nazi*", "*porn*", "*hentai*", "*rape*"]},
         "actions": [{"type": 4}, alert]},
    ]
    existing = {a["name"]: a for a in call("GET", f"/guilds/{G}/auto-moderation/rules")}
    for rule in rules:
        rule["enabled"] = True
        try:
            if rule["name"] in existing:
                body = dict(rule)
                body.pop("trigger_type", None)
                call("PATCH", f"/guilds/{G}/auto-moderation/rules/{existing[rule['name']]['id']}", body, REASON)
            else:
                call("POST", f"/guilds/{G}/auto-moderation/rules", rule, REASON)
            log("  automod:", rule["name"])
        except RuntimeError as e:
            log("  automod NOT set:", rule["name"], str(e)[:200])


# ------------------------------------------------------------------ messages

def post(key, name, embeds, pin=False):
    msgs = state.setdefault("messages", {})
    channel = state["channels"][key]
    mid = msgs.get(name)
    if mid and call("GET", f"/channels/{channel}/messages/{mid}", ok404=True):
        call("PATCH", f"/channels/{channel}/messages/{mid}", {"embeds": embeds, "allowed_mentions": {"parse": []}})
    else:
        m = call("POST", f"/channels/{channel}/messages", {"embeds": embeds, "allowed_mentions": {"parse": []}})
        msgs[name] = m["id"]
        if pin:
            call("PUT", f"/channels/{channel}/pins/{m['id']}", reason=REASON)
    save_state(state)


def messages():
    ids = state["channels"]
    post("welcome", "welcome", C.welcome(ids))
    post("rules", "rules", C.rules(ids))
    post("faq", "faq", C.faq(ids))
    post("download", "download", C.download(ids))
    post("staff_chat", "staff_guide", C.staff_guide(ids), pin=True)
    log("messages: welcome, rules, faq, download, staff guide")


def forum_post(key, name, title, lines, tag=None, pin=False):
    threads = state.setdefault("threads", {})
    forum = call("GET", f"/channels/{state['channels'][key]}")
    tags = {t["name"]: t["id"] for t in forum.get("available_tags", [])}
    content = "\n\n".join(lines)
    tid = threads.get(name)
    thread = call("GET", f"/channels/{tid}", ok404=True) if tid else None
    applied = [tags[tag]] if tag in tags else []
    if thread:
        first = thread["id"]  # a forum post's first message has the thread's id
        call("PATCH", f"/channels/{thread['id']}/messages/{first}", {"content": content, "allowed_mentions": {"parse": []}})
        call("PATCH", f"/channels/{thread['id']}", {"name": title, "applied_tags": applied}, REASON)
    else:
        thread = call("POST", f"/channels/{forum['id']}/threads",
                      {"name": title, "applied_tags": applied, "auto_archive_duration": 10080,
                       "message": {"content": content, "allowed_mentions": {"parse": []}}}, REASON)
        threads[name] = thread["id"]
        save_state(state)
    if pin:
        call("PATCH", f"/channels/{thread['id']}", {"flags": 1 << 1}, REASON)  # pinned to the top of the forum
    return thread["id"]


def guides():
    for key, (title, lines) in C.GUIDE_POSTS.items():
        forum_post(key, "guide_" + key, title, lines, pin=True)
        # locked: it's a guide, not a discussion
        call("PATCH", f"/channels/{state['threads']['guide_' + key]}", {"locked": True}, REASON)
    log("guide posts:", ", ".join(C.GUIDE_POSTS))


def tutorials():
    for i, (title, tag, lines) in enumerate(reversed(C.TUTORIALS)):
        # posted last-first so the forum lists them first-to-last (newest on top)
        forum_post("tutorials", "tutorial_" + title, title, lines, tag=tag, pin=(title.startswith("Getting started")))
        time.sleep(0.6)
    log("tutorials:", len(C.TUTORIALS))


# ------------------------------------------------------------------ welcome screen, onboarding

def welcome_screen():
    ids = state["channels"]
    call("PATCH", f"/guilds/{G}/welcome-screen", {
        "enabled": True,
        "description": "The official Protobot community: learn it, get help, share your robots.",
        "welcome_channels": [
            {"channel_id": ids["download"], "description": "Get Protobot", "emoji_name": "📥"},
            {"channel_id": ids["tutorials"], "description": "Learn with step-by-step guides", "emoji_name": "📚"},
            {"channel_id": ids["help"], "description": "Ask for help", "emoji_name": "🛠️"},
            {"channel_id": ids["showcase"], "description": "Show off your robot", "emoji_name": "🤖"},
            {"channel_id": ids["general"], "description": "Say hi", "emoji_name": "💬"},
        ],
    }, REASON)
    log("welcome screen: on")


def onboarding():
    ids, r = state["channels"], state["roles"]
    nid = int(time.time() * 1000) << 22

    def opt(i, title, desc, emoji_name, roles=(), channels=()):
        return {"id": str(nid + i), "title": title, "description": desc, "emoji": {"name": emoji_name},
                "role_ids": list(roles), "channel_ids": list(channels)}

    prompts = [
        {"id": str(nid + 100), "type": 0, "title": "What brings you to Protobot?", "single_select": False, "required": False,
         "in_onboarding": True, "options": [
             opt(1, "I'm new to Protobot", "Show me where to start", "🚀", channels=[ids["tutorials"], ids["download"], ids["faq"]]),
             opt(2, "I build robots in it", "Share builds and tips", "🔩", channels=[ids["showcase"], ids["tips"]]),
             opt(3, "I write robot code", "Python, C++ and Blocks", "💻", channels=[ids["code"]]),
             opt(4, "I play online", "Matches and the leaderboard", "🌐", channels=[ids["lfm"]]),
             opt(5, "I'm on a VEX team", "Get the VEX Team Member role", "🏆", roles=[r["team"]], channels=[ids["vex"]]),
         ]},
        {"id": str(nid + 200), "type": 0, "title": "Which pings do you want?", "single_select": False, "required": False,
         "in_onboarding": True, "options": [
             opt(11, "Announcements", "Big news about Protobot", "📢", roles=[r["ping_announce"]]),
             opt(12, "New versions", "A ping when an update comes out", "🆕", roles=[r["ping_updates"]]),
             opt(13, "Events", "Build challenges and online events", "🎉", roles=[r["ping_events"]]),
         ]},
    ]
    defaults = [ids[k] for k in ("welcome", "rules", "announcements", "releases", "faq", "download", "tutorials", "general",
                                 "help", "bugs", "ideas", "showcase", "offtopic", "tips")]
    try:
        call("PUT", f"/guilds/{G}/onboarding", {"prompts": prompts, "default_channel_ids": defaults, "enabled": True, "mode": 0}, REASON)
        log("onboarding: on (interests and ping roles)")
    except RuntimeError as e:
        log("  onboarding NOT set:", str(e)[:300])


# ------------------------------------------------------------------ webhooks, invite

def webhooks():
    out = {}
    for key, kind in C.WEBHOOKS.items():
        channel = state["channels"][key]
        hooks = call("GET", f"/channels/{channel}/webhooks")
        hook = next((h for h in hooks if h["name"] == "Protobot Reports"), None)
        if not hook:
            body = {"name": "Protobot Reports"}
            if os.path.exists(ICON):
                body["avatar"] = image_data(ICON)
            hook = call("POST", f"/channels/{channel}/webhooks", body, REASON)
        out[kind] = f"https://discord.com/api/webhooks/{hook['id']}/{hook['token']}"
    path = os.path.join(PRIVATE, "report_webhooks.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f)
    log("webhooks: made for", ", ".join(C.WEBHOOKS), "(URLs in the private folder, not printed)")


def invite():
    inv = call("POST", f"/channels/{state['channels']['welcome']}/invites", {"max_age": 0, "max_uses": 0, "unique": False}, REASON)
    state["invite"] = "https://discord.gg/" + inv["code"]
    save_state(state)
    log("invite:", state["invite"])


# ------------------------------------------------------------------ check

def check():
    """What each role can do: nobody below Admin can manage roles, channels, webhooks, the server or mention everyone."""
    # (Proto Bot may mention roles in #releases and #announcements only, by channel overwrite: not a role power.)
    dangerous = ["ADMINISTRATOR", "MANAGE_ROLES", "MANAGE_CHANNELS", "MANAGE_GUILD", "MANAGE_WEBHOOKS", "MENTION_EVERYONE",
                 "MANAGE_GUILD_EXPRESSIONS", "MANAGE_EVENTS", "USE_EXTERNAL_APPS"]
    problems = 0
    for role in call("GET", f"/guilds/{G}/roles"):
        p = int(role["permissions"])
        has = [d for d in dangerous if p & P[d]]
        if role["name"] in ("Admin",) or role.get("tags", {}).get("bot_id"):
            log(f"  {role['name']}: {'Administrator' if p & P['ADMINISTRATOR'] else ', '.join(has) or 'no special powers'}")
            continue
        if has:
            problems += 1
        log(f"  {role['name']}: {'TOO MUCH: ' + ', '.join(has) if has else 'ok'}")
    log("check:", "ok" if problems == 0 else f"{problems} roles with too much")


STEPS = {"settings": settings, "roles": roles, "channels": channels, "online": online, "leaderboard": leaderboard, "console": console, "community": community, "automod": automod,
         "messages": messages, "guides": guides, "tutorials": tutorials, "welcome": welcome_screen,
         "onboarding": onboarding, "webhooks": webhooks, "invite": invite, "check": check}

if __name__ == "__main__":
    wanted = sys.argv[1:] or list(STEPS)
    for name in wanted:
        print("==", name, flush=True)
        STEPS[name]()
    save_state(state)
