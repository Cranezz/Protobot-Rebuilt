# Protobot Discord server

The official Protobot server is set up by script through the **Proto Bot** account (Discord application
1574094193188409467, not public: only the owner can add it). Nothing in this folder is secret.

Private files live outside the repository in `C:/Dev/ProtobotRebuiltV2/Discord`:
- `bot_token.txt`: the bot's token (never print it, never commit it; reset it in the Developer Portal if it leaks)
- `server_state.json`: ids of the server, roles, channels and the messages the bot posted (so reruns edit them)
- `report_webhooks.json`: the webhooks Protobot's reports go to (also kept on the server in Cloud Save private data)
- `protobot_icon.png`: the server and bot icon

## Files
- `server_content.py`: everything the server is: roles, channels and who may do what, forum tags, the welcome /
  rules / FAQ / download / staff messages, the tutorial guides and the pinned forum guides. Edit text here.
- `setup_server.py`: applies it. `python -I Tools/Discord/setup_server.py [steps]` (steps: settings roles channels
  community automod messages guides tutorials welcome onboarding webhooks invite check).
- `post_release.py <build> [--no-ping] [--video FILE]`: a short post (headlines + link) for a **big** release in
  #releases (Update Pings mention, published to following servers). `post_release.py <build> --short`: a bug-fix build,
  one line, no ping (the owner's rule: the full notes are on the GitHub release). Run it right after `vpk upload github`.
  The bot may mention roles only in #releases and #announcements (a channel overwrite).
- `discord_api.py`: the small REST client (no gateway, no commands: there is nothing in Discord to talk to the bot with).

## Permissions
Proto Bot normally has only: View, Send, Send in threads, Create public threads, Manage messages, Manage threads,
Embed, Attach, Read history, Add reactions, Pin messages. That's enough to edit its messages and posts
(`messages guides tutorials`). Steps that change the server's structure (settings, roles, channels, community, automod,
welcome, onboarding, webhooks) need Administrator: authorize it again with permissions=8
(https://discord.com/oauth2/authorize?client_id=1574094193188409467&scope=bot&permissions=8), run them, then authorize
it again with the reduced set (permissions=2252126231325760) and run `check`.

Roles, top to bottom: Admin (Administrator; only the owner gives it), Proto Bot, Developer, Moderator (kick, ban, time
out, manage messages/threads/nicknames, audit log), Helper (manage threads; manage messages in the support forums), Beta
Tester, VEX Team Member, Announcement/Update/Event Pings. Nobody below Admin can manage roles, channels, webhooks or the
server or mention everyone; self-assigned roles (onboarding) have no permissions. `check` lists any role with too much.

Reports from the app: Cloud Code `SendReport.js` posts bug reports to #app-bug-reports, player reports to
#player-reports and appeals to #appeals (Cloud Save private "secrets": webhook_bug / webhook_report / webhook_appeal,
discordWebhook = fallback). Set them with Tools > Protobot > Skills Leaderboard Moderation (`SetWebhook(url, key)`) or
`Tools/AgentTesting/cs/t_webhooks_set.cs`.

## Players online channel
The voice channel "🟢 Players online: N" at the very top (outside every category, nobody can join it) is made by
`setup_server.py online` (its id is `channels.online` in server_state.json). Cloud Code `Presence.js` renames it: on the
app's heartbeat when the count changed (at most once in 5 minutes: Discord allows two renames in 10), when a player
closes the app (`leaving`), and every hour from the UGS schedule "discord-online" (trigger "discord-online", no player),
so it goes back to "⚫ Players online: 0" when everyone has left. The bot token and the channel id are on the server
(Cloud Save private custom data "secrets": discordBotToken, discordOnlineChannel), set by
`Tools/AgentTesting/cs/t_discord_online.cs` (rerun it after resetting the token). The bot may manage that one channel by
overwrite, so the counter keeps working when its role is cut back to the reduced set.

## Skills leaderboard channel
#skills-leaderboard (COMMUNITY, read-only) holds one message (posted by `setup_server.py leaderboard`, id in
server_state.json `messages.leaderboard`) that Cloud Code `DiscordLeaderboard.js` edits: the top 10 of the Driving and
Programming Skills leaderboards, names through the app's name filter (its lists are copied in by
`Tools/AgentTesting/lb_filter.py`), players who deleted their data left out. UGS Triggers run it on every score posted
("discord-leaderboard": com.unity.services.leaderboards.score-submitted.v1) and hourly ("discord-leaderboard-hourly":
the "discord-online" schedule). Ids in Cloud Save private "secrets" (`Tools/AgentTesting/cs/t_discord_lb.cs`).

## Tutorial posts
`Tools/Video/publish_tutorials.py discord|announce` posts with links in <...> and the SUPPRESS_EMBEDS flag (no GitHub
card, no second copy of the video under the attached one).

## Player messages channel
#player-messages (🧾 PROTOBOT REPORTS, admins only; `setup_server.py console` makes it and pins the help) is how the
developer talks to the players online. Commands (typed by a person; the bot's own `!` messages count too, for tests):
`!ask <question>` (a card with a text box in every app online, and in apps opened while it's up), `!notify <text>`,
`!ask 2h ...` (how long: default 24h, at most 7d), `!test ask|notify ...` (only the players in Cloud Save private
"secrets"/"devPlayers"), `!list`, `!stop <id>|all`, `!help`. Cloud Code `Presence.js` reads the channel on the app's
heartbeat (at most every 20 s, whoever comes first) and hourly, and answers each command as a reply; `Feedback.js`
posts each answer as a reply to its question (3 per player per question, 10 an hour). The channel id is "secrets"/
"discordConsoleChannel" and devPlayers is set from PlayerPrefs "t_dev_players", both by
`Tools/AgentTesting/cs/t_discord_console.cs`. The same heartbeat also hands every app the newest release's build number
(GitHub's latest releases.win.json, at most once a minute), so the app's "Update available" card shows within a minute.
