// Play mode: this player's id, whether it's past the terms, the heartbeat's last answer (players online, newest build)
// and the notice card showing now.
return "pid " + Protobot.Online.OnlineServices.PlayerId + " ready " + Protobot.Online.AgeCheck.Ready + " online " + Protobot.Online.Presence.Online
    + " latest " + Protobot.Online.Presence.Latest + " card " + (Protobot.Online.Notices.Showing ?? "none") + " result " + (Protobot.Online.Notices.LastResult ?? "-");
