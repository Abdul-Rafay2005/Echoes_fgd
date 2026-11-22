# Echoes of Eternity — implementation specification

## Core experience

First-person 3D temple puzzle adventure, approximately 8-12 minutes.
An explorer enters an ancient temple. The entrance seals.
Collect three keys, open the Heart chamber, retrieve the Heart of Eternity
and return to the exit. Switch between Past and Present to reveal clues
and open different routes. Use one compact handcrafted level.

Show a short introduction, then let gameplay explain the world.
Required: clear instructions, score, progress persistence, victory/defeat,
tested standalone executable and editable Unity project.
No enemies, combat, procedural maze, crafting, multiplayer or paid services.

## Controls

WASD movement, mouse look, E interact, Q switch timeline, Escape pause.
No jumping is required. Design platform endpoints for walking on/off.
Use a CharacterController with gravity and a child camera at eye height.
Clamp camera pitch. Normalize diagonal movement.
Use cursor lock during play; release it for menus and loss of focus.
A hidden capsule is sufficient for the first-person body.
A small hourglass viewmodel is optional after gameplay is reliable.

## Level

One entrance hub and three short branches:
- Rune Gallery awards SUN key.
- Spike Passage awards LEAF key.
- Moving Platform chamber awards WAVE key, held in a treasure chest.
All branches can be completed in any order.
Three keys unlock the Heart chamber.
Taking the Heart permanently unlocks the exit near the entrance.
Use bends, a short dead end and landmarks to make a readable compact maze.
Keep the outside courtyard small and decorative.

Use level_spec.json as topology and state data, not a completed scene.
Choose coordinates after checking prefab dimensions and player clearance.

## Timeline design

Only two authored states: Past and Present.
Past: intact architecture, restrained blue rune glow, fewer plants.
Present: moss, rubble, weathered decoration and selected blocked routes.
Never implement simulation of history or arbitrary causal time travel.

One persistent player, camera, HUD, progression object and common world root.
Shared root includes main floor, checkpoints, pickups, traps and moving platform.
Past/Present roots hold only mutually exclusive art and selected barriers.
Do not duplicate key pickups or stateful puzzle controllers in timeline roots.
Objects below a disabled root must not reset progress when reactivated.

Q is allowed only while:
- actively playing;
- grounded on explicitly marked shared stationary safe ground;
- off the moving platform, stairs and trap plates;
- outside death, respawn, interaction and previous transition.

Before switching, validate the player's capsule against target timeline
blocking geometry and verify support. Keep target blocker queries available
even when target render roots are hidden, using dedicated query layers or
another explicit safe validation implementation. Filter the player and
trigger-only objects. Allow normal floor contact; do not treat the supporting
floor as a wall overlap. Do not rely on querying disabled colliders.

If unsafe, keep the original state and display a short reason.
If valid, apply target world, lighting and timeline label atomically.
Use a brief visual fade and cooldown; no camera teleport.
Global collected keys and solved puzzles survive every switch.

## Three puzzles

1. Rune Gallery:
Past shows three symbols on a clue wall in order: SUN, WAVE, LEAF.
Present contains three interactive rune controls. Player enters the sequence
with E. Wrong input resets only the unfinished sequence with gentle feedback.
Correct input opens a small cache and awards SUN key once.
Keep a shared safe area at the clue/console so switching is reliable.
Use matching mesh and UI glyphs rather than text-only answers.

2. Spike Passage:
Past opens the corridor entrance; Present's blocked entrance is a selected
timeline barrier. The floor, spikes and safe waiting platforms are shared.
Trap cycle: safe, visible warning, active damage, recovery.
Place LEAF key beyond the trap on static safe floor.
Collecting it opens a permanent shared return shortcut to the hub.
Timeline changes cannot remove that shortcut.

3. Moving Platform:
Past clears access to a platform landing blocked in Present.
The platform itself exists in both timelines.
Use one kinematic platform between two visible endpoints with dwell time.
Player walks on/off without jumping. Position endpoints flush with landings.
Carry the CharacterController by platform displacement, not by parenting
the whole controller under the moving platform.
Check collisions when applying platform displacement.
Q is disabled while riding. Avoid crushing passages.
The destination chest opens with E and awards WAVE key once.
Opening it also opens a permanent shared return shortcut.

## Heart and escape

Display key count 0/3 with each key's identity stored separately.
Heart chamber stays locked at 0-2 keys and opens permanently at 3.
Keys are not consumed and are not needed again for repeated door triggers.
Heart pickup awards once and permanently unlocks the entrance exit.
Exit works in either timeline after Heart collection.
Update objective and guide back with blue lamps or gold exit marker.
No countdown is required in the core scope. Use audio/lighting for urgency.
Win only with Heart owned and player inside the correct exit trigger.

## State, scoring and saving

Explicit states: Menu, Playing, Transitioning, Paused, Lost, Won.
Health starts at 3. Expose trap timing, damage, movement and score values.
Damage immunity of at least 1 second prevents instant repeated hits.
Reaching zero HP enters Lost with Retry and Main Menu.
Falling enters the controlled loss/recovery flow without repeated triggers.

Suggested scores: 100 per key, 300 Heart, 500 escape.
Stable IDs prevent duplicate rewards.
Track deaths independently. Only completed runs update best score.

Checkpoints always occupy common stationary clear floor.
Retry restores 3 HP at checkpoint; retain collected keys, completed puzzles,
Heart ownership, opened shortcuts and score. Reset temporary rune input.
Restore a specified checkpoint timeline after validating the safe position.
Clear player velocity and platform support; reset platform to a safe endpoint.
Normalize trap timers to give a safe recovery interval.
Never return a player to a closed room after restoring progression.

Save a versioned run snapshot at safe checkpoints:
checkpoint ID, timeline, keys, puzzle completions, Heart ownership,
permanent door/shortcut flags, score and deaths.
Use Application.persistentDataPath and atomic/recoverable file replacement.
Validate IDs and ranges. Corrupt/missing/incompatible saves allow New Game.
Continue always restores a checkpoint; do not save arbitrary platform positions.
New Game clears current progress but retains settings and best score.
Clear active-run save after victory.

## UI and audio

HUD: 3 health icons, keys 0/3, score, current objective, timeline name,
Q hint, center crosshair and E prompt for a valid nearby target.
Use a short center-camera raycast around 2 metres; filter player colliders.
Use text/symbol differences in addition to timeline colors.
Avoid interaction through walls.
Menus: New Game, Continue when available, Controls, Volume, Quit.
Pause freezes player, traps, platform and active game timers.
Timeline/menu state must not leave mouse look active under a menu.
Victory and defeat screens include score, deaths and functioning buttons.
Use responsive Canvas layout; verify 720p and 1080p.

Ambient audio quietly loops. Use restrained positional sounds for mechanisms.
Avoid loud full-volume playback of every synthesized sound simultaneously.
Control sound levels using a central audio/settings controller.

## Code organization

Suggested small classes:
FirstPersonMotor, PlayerInteractor, TimelineManager, TimelineSafeZone,
KeyPickup, KeyInventory, RunePuzzle, TimedSpikeTrap, MovingPlatform,
ChestInteractable, KeyDoor, HeartPickup, ExitTrigger, CheckpointManager,
SaveService, GameSession, HUDController and AudioManager.

Put editor code in an Editor folder. No UnityEditor imports in runtime.
Prefer serialized references and stable IDs over runtime name searches.
Keep tuning fields accessible for the user's live evaluation.
Use the input system already installed; avoid introducing both input stacks
without a supported reason and explicit configuration.

## Automatic project generation

Implement Editor menu and command-line builder that:
- imports and configures assets;
- creates pipeline-compatible Unity materials;
- creates prefabs with appropriate colliders;
- constructs the shared/Past/Present level roots;
- wires references, lights, puzzle state and UI;
- saves an editable scene and adds it to build settings.

Save generated content under a clearly owned Generated folder.
Builder reruns must not duplicate objects or overwrite hand-authored work.
Do not make the entire game exist only as runtime-generated objects.

Suggested entry points TO IMPLEMENT:
Echoes.Editor.ProjectBuilder.BuildProject
Echoes.Editor.ProjectBuilder.ValidateProject
Echoes.Editor.BuildGame.Build

These methods are requirements, not files already present in this asset pack.
Only invoke them after implementing and compiling them.

Create a wrapper that locates the installed editor, quotes paths, logs output,
preserves exit codes and checks BuildReport success.
After implementation, invoke Unity with:
-batchmode -quit -projectPath PATH
-executeMethod Echoes.Editor.BuildGame.Build -logFile build.log

Target the evaluation computer's OS. Do not assume Windows if user has
specified otherwise. Missing modules/licence must be reported accurately.
Package the entire generated build folder, not only the executable.

## Development order

1. Import and first-person movement with menu/cursor/pause.
2. Full greybox loop: keys -> door -> Heart -> exit, including loss/retry.
3. Safe timeline switching with explicit collision/support validation.
4. Rune, spikes and platform branches; all reference assignments.
5. Persistent saves, audio, visuals and readable UI.
6. Acceptance tests, standalone playtest, packaging and demonstration guide.

Do not add extra levels or systems before the full required loop works.
Do not change working mechanics merely to match decorative concept details.

## Final handoff

Commit Assets with .meta files, Packages/lock data and ProjectSettings.
Ignore Library, Temp, Obj, Logs and generated builds.
Do not push or publish unless requested.
Include setup/build instructions, asset credits, controls, known issues,
actual tests, and instructions for changing trap timing, speed and rewards.
Explain how timeline validation and persistent keys work.
