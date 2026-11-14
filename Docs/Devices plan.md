# Devices menu (V5 Brain style): plan and findings (2026-11-14)

Replaces `Motion/MotorControls` (its window) and `Motion/ControlsPanel` with one Brain-style Devices panel, and makes
the sensors give real readings. Built 2026-11-14 (Build 30): `Scripts/Devices`.

## What the real Brain shows (researched)
- **Device Info:**
  - The top row is Controller, Battery and 3-Wire.
  - Below it is a grid of Smart Ports 1-21 (7 x 3); each used port shows its device and number.
- **Motor dashboard:**
  - Gears, tappable to change the cartridge (36:1 / 18:1 / 6:1).
  - `<` `■` `>` run it in reverse, stop it or run it forward.
  - Revolutions and Degrees.
  - Velocity in rpm, with a bar from -100% to +100%.
  - Power (W) and Torque (Nm) graphs against rpm %, each with a dot for where the motor is now.
- **3-Wire Ports:**
  - Two columns of cells, A-D and E-H.
  - Each cell shows the letter in a triangle, the mode and the value: "Analog Input 31 (0%)", "Digital Input High (1)", "Digital Out High (1) / Low (0)".
  - A solenoid is a Digital Out: Low is the default, High switches the air.
- **Distance (V5):**
  - Range 20-2000 mm. Accuracy is ±15 mm under 200 mm and 5% above that.
  - Shows distance (mm or in, toggled by touching it), object size (small/medium/large), velocity in m/s and whether an object is found.
  - The diagram draws the object, with an arrow when it's coming closer.
- **Optical (V5):** hue 0-359, brightness %, proximity, and a touchable LED brightness.
- **Rotation (V5):**
  - Angle 0-359.99°, position (turns, can be reset), velocity in rpm.
  - "Set zero" is kept in the sensor.

## Part ids and geometry (measured from the models, inches, part space)
- **SNSR (the original's):**
  - `SNSR-Distance-V5` (V5 distance) and `SNSR-Distance-Cortex` (ultrasonic).
  - `SNSR-Rotation-V5` (rotation: a 0.25" square hole at the origin, axis local +z) and `SNSR-Rotation-Cortex` (optical shaft encoder: a 1/8" square hole at the origin, +z).
  - `SNSR-Optical-V5` (optical) and `SNSR-Optical-Cortex` (light sensor).
  - `SNSR-Inertial-V5`, `SNSR-Vision-V5`, `SNSR-Line Tracker-Cortex`.
  - `SNSR-Potentiometer-V5` (3-wire: a 1/8" square hole at the origin, +z).
- **Switches:** `SWCH-Bumper-V5`, `SWCH-Bumper-Cortex`, `SWCH-Limit-Cortex`. The button is the Red Plastic sub-mesh; the limit switch's lever is Silver Steel.
- **VEX-CAD (`VXSENSORS-`):**
  - Light Sensor, Line Tracker V2, Optical Shaft Encoder, Ultrasonic Range Finder, Potentiometer V2.
  - Analog Accelerometer, Yaw Rate Gyroscope Sensor, GPS Sensor, V5 3 Wire Expander.
- **Which way flat sensors look** (distance, optical, ultrasonic, light, line trackers): away from the mounting holes, along the hole axis, toward the far side of the mesh. Distance V5 and Optical V5 look along local +y; check this against the "Black Screen" sub-mesh's normals.
- **Smart port sockets:** Distance V5 at z +0.433, Optical V5 at z -0.433, Inertial at z -0.602.

## Design
- **Device layer:** `Scripts/Devices` (namespace `Protobot.Devices`).
  - **Device classes:** a `V5Device` base, with a subclass per kind: motor, distance, optical, rotation, inertial, generic (vision/GPS/radio/expander), light, line tracker, potentiometer, switch, encoder (2 ports), ultrasonic (2 ports), solenoid (Digital Out driving the cylinders it's tubed to, found through the fittings' neighbours; a cylinder with no solenoid acts as its own).
  - **`V5Brain`:** the device list, rebuilt when `Dynamics.Version` or the wires change.
  - **Ports:**
    - A wired device takes the port its cable goes to; unwired devices get the free numbers in order, motors first, so motor labels stay the same.
    - `MotorDrive.Number` reads the port from `V5Brain`.
- **Readings:**
  - Sampled every frame while the panel is open, or later while robot code runs: in FixedUpdate during the sandbox (rotation needs it), in LateUpdate in the build.
  - Raycasts use Default plus layers 18/19 in the build, and layers 16-19 in the sandbox, positioned with `SimPoint`/`SimDirection`.
- **Motor numbers:**
  - Velocity = Level x Rpm.
  - Position is integrated from the velocity.
  - Torque = clamp(Stall x (Command - Level)), the same model as `Sandbox.Turn`.
  - Power = torque x ω; current = |torque| / stall x 2.5 A.
- **Manual `<` `■` `>`:**
  - A new `MotorDrive.Manual` (-1..1) that steps 25% per tap. MotorRunner and `Sandbox.Update` use it ahead of the keys.
  - It's cleared when the panel closes, as leaving the Brain's dashboard stops the motor.
- **Rotation, encoder and potentiometer:**
  - Angle = the signed angle about the hole axis between the housing's fixed local vector and the shaft's (sim-aware), plus the `zero:` token. Position is unwrapped.
  - **Must fix:** GearTrain's `Fill` locks any part whose square hole fits the shaft, so the sensor would turn with its shaft. Sensors need to be bearings instead.
- **Optical:**
  - Hue comes from the hit material: the sub-mesh at the hit triangle in the build, the renderer under the hit point in the sandbox.
  - Proximity is 0-255 within about 4"; LED setting `led:` token.
- **Inertial:**
  - Heading is clockwise about world up from its calibration; rotation is the unwrapped heading.
  - Pitch and roll come from its axes; acceleration is the second difference of its position minus gravity (386.1 in/s²).
- **Switches:** an OverlapBox on the button or lever, ignoring the switch itself and its own sim body.
- **Panel (`DevicesPanel`):** a floating window that can be dragged and minimized, replacing both old windows (update GameUi, Sandbox, TopBar and PartMenu).
  - **Top tiles:** Controller (drive mode, layout, camera in the sandbox), Battery, 3-Wire (the A-H page).
  - **Port grid:** 7 x 3.
  - **Card list:**
    - A minimized motor shows port, cartridge color, % and `<` `■` `>`.
    - An expanded motor adds the dashboard (gears, revolutions/degrees, velocity bar, power and torque graphs drawn with `ShapeGraphic`) and its settings (name, keys or Left/Right, reversed, geared-system info and Pair).
    - Sensor cards show the Brain's readings.
    - Pneumatic cards show port, position (Extended/Retracted/Loose) and mode, with a High/Low toggle.
  - **Simulation controls:** Pause, Reset and Stop in the header while simulating.
  - **Clicking a motor in the build:** opens the panel with that motor expanded and controlled; Ctrl+click adds more.
- **Sandbox additions:** `Sandbox.BodyOf(part)` and `Sandbox.SetExtended(cylinder, bool)`.
