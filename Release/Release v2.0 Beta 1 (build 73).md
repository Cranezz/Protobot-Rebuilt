# Protobot Rebuilt V2, v2.0 Beta 1 (build 73)

**Bug fixes.**

- **V5 Smart Cables are gone from the parts list** (and from E over a port): they were only for looks and placing them made the app lag. Builds that already have some still open with them. Devices take the next free Brain port and keep it, and Insert devices writes the code to match. 3-wire cables are still there.
- **Opening a build while simulating** now stops the simulation first (it kept running on the old robot and logged an error every step).
