YOUR TASK (release, step 3 of 3): release the fixes already committed for these issues as a bug-fix build.

Fixed (issue, line for the notes):
{fixes}

Follow Tools/Bugs/README.md step 4 and AGENT_HANDOFF.md "Build and release routine":
1. Write `Release/Release v2.0 Beta 1 (build N).md` where N is the next build number (Assets/ProtobotRebuilt/Resources/
   Version/build.txt + 1: the player build bumps it): "# Protobot Rebuilt V2, v2.0 Beta 1 (build N)", a "**Bug fixes.**"
   line, then one bullet per line above (no personal details).
2. `cd Tools/AgentTesting && bash release_build.sh` (it leaves Play and builds; the build number becomes N; it prints
   "STALLED:"/"FAILED:" and stops on a problem, and Tools/AgentTesting/status.txt shows its step: check that the time
   there is from this run before reading anything into it). Then in
   C:/Dev/ProtobotRebuiltV2/Build/Protobot Beta 1.9: delete ProtobotRebuiltV2_BurstDebugInformation_DoNotShip, copy the
   notes there as "Release notes.md".
3. From C:/Dev/ProtobotRebuiltV2: `vpk pack --packId ProtobotRebuiltV2 --packVersion 2.0.0-beta.1.N --packDir "Build/Protobot
   Beta 1.9" --mainExe ProtobotLauncher.exe --packTitle "Protobot Rebuilt V2" --releaseNotes "ProtobotRebuiltV2/Release/
   Release v2.0 Beta 1 (build N).md" --outputDir C:/Dev/ProtobotRebuiltV2/Releases`.
4. Revert the font atlases, commit the notes, build.txt and PatchNotes.txt (and anything the build step changed that
   belongs), push `git push origin v2-classic-protobot:main`.
5. `vpk upload github --repoUrl https://github.com/Cranezz/Protobot-Rebuilt --token <git credential fill, never printed>
   --publish --tag v2.0.0-beta.1.N --releaseName "v2.0 Beta 1 (build N)" --outputDir C:/Dev/ProtobotRebuiltV2/Releases`.
6. `python -I Tools/Discord/post_release.py N --short`, then `python -I Tools/Bugs/bugs.py fixed ISSUE --build N` for each
   issue above.
7. Add a short "Build N" line to CLAUDE.md (git-ignored: just edit it).
If the build or upload fails, don't retry blindly: say what failed (the fixes stay committed; push them if step 4 didn't).

Answer with the JSON the run asks for: "released" (true/false), "build" (N), "note".
