# Protobot Rebuilt

A VEX V5 robot design tool: build robots from full-detail VEX parts, see them move, and drive them on the
competition field, alone or online with other players.

Protobot Rebuilt continues the Protobot project started by [davegersh](https://github.com/davegersh), carried on as
[Protobot Rebuilt by BreadSoup](https://github.com/BreadSoup/Protobot-Rebuilt). This is version 2 ("V2"): the same
app, with the same look, menus and behavior, moved to Unity 6 and grown from there.

## What's in V2

- **Parts:** the full VEX-CAD library at full detail, a parts list with categories, families and search, imported
  custom parts (STL, OBJ, 3MF, GLB, PLY) and a SolidWorks-style polycarbonate sketcher with the plastic rules.
- **Building:** quick fastening (screw and nut through touching holes), parts that slide along screws and shafts,
  mirroring, a saw, drill, bend tool, measure tool, paint, pneumatic tubing, wiring, rubber bands and zip ties.
- **Motion:** motors, gears, chains, sprockets, cylinders, hinges, slides and joints that move in the build, and a
  physics sandbox to drive the robot.
- **The game:** the 2026-27 V5RC Override field, Robot Skills and match practice with the manual's start rules and
  scoring, and an end-of-run score reveal.
- **Online:** host or join matches by invite code (Unity Relay and Lobby), 2v2 with each player driving their own
  robot without delay, the holding rule, rejoining, reports and vote kick.
- **Everything else:** graphics presets from Very Low to Extreme with a benchmark, every key rebindable, autosave,
  bug reports, and zip-based build files (old ones still open).

## Building it yourself

1. Install [Git LFS](https://git-lfs.com) before cloning: the part meshes and field models are stored with it.
2. Open the project in **Unity 6.3 LTS (6000.3.21f1)**. The main scene is `Assets/ProtobotRebuilt/Scenes/Main.unity`.
3. Online play needs the project linked to your own Unity Cloud project (Edit > Project Settings > Services); Relay
   and Lobby work on Unity's free tier.
4. Bug reports and player reports go to a Discord webhook read from `Assets/ProtobotRebuilt/Resources/Secrets/BugWebhook.txt`.
   That file isn't in the repository: without it, reports are saved on the computer instead.

Notes for working on the code (structure, conventions, how to test) are in [CLAUDE.md](CLAUDE.md) and
[AGENT_HANDOFF.md](AGENT_HANDOFF.md); the plan for what's next is in [Docs/Roadmap.md](Docs/Roadmap.md).

## Credits

- **davegersh:** the original Protobot.
- **BreadSoup:** Protobot Rebuilt (1.3.7), which this version builds on.
- **VEX Robotics:** the VEX-CAD part models and the Override field model. Not an official VEX product.
- **Third-party:** DOTween, Easy Performant Outline, RestClient and StandaloneFileBrowser (in `Assets/ThirdParty`).

## License

[GPLv3](LICENSE), like Protobot Rebuilt.
