# Protobot Rebuilt V2, v2.0 Beta 1 (build 88)

**Events and tournaments: host a bracket, invite people with a code, and play it out online.**

- **Events** (Multiplayer > Events): anyone can host a tournament. Players join with the event's code (or from Open events if the host lists it) any time before it starts, and can close Protobot and come back.
  - **Starts** when the host presses Start, in a set time (5 minutes to 4 hours), or at a date and time, shown in each player's own time zone.
  - **Robots**: bring any robot (lock in a saved build before your first match), or **build a new one**: everyone gets the same build time (15 minutes to 4 hours) with a timer at the top of the screen, and the build that's open is locked in when time's up (or press I'm done).
  - **Single elimination bracket**, seeded at random, byes for odd numbers, drawn live on the event page with every score.
  - **Your match is up**: a card appears wherever you are in Protobot. Join now puts you in the match lobby with your locked-in robot; the match starts by itself once you're both in. Like a VEX tournament, a player who isn't there when the call time runs out (2 minutes by default) is a no-show, and each player has one 3-minute time-out. Ties are played again.
  - **The host** can start early, award a match or have it played again, remove a player, send everyone a message, or end the event.
  - **Champions**: every finished event's winner goes on the Champions list.
- **Loaders**: they look and work like VEX's now. The tube is clear with red or blue collars, and the whole tube (collars, handle, bolts) slides up 8.6" in its black bracket, like the game manual's drawing, so a robot can reach the Match Loads under it. Press **L** (change it in Settings > Keyboard shortcuts) to raise or lower your Loader during driver control. In online matches everyone sees it move.
- **Cups**: robot parts grab them properly. Their collision shape now covers the whole cup (parts used to sink up to 0.4" into it before touching), and pins nest in them as deep as in VEX's model. In Simulate, a cup or pin you place against a claw or intake is no longer ignored by the parts it starts next to.
- **String** (Motion > String and pulleys, asked for on the Discord): click where it's tied, each pulley, post or hole it goes round or through, then the last point again to tie it off; ending on a winch drum winds it on. Choose VEX rope, paracord, braided nylon line or braided fishing line, and how much slack. It follows its parts, hangs when slack, shows knots and the turns on the drum, and the parts list says how long to cut it. Select a string to move its points or send it round the other side of a pulley. VEX's pulley, winch drum, ratchet and pawl are in the same group.
  - **In Simulate it can't stretch**: a tight string stops what it's tied to (a travel stop for an arm or a flap), round every pulley in its way, and a string doubled back round a pulley on the moving part pulls it with twice the force (block and tackle). Point at a string to see how hard it's pulled; past its rating (VEX rope 100 lb, paracord 550 lb...) it breaks and its two ends hang loose.
  - **Winches**: a string ending on a winch drum winds on and off as a motor or the hand crank turns the drum, in Simulate and while building (it stops when the string's tight or all let out). In Simulate the motor feels the load: a 100 rpm motor stalls at the pull its 2.1 N·m gives at the drum.
  - **Releases**: tie an end round a pin (a screw, standoff or shaft) through a hole; when a piston or motor pulls the pin out past the loop, the string lets go.
  - "How string works" in the String tool, and VEX rope priced from the Winch and Pulley Kit (4 ft).
  - **Open chain**: choose VEX chain (normal or high strength) in the String tool for a run of chain bolted to parts at its ends, as chain lifts use. It's drawn link by link, runs round sprockets on their teeth, and in Simulate a motor's sprocket drives it (it can't slide round the sprocket like a rope round a pulley). Forced on past a hard stop, it skips a tooth.
  - **Cascade lifts**: string and chain through pulleys and sprockets on moving stages now follow each other as they should. A cascade's carriage keeps up with its stage both ways (lowering one used to crawl, the motor near its stall), and a stage stopped by a hard stop holds there at its motor's pull.
- **Sample builds: two cascade lifts** (Sample Builds > Cascade Lift (string) and Cascade Lift (chain)), on VEX's linear slides. Rubber bands raise them; a winch with VEX rope (string) or a chain loop on a 10T sprocket (chain) pulls them down, and a second rope or chain makes the carriage go twice as far as stage 2 (4.5" and 9"). U raises, J lowers.
- **Linear slides in Simulate**: a stage now hits what's on the part it slides in (a hard stop on the tower stops it), and stops cleanly at the slide's end even when a motor drives it there at stall.
- **Arms and lifts push like real V5 motors in Simulate**: a motor stalled against something (a hard stop, a chain, a string) now gives its full stall torque (it gave a few percent of it), and an arm let go holds still where it is. Drivetrains are unchanged.
- **Screws over bearings**: a screw placed into a bearing flat on a C-channel sometimes went into the channel's hole under it and sat half sunk in the bearing (thanks Rudra). The top hole of a stack now wins.
- **RoboSource spacers**: they no longer look inside out (their faces were drawn from the inside).
- **Shaft collars on a motor's shaft hold now**: a shaft in a V5 motor fastened nothing (the collars, channels and gears on it weren't part of the robot, so moving the robot left them behind). Builds that have this are fixed when they open.
- **Linear slides**: the slide truck sits in its slide member the way VEX's does, held in by the member's lips with its face just out of them (it sat against the back with its sides in the walls). The classic inner and outer slide trucks go in and round the structure rail (Structure > Rails > Rail). Builds saved with trucks the old way keep sliding.
  - **Slide a truck while building**: select a slide truck and drag the arrow beside it to slide it along its rail or slide member, with everything fastened to it (a stage, and a carriage riding in that stage); it snaps like the Transform tool (Shift: free). Slide it right off the end and it's no longer held by the rail.
- **Parts list**: the anti-static omni and traction wheels are gone (they were the same models as the omni and traction wheels; builds that use them still open).

## Also in this update (build 87)

**The Advanced Gear Kit works, hinges swing, and lots of parts placing better.**

- **Advanced Gear Kit:** bevel gears mesh at right angles, a worm turns its wheel (and the wheel can't turn the worm back), a gear drives a rack in a straight line, a lead screw drives its nut along it (1/5" a turn), a cam pushes its follower, and a ratchet's pawl lets its shaft turn one way only. They work when a motor runs, with the hand crank, and in the simulation. The kit's parts are VEX's green and gray, in plain plastic.
- **Hand crank:** select a hand crank and drag the ring round it to turn its shaft and everything on it.
- **Hinges:** one 5 hole hinge, the whole part with both leaves; select it and drag the ring round its pin to swing it open or shut (5° steps, Shift free).
- **Universal joint:** a bent joint turns the shaft on its other side properly.
- **Cam follower mounting bracket:** now in the Advanced Gear Kit, and a follower placed over it goes through its window.
- **Retainers:** a nut placed over a hex nut retainer drops into its pocket, lined up with it; the stray circle over the pocket and the false slots between the 4-post retainer's posts are gone. A nut placed over any hole sits flat on its face.
- **Spacers:** 1/2", 7/8" and 1" spacers snap onto screws and holes (the 1" one was turned the wrong way).
- **RoboSource spacers:** their extra thin (1/64"), thin (1/32") and thick (1/16") nylon washers, 1/4" OD spacers from 1/8" to 1", and the 1" fender washer, under Spacers > RoboSource.
- **Ctrl + mouse wheel while placing** goes through the part's sizes and kinds (flex wheels: every diameter and hardness).
- **Rubber bands:** one rubber band in the parts list; choose its size (#32, #64, #117B) in its bar, and click a band to set how far it's stretched.
- **Missing holes added:** VEX's drop off cam, the ratchet and winch drum, the HS 36T/48T/60T/84T gears' bolt holes, the 6P 24T sprocket, the 30°/45°/60° bent gussets, the brain mounts' and battery clip's slots, the bumper switch's slots, and the old 2.75" omni wheel.
- **Home menu:** going back to it with a build open shows your build, blurred, behind it; the Beta tester badge isn't cut off any more.
- **Update window:** its text no longer overlaps.
