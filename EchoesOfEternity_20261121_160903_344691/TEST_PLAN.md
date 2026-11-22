# Acceptance checklist — initially UNRUN

Record Unity version, target OS, commands, results and unresolved failures.

ASSET/PROJECT
- Generator integrity checks pass; all manifest paths resolve.
- Unity imports models, textures, UI and audio without missing references.
- Materials look correct in chosen render pipeline.
- Builder can run twice without duplication or loss of user edits.
- Runtime assembly has no UnityEditor dependencies.

FIRST-PERSON AND UI
- WASD and mouse work in editor and standalone.
- Diagonal movement is not faster; walls block the player.
- Pitch is clamped; pause/focus loss releases cursor appropriately.
- Menus work at 720p and 1080p.
- Interaction does not work through walls.
- Pause freezes traps, platform and transitions; resume is consistent.

TIMELINES
- Q succeeds on safe common stationary floor.
- Q is rejected while airborne, riding platform, on hazards or respawning.
- Destination capsule obstruction rejects switch without changing state.
- Supporting floor contact does not cause every switch to fail.
- Rapid repeated Q presses do not produce conflicting transitions.
- Selected blockers change correctly; main support remains.
- Keys, solved puzzles and shortcuts persist across both timelines.
- Checkpoint load restores a valid timeline and collision state.

PUZZLES
- Correct rune order rewards SUN key once.
- Wrong order resets only unfinished sequence.
- Trap warning occurs before damage; cooldown prevents instant multihit.
- LEAF key route and its return shortcut work.
- Platform travels, waits and carries the player consistently.
- Boarding and disembarking work at both ends and varied frame rates.
- Falling or dying near platform recovers to clear stationary ground.
- Chest opens once, grants WAVE key once, opens its return shortcut.
- All six key collection orders can complete the level without softlocks.

PROGRESSION
- Heart door rejects 0, 1 and 2 keys; opens permanently with 3.
- Keys cannot duplicate through interaction, death or save reload.
- Heart requires access to its unlocked chamber and awards once.
- Exit rejects missing Heart and works after pickup in either timeline.
- Victory occurs once; final score includes one escape reward.

SAVE/RETRY
- Death restores health, checkpoint and retained progression coherently.
- Rune partial input resets; platform/trap transient state is safe.
- Close/reopen Continue works after each key and after Heart.
- Corrupt/missing save permits New Game without crashes.
- New Game clears run but preserves best score/settings.
- Completed run cannot Continue into a broken post-victory state.

EXPORT/EVALUATION
- BuildReport succeeds; process exit code and logs are inspected.
- Entire build folder is packaged.
- Fresh extraction launches without Unity Editor.
- Another person completes a standalone run.
- User can explain scripts and change trap timing, speed and key score.
