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
- `post_release.py <build> [--no-ping]`: a short post (headlines + link) for a **big** release in #releases (Update Pings
  mention, published to following servers). Bug-fix builds aren't posted (the owner's rule: keep #releases short). Run it
  right after `vpk upload github`. The bot may mention roles only in #releases and #announcements (a channel overwrite).
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
