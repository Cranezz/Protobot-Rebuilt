# Protobot Rebuilt V2, v2.0 Beta 1 (build 84)

**Protobot on Mac, a path planner, a simpler match setup, chain tensioners, air in the tanks, and what your robot costs.**

- **Protobot on Mac (test version):** one app for Intel and Apple Silicon Macs, "Protobot Rebuilt V2 (Mac).zip" on the download page. It hasn't been tried on a real Mac yet, so please tell us in the Discord what works and what doesn't. The first time, right-click the app and choose Open (it isn't notarized by Apple). Macs don't update by themselves: download new versions from the releases page.
- **Path planner:** in the code window, Paths. Click points on a top view of the field, drag them and set each one's speed; the path is a smooth curve. It writes the code for your program: LemLib (a path file and follow), EZ-Template or Python, starting from where your robot starts. After a run it shows where the robot really went, and where its odometry thought it was.
- **Simpler match setup:** a map of the field to pick your Quadrant and click where along the wall to start; spots lined up with the tiles, as teams do on a real field (an edge on a tile seam, or centred on a tile); Straight, 45° and Backwards, or Aim at a Goal; one line that says whether the start is legal. The rest is under More options.
- **Chain tensioners:** a spacer or sprocket idler on an arm pulled by a rubber band keeps a chain tight, in the build and in the simulation. The Chain tool takes spacers as idlers. Sample: Sample Builds/Chain Tensioners.pbb.
- **Chains hold in the simulation:** a chain no longer stretches: a sprocket on a swinging arm is stopped by its chain.
- **Air:** cylinders use the air in your tanks. Each move takes some, and they push weaker as the pressure drops (one V5 tank: about 35 moves to half force). The Devices window shows the pressure, what the tanks are charged to, and a regulator's setting. A robot with no tank still has unlimited air.
- **Toggles turn:** a robot's arm can now flip a Toggle (it took about ten times a real Toggle's push before), and a flipped Toggle counts as set.
- **What your robot costs:** Parts list > Cost: every part at VEX's price (a cut channel costs its share of the full length), what your team already owns, and What to order: the VEX packs to buy, as a spreadsheet.
