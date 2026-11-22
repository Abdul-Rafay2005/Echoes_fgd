# Echoes of Eternity

This pack contains original procedural starter art, audio and a build brief.
It is NOT a complete Unity game. It contains no implemented gameplay scripts,
Unity scenes, prefabs or executable. Those are the coding agent's next task.
Models are static OBJ meshes; animations must be implemented in Unity.
The supplied mood board is a visual reference and is not included in this pack.

## Agent instruction

Read BUILD_BRIEF.md, ASSET_GUIDE.md, TEST_PLAN.md and level_spec.json.
Inspect the current repository, installed Unity editor and build modules.
Implement the complete game using these assets. Generate editable scenes,
materials and prefabs with an Editor builder so the user does not need
to assemble the level manually. Validate available behaviour and export a
standalone build for the evaluation computer. Preserve all existing work.

## Existing project

Inspect git status and repository instructions before edits.
Merge Assets/_Echoes into the current project. Resolve file conflicts by
inspection. Do not overwrite existing ProjectSettings or Packages blindly.
Keep the current editor version, rendering pipeline and input system.
Do not create another worktree or replace a working Borrowed Light project
without preserving it. If both games exist, keep their assets and scenes
separate and explicit in build settings.

## New project

Locate Unity installed through Unity Hub. Prefer the course's editor.
Create UnityProject/ using the installed editor, then copy Assets into it.
Use a simple Built-In 3D setup unless the user already uses URP.
Record the exact editor version actually used.
A terminal alone cannot compile Unity without an installed/licensed editor.
If Unity or the export module is missing, finish source work, then report
the precise missing prerequisite. Never fabricate successful build results.

## Required final outputs

- Complete editable Unity project.
- Assets/Editor/EchoesProjectBuilder.cs.
- Assets/Editor/EchoesBuild.cs.
- A tested local build wrapper: tools/build.ps1 or tools/build.sh.
- Standalone build and ZIP containing the entire build directory.
- README with controls, setup/build steps, tuning and actual test results.
- A short explanation of timeline switching, persistence and puzzles.

The user can run this generator without installing Python packages.
Unity itself must be installed separately for the coding agent's build work.
