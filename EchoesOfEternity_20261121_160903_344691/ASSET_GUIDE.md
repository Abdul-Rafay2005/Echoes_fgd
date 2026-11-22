# Asset guide

## Format

36 original static low-poly OBJ models with UVs and palette.mtl.
World textures and UI sprites are PNG. Audio is mono 22050 Hz PCM WAV.
No skeletal rigs, baked animations, Unity prefabs or gameplay scripts included.
The reference image's illustration quality is an art direction target;
these generated models are a simple prototype kit.

Coordinates: Y up, one unit = one metre.
Most pivots are centered at the base.
Floor, bridge and moving platform top surfaces are at local Y=0.
Check actual dimensions and OBJ orientation after Unity import.

## Environment

floor_clean / floor_moss: 2x2-metre tiles.
wall_clean / wall_ruined: 2-metre wall sections.
pillar / pillar_broken: tall and ruined landmarks.
archway: broad stone arch.
stairs: five 0.2-metre steps.
moving_platform: 2.4x2.4 metres; top at Y=0.
platform_rail: optional sides; do not block boarding ends.
bridge_2m: shared fixed walkway segment.
door_frame / door_panel / door_lock: separate frame and movable parts.
wood_crate, rubble, vine, fern, emblem: sparse decoration.

Visual ruins must not accidentally remove shared walkable support.
Use explicit gameplay blockers; do not infer collision from appearance.

## Chest

Create ChestRoot. Place chest_base at local (0,0,0).
Place chest_lid at local (0,0.6,-0.4), rotation zero.
Its pivot is on the rear hinge. Animate local X toward roughly -95 degrees.
Keep player collision on the chest body; lid normally needs no collider.
Add key reward via a controller, not by adding a new key on every animation.

## Interactions

key_sun / key_leaf / key_wave: distinct glyphs and accent colors.
rune_sun / rune_leaf / rune_wave: dark tablets with blue symbols.
Use the matching UI sun/wave/leaf icons for clues and input feedback.
heart: gold faceted objective, bottom at Y=0.
heart_pedestal: artifact display; place Heart above it.
hourglass: optional timeline viewmodel/prop.
spike_base: stationary two-metre trap plate.
spikes: separate child animated below/above plate.
pressure_plate: optional interactive rune base.
torch: add a warm light and subtle intensity variation.
checkpoint: blue marker.
key_pedestal: top around Y=0.93.

## Materials

Model MTL references ../Textures by relative path.
Assign actual Unity materials explicitly when needed; check render pipeline.
Blue rune materials may use emission. Gold artifact can glow.
Ordinary gold/brass trim should not all emit light.
Moss marks age and vegetation; blue marks timeline/rune interactions.
Danger red is a functional UI/trap accent beyond the five moodboard colors.
Parchment is provided for UI panels.

Past/Present can reuse meshes and use different material/light settings.
Do not assume MTL automatically creates correct URP/Standard shaders.

## Import and collision

World textures: sRGB, Repeat, mipmaps.
UI: Sprite (2D and UI), alpha transparency, Clamp, no mipmaps.
Use simple box/capsule colliders for gameplay.
Archways need separate upright colliders so openings remain traversable.
Do not add blocking colliders to ferns, vines, symbols or loose trim.
Treat pickup volumes as triggers separate from interaction blockers.
Generate lightmap UV2 only if baking.
Audio: loop only temple_ambience; lower ambient volume.
