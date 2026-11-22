#!/usr/bin/env python3
"""Echoes of Eternity asset generator. Python 3.9+. No dependencies."""

from pathlib import Path
from datetime import datetime
import hashlib
import io
import json
import math
import random
import struct
import wave
import zipfile
import zlib

ROOT = Path.cwd() / (
    "EchoesOfEternity_" + datetime.now().strftime("%Y%m%d_%H%M%S_%f")
)
ROOT.mkdir(exist_ok=False)
FILES = {}
MODELS = []
BASE = "Assets/_Echoes/"
COLORS = {
    "Moss": "#587D32",
    "Stone": "#8A8C87",
    "Blue": "#35ADFF",
    "Gold": "#F2BA55",
    "Wood": "#80512F",
    "Shadow": "#19232B",
    "Brass": "#9C763B",
    "Danger": "#BA4B3F",
    "Parchment": "#D9B47B",
}

def rgb(hex_color):
    return tuple(int(hex_color[i:i + 2], 16) for i in (1, 3, 5))

def put(name, data):
    FILES[name] = data.encode("utf-8") if isinstance(data, str) else data

def png_chunk(kind, data):
    return (
        struct.pack(">I", len(data)) + kind + data
        + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
    )

def make_png(w, h, pixels):
    raw = b"".join(
        b"\0" + bytes(pixels[y*w*4:(y+1)*w*4]) for y in range(h)
    )
    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
        + png_chunk(b"IDAT", zlib.compress(raw, 9))
        + png_chunk(b"IEND", b"")
    )

# Small repeatable world textures.
for material, value in COLORS.items():
    color = rgb(value)
    pixels = []
    for y in range(128):
        for x in range(128):
            detail = (
                3 * math.sin(math.tau*x/16) * math.cos(math.tau*y/16)
                + 2 * math.sin(math.tau*(x+y)/32)
            )
            if material in ("Stone", "Shadow"):
                if y % 64 < 3 or (x + (y//64)*32) % 64 < 3:
                    detail -= 24
            if material == "Wood":
                detail += 7 * math.sin(math.tau*x/32)
                if x % 32 < 2:
                    detail -= 20
            pixels.extend(
                [max(0, min(255, round(c + detail))) for c in color] + [255]
            )
    put(BASE + "Art/Textures/" + material.lower() + ".png",
        make_png(128, 128, pixels))

class Mesh:
    """Triangulated flat-shaded geometry with planar UVs."""
    def __init__(self, name):
        self.name = name
        self.vertices = []
        self.faces = []

    def polygon(self, points, material, center):
        points = list(points)
        a, b, c = points[:3]
        u = [b[i]-a[i] for i in range(3)]
        v = [c[i]-a[i] for i in range(3)]
        normal = (
            u[1]*v[2]-u[2]*v[1],
            u[2]*v[0]-u[0]*v[2],
            u[0]*v[1]-u[1]*v[0],
        )
        middle = [sum(p[i] for p in points)/len(points) for i in range(3)]
        if sum(normal[i]*(middle[i]-center[i]) for i in range(3)) < 0:
            points.reverse()
        first = len(self.vertices)+1
        self.vertices.extend(points)
        for i in range(1, len(points)-1):
            self.faces.append((material, (first, first+i, first+i+1)))

    def box(self, center, size, material="Stone", angle=0):
        # Rotation about Z; angle in radians.
        cx, cy, cz = center
        sx, sy, sz = [s/2 for s in size]
        co, si = math.cos(angle), math.sin(angle)
        vertices = []
        for x, y, z in (
            (-sx,-sy,-sz), (sx,-sy,-sz), (sx,sy,-sz), (-sx,sy,-sz),
            (-sx,-sy,sz), (sx,-sy,sz), (sx,sy,sz), (-sx,sy,sz),
        ):
            vertices.append((cx+x*co-y*si, cy+x*si+y*co, cz+z))
        for face in (
            (0,1,2,3), (4,5,6,7), (0,1,5,4),
            (3,2,6,7), (0,3,7,4), (1,2,6,5),
        ):
            self.polygon([vertices[i] for i in face], material, center)

    def taper(self, base, r0, r1, height, material="Stone", sides=8):
        x, y, z = base
        low, high = [], []
        for i in range(sides):
            t = i*math.tau/sides
            low.append((x+r0*math.cos(t), y, z+r0*math.sin(t)))
            high.append((x+r1*math.cos(t), y+height, z+r1*math.sin(t)))
        center = (x, y+height/2, z)
        self.polygon(low, material, center)
        self.polygon(high, material, center)
        for i in range(sides):
            j = (i+1) % sides
            self.polygon([low[i], low[j], high[j], high[i]], material, center)

    def gem(self, center, radius, height, material="Gold", sides=6):
        x, y, z = center
        ring = [
            (x+radius*math.cos(i*math.tau/sides), y,
             z+radius*math.sin(i*math.tau/sides))
            for i in range(sides)
        ]
        for i in range(sides):
            j = (i+1) % sides
            self.polygon(
                [ring[i], ring[j], (x,y+height/2,z)], material, center
            )
            self.polygon(
                [ring[j], ring[i], (x,y-height/2,z)], material, center
            )

    def ring(self, center, radius, thickness, material="Gold", count=16):
        x, y, z = center
        for i in range(count):
            t = i*math.tau/count
            self.box(
                (x+radius*math.cos(t), y+radius*math.sin(t), z),
                (thickness, 2*radius*math.sin(math.pi/count)*1.07, thickness),
                material, t,
            )

    def save(self):
        lines = [
            "# Procedural Echoes of Eternity mesh",
            "mtllib palette.mtl", "o " + self.name, "s off",
        ]
        lines += ["v %.6f %.6f %.6f" % p for p in self.vertices]
        for material, ids in self.faces:
            points = [self.vertices[i-1] for i in ids]
            u = [points[1][i]-points[0][i] for i in range(3)]
            v = [points[2][i]-points[0][i] for i in range(3)]
            normal = (
                u[1]*v[2]-u[2]*v[1],
                u[2]*v[0]-u[0]*v[2],
                u[0]*v[1]-u[1]*v[0],
            )
            dominant = max(range(3), key=lambda i: abs(normal[i]))
            axes = ((1,2), (0,2), (0,1))[dominant]
            for p in points:
                lines.append("vt %.6f %.6f" % (p[axes[0]]/2, p[axes[1]]/2))
        last, uv = None, 1
        for material, ids in self.faces:
            if material != last:
                lines.append("usemtl EE_" + material)
                last = material
            lines.append("f " + " ".join(
                "%d/%d" % (vertex, uv+i) for i, vertex in enumerate(ids)
            ))
            uv += 3
        path = BASE + "Art/Models/" + self.name + ".obj"
        put(path, "\n".join(lines) + "\n")
        MODELS.append({
            "name": self.name,
            "path": path,
            "triangles": len(self.faces),
            "bounds_min": [
                round(min(p[i] for p in self.vertices), 4) for i in range(3)
            ],
            "bounds_max": [
                round(max(p[i] for p in self.vertices), 4) for i in range(3)
            ],
        })

mtl = []
for material in COLORS:
    mtl += [
        "newmtl EE_" + material, "Ka 0 0 0", "Kd 1 1 1",
        "Ks 0.05 0.05 0.05", "Ns 10", "d 1", "illum 2",
        "map_Kd ../Textures/" + material.lower() + ".png", "",
    ]
put(BASE + "Art/Models/palette.mtl", "\n".join(mtl))

# ENVIRONMENT
for name in ("floor_clean", "floor_moss"):
    m = Mesh(name)
    m.box((0,-.15,0), (2,.3,2))
    if name == "floor_moss":
        for x, z, s in ((-.6,-.65,.38), (.55,.65,.45), (-.7,.4,.24)):
            m.box((x,.008,z), (s,.016,s*.7), "Moss")
    m.save()

for name in ("wall_clean", "wall_ruined"):
    m = Mesh(name)
    if name == "wall_clean":
        m.box((0,1.5,0), (2,3,.45))
    else:
        m.box((0,.8,0), (2,1.6,.45))
        m.box((-.65,1.95,0), (.7,.7,.45))
        m.box((.65,1.8,0), (.7,.4,.45))
    m.box((0,.15,0), (2.1,.3,.6), "Shadow")
    if name == "wall_clean":
        m.box((0,2.85,0), (2.1,.3,.6), "Shadow")
    m.save()

for name, height in (("pillar",3), ("pillar_broken",1.3)):
    m = Mesh(name)
    m.box((0,.15,0), (.95,.3,.95))
    m.box((0,height/2,0), (.65,height,.65))
    if name == "pillar":
        m.box((0,height-.12,0), (.95,.24,.95))
    else:
        m.gem((.05,height,0), .32,.28, "Stone",4)
    m.save()

m = Mesh("archway")
for x in (-1.6,1.6):
    m.box((x,1.5,0), (.65,3,.7))
for i in range(9):
    t = i*math.pi/8
    m.box((1.6*math.cos(t),3+1.6*math.sin(t),0),
          (.65,.66,.7), angle=t)
m.save()

m = Mesh("stairs")
for i in range(5):
    h = .2*(i+1)
    m.box((0,h/2,-.8+.4*i), (2,h,.4))
m.save()

m = Mesh("moving_platform")
m.box((0,-.2,0), (2.4,.4,2.4))
for x in (-1.1,1.1):
    m.box((x,.012,0), (.07,.024,2.2), "Blue")
m.save()

m = Mesh("platform_rail")
for z in (-.8,.8):
    m.box((0,.4,z), (.18,.8,.18))
m.box((0,.72,0), (.16,.16,2))
m.save()

m = Mesh("door_frame")
for x in (-1.25,1.25):
    m.box((x,1.5,0), (.5,3,.65))
m.box((0,3,0), (3,.5,.65))
m.save()

m = Mesh("door_panel")
m.box((0,1.4,0), (2.1,2.8,.3), "Wood")
for y in (.3,1.4,2.5):
    m.box((0,y,.18), (2.05,.13,.08), "Brass")
m.save()

m = Mesh("door_lock")
m.box((0,.18,0), (.35,.36,.12), "Gold")
m.ring((0,.44,0), .17,.055, "Brass",12)
m.box((0,.18,.07), (.055,.13,.02), "Shadow")
m.save()

# CHEST: lid has a hinge pivot; assembly documented below.
m = Mesh("chest_base")
m.box((0,.3,0), (1.2,.6,.8), "Wood")
for x in (-.48,.48):
    m.box((x,.3,.415), (.08,.6,.035), "Brass")
m.box((0,.35,.43), (.18,.2,.07), "Brass")
m.save()

m = Mesh("chest_lid")
m.box((0,.11,.4), (1.24,.22,.84), "Wood")
for x in (-.48,.48):
    m.box((x,.23,.4), (.08,.025,.84), "Brass")
m.save()

# KEYS AND DISTINCT RUNE SYMBOLS
def add_symbol(mesh, kind, center, size=1, material="Blue"):
    x, y, z = center
    if kind == "sun":
        mesh.ring((x,y,z), .22*size,.045*size,material,12)
        for i in range(8):
            t = i*math.tau/8
            mesh.box((x+.32*size*math.cos(t),y+.32*size*math.sin(t),z),
                     (.04*size,.11*size,.035*size),material,t-math.pi/2)
    elif kind == "leaf":
        for side in (-1,1):
            mesh.box((x+side*.08*size,y,z),
                     (.04*size,.44*size,.035*size),
                     material, side*.38)
        mesh.box((x,y-.12*size,z), (.035*size,.4*size,.035*size),material)
    else:
        for row in (-1,1):
            for i in range(3):
                mesh.box((x+(i-1)*.14*size,y+row*.12*size,z),
                         (.19*size,.045*size,.035*size),
                         material, (.45 if i%2 == 0 else -.45))

for kind, accent in (("sun","Gold"),("leaf","Moss"),("wave","Blue")):
    m = Mesh("key_" + kind)
    m.box((0,.3,0), (.055,.6,.055), "Brass")
    for y in (.1,.23):
        m.box((.085,y,0), (.17,.055,.055), "Brass")
    m.ring((0,.72,0), .16,.045, "Brass",12)
    add_symbol(m,kind,(0,.72,.04),.38,accent)
    m.save()

    m = Mesh("rune_" + kind)
    m.box((0,.55,0), (.8,1.1,.24), "Shadow")
    add_symbol(m,kind,(0,.58,.15),1,"Blue")
    m.save()

# OBJECTIVES AND INTERACTIONS
m = Mesh("heart")
m.gem((0,.55,0), .36,1.1,"Gold",6)
m.save()

m = Mesh("heart_pedestal")
m.taper((0,0,0),1.15,1.15,.2)
m.taper((0,.2,0),.72,.58,.65)
m.taper((0,.85,0),.68,.68,.12,"Brass")
m.save()

m = Mesh("hourglass")
for y in (.04,.72):
    m.taper((0,y,0),.25,.25,.06,"Brass")
for x in (-.17,.17):
    for z in (-.17,.17):
        m.box((x,.4,z),(.035,.66,.035),"Brass")
m.taper((0,.12,0),.17,.018,.27,"Gold")
m.taper((0,.4,0),.018,.17,.27,"Gold")
m.save()

m = Mesh("spike_base")
m.box((0,-.1,0),(2,.2,2),"Shadow")
for z in (-.87,.87):
    for x in (-.6,0,.6):
        m.box((x,.01,z),(.28,.02,.12),"Danger")
m.save()

m = Mesh("spikes")
for x in (-.6,0,.6):
    for z in (-.6,0,.6):
        m.taper((x,0,z),.17,.015,.85,"Brass",5)
m.save()

m = Mesh("pressure_plate")
m.box((0,.06,0),(1.5,.12,1.5),"Shadow")
m.box((0,.14,0),(1.25,.04,1.25),"Stone")
for x in (-.56,.56):
    m.box((x,.17,0),(.04,.02,1.1),"Blue")
m.save()

m = Mesh("torch")
m.box((0,.35,0),(.12,.7,.12),"Wood")
m.taper((0,.7,0),.17,.23,.18,"Brass")
m.gem((0,1,0),.13,.4,"Gold")
m.save()

m = Mesh("checkpoint")
m.taper((0,0,0),.55,.55,.14,"Shadow")
m.gem((0,.55,0),.15,.55,"Blue")
m.save()

m = Mesh("key_pedestal")
m.box((0,.08,0),(.8,.16,.8))
m.taper((0,.16,0),.28,.22,.65)
m.box((0,.87,0),(.65,.12,.65),"Brass")
m.save()

# DECORATION
m = Mesh("rubble")
for x,z,s,h in ((-.3,-.1,.6,.35),(.3,.2,.45,.6),(.1,-.35,.35,.2)):
    m.box((x,h/2,z),(s,h,s))
m.save()

m = Mesh("vine")
for i in range(8):
    x = math.sin(i*.8)*.12
    y = .15+i*.22
    m.box((x,y,0),(.035,.27,.035),"Moss")
    m.gem((x+(-1)**i*.13,y,.015),.1,.23,"Moss",4)
m.save()

m = Mesh("fern")
for i in range(8):
    t = i*math.tau/8
    m.gem((.22*math.cos(t),.23,.22*math.sin(t)),
          .1,.38,"Moss",4)
m.save()

m = Mesh("emblem")
add_symbol(m,"sun",(0,.4,0),1,"Brass")
m.save()

m = Mesh("bridge_2m")
m.box((0,-.18,0),(2,.36,2))
for x in (-.9,.9):
    m.box((x,.01,0),(.05,.02,1.9),"Blue")
m.save()

m = Mesh("wood_crate")
m.box((0,.4,0),(.8,.8,.8),"Wood")
for y in (.08,.72):
    m.box((0,y,.415),(.8,.09,.03),"Brass")
m.save()

# UI ICONS: import as sprites, not world textures.
def icon_shape(name,x,y):
    if name == "health":
        xx,yy = x*1.35,-y*1.35
        return (xx*xx+yy*yy-1)**3-xx*xx*yy**3 <= 0
    if name == "key":
        return (.23 < math.hypot(x,y+.37) < .38
                or abs(x)<.07 and -.15<y<.75
                or 0<x<.28 and .4<y<.53)
    if name == "heart":
        return abs(x)/.58+abs(y)/.85 < 1
    if name in ("sun","past"):
        r = math.hypot(x,y)
        return .32<r<.48 or (
            .55<r<.8 and
            abs(math.sin(4*math.atan2(y,x)))<.22
        )
    if name in ("leaf","present"):
        return (x*x/.25+y*y/.6<1
                and not (abs(x)<.035 and -.7<y<.6))
    if name == "wave":
        return any(abs(y-row-.14*math.sin(x*5))<.055
                   for row in (-.3,.05,.4)) and abs(x)<.75
    if name == "hourglass":
        return (abs(y)>.62 and abs(y)<.74 and abs(x)<.55
                or abs(y)<.62 and abs(abs(x)-abs(y)*.7)<.055)
    if name == "interact":
        return abs(x)<.65 and abs(y)<.65 and (
            abs(x)>.51 or abs(y)>.51
        )
    if name == "crosshair":
        return math.hypot(x,y)<.07 or (
            abs(x)<.025 and .17<abs(y)<.4
            or abs(y)<.025 and .17<abs(x)<.4
        )
    if name == "warning":
        return (abs(x)<.09 and -.7<y<.2
                or abs(x)<.1 and .4<y<.6)
    return False

ICON_NAMES = (
    "health","key","heart","sun","leaf","wave",
    "past","present","hourglass","interact","crosshair","warning",
)
for name in ICON_NAMES:
    material = (
        "Danger" if name in ("health","warning") else
        "Moss" if name in ("leaf","present") else
        "Blue" if name in ("wave","hourglass","past") else "Gold"
    )
    color = rgb(COLORS[material])
    pixels = []
    for y in range(128):
        for x in range(128):
            coverage = sum(
                icon_shape(name,(x+dx)/64-1,(y+dy)/64-1)
                for dx,dy in ((.25,.25),(.75,.25),(.25,.75),(.75,.75))
            )
            pixels.extend((*color,round(255*coverage/4)))
    put(BASE+"Art/UI/"+name+".png",make_png(128,128,pixels))

# SYNTHESIZED AUDIO: mono PCM WAV, 22050 Hz.
SOUNDS = {
    "footstep": (.14,[100,75],.65),
    "interact": (.2,[420,630],.05),
    "key_pickup": (.7,[523,659,784],0),
    "door_open": (1.6,[65,82],.6),
    "platform_move": (1.4,[55,73],.5),
    "spike_warning": (.4,[740,880],.03),
    "damage": (.3,[130,95],.4),
    "timeline_switch": (1.0,[147,294,588],.06),
    "rune_solved": (1.1,[392,494,587],0),
    "heart_pickup": (1.7,[440,554,659,880],0),
    "victory": (2.4,[392,494,587,784],0),
    "defeat": (1.8,[220,196,147],.03),
    "temple_ambience": (8,[55,82,110],0),
}
for index,(name,(duration,tones,noise)) in enumerate(SOUNDS.items()):
    rng = random.Random(900+index)
    rate = 22050
    data = bytearray()
    for i in range(int(duration*rate)):
        t = i/rate
        value = sum(math.sin(math.tau*f*t) for f in tones)/len(tones)
        if name == "temple_ambience":
            value *= .11*(.75+.25*math.cos(math.tau*t/duration))
        else:
            envelope = min(1,t/.018)*(1-t/duration)**1.7
            value = ((1-noise)*value+noise*rng.uniform(-1,1))*envelope*.4
        data.extend(struct.pack("<h",round(max(-1,min(1,value))*32767)))
    buffer = io.BytesIO()
    with wave.open(buffer,"wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(data)
    put(BASE+"Audio/"+name+".wav",buffer.getvalue())

# DOCUMENTATION FOR THE LOCAL CODING AGENT
put("START_HERE.md", """# Echoes of Eternity

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
""")

put("BUILD_BRIEF.md", """# Echoes of Eternity — implementation specification

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
""")

put("ASSET_GUIDE.md", """# Asset guide

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
""")

put("TEST_PLAN.md", """# Acceptance checklist — initially UNRUN

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
""")

put("STORYBOARD_PLAN.md", """# Mood board and storyboard submission

Title: ECHOES OF ETERNITY
Tagline: Explore - Solve - Escape

Visual identity:
Moss green foliage, gray stone, blue timeline/rune light,
gold objectives and warm wood. Low-poly ancient jungle temple.

Create six storyboard panels:
1. Arrival: explorer enters, door seals. First-person controls appear.
2. Echo: Q reveals the intact Past and a blue rune clue.
3. Memory: return to Present, enter rune sequence, gain first key.
4. Trial: navigate spikes and moving platform for remaining keys.
   Add small health-zero -> checkpoint retry inset.
5. Heart: three keys unlock chamber; collect golden artifact.
6. Escape: return to now-unlocked entrance and walk into daylight.

Each panel shows player action, objective and feedback.
Use the same temple design across all images.
Use actual game screenshots when available; distinguish concept images
from screenshots of implemented gameplay.
""")

put("ASSET_CREDITS.md", """# Provenance

Geometry, textures, UI icons and WAV audio were procedurally generated by
the accompanying Python script supplied through ChatGPT for this project.
No third-party artwork, audio, fonts or Unity binaries were downloaded.
Keep this provenance and record source/licence information for later assets.
""")

level = {
    "game": "Echoes of Eternity",
    "timelines": ["present", "past"],
    "start": {"area": "hub", "timeline": "present"},
    "keys": ["sun", "leaf", "wave"],
    "rune_sequence": ["sun", "wave", "leaf"],
    "areas": [
        {"id": "hub", "checkpoint": True, "shared_floor": True},
        {"id": "rune_gallery", "reward": "sun",
         "clue_timeline": "past", "console_timeline": "present"},
        {"id": "spike_passage", "reward": "leaf",
         "entry_timeline": "past", "permanent_return_on_reward": True},
        {"id": "platform_chamber", "reward": "wave",
         "entry_timeline": "past", "reward_container": "chest",
         "permanent_return_on_reward": True},
        {"id": "heart_chamber", "requires": ["sun", "leaf", "wave"]},
        {"id": "exit", "requires": ["heart"], "timeline": "either"},
    ],
    "connections": [
        ["hub", "rune_gallery"], ["hub", "spike_passage"],
        ["hub", "platform_chamber"], ["hub", "heart_chamber"],
        ["hub", "exit"],
    ],
    "note": "Topology for the coding agent; not a generated Unity scene.",
}
put("level_spec.json", json.dumps(level, indent=2) + "\n")
put("palette.json", json.dumps(COLORS, indent=2) + "\n")
put(".gitignore", """[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Ll]ogs/
UserSettings/
Build/
Builds/
.vs/
*.csproj
*.sln
*.user
.DS_Store
""")

# File integrity checks. These do not replace a Unity import/playtest.
def validate():
    for name,data in FILES.items():
        assert data, "Empty file: " + name
        if name.endswith(".png"):
            assert data[:8] == b"\x89PNG\r\n\x1a\n", name
            offset, compressed = 8, b""
            while offset < len(data):
                length = struct.unpack(">I",data[offset:offset+4])[0]
                kind = data[offset+4:offset+8]
                payload = data[offset+8:offset+8+length]
                crc = struct.unpack(
                    ">I",data[offset+8+length:offset+12+length]
                )[0]
                assert zlib.crc32(kind+payload)&0xffffffff == crc, name
                if kind == b"IDAT":
                    compressed += payload
                offset += length+12
            assert len(zlib.decompress(compressed)) == 128*(1+128*4), name
        elif name.endswith(".wav"):
            with wave.open(io.BytesIO(data),"rb") as w:
                assert w.getnchannels() == 1, name
                assert w.getsampwidth() == 2, name
                assert w.getnframes() > 0, name
        elif name.endswith(".obj"):
            vertices, uv_count, faces = [], 0, []
            for line in data.decode().splitlines():
                parts = line.split()
                if not parts:
                    continue
                if parts[0] == "v":
                    p = tuple(float(v) for v in parts[1:])
                    assert len(p) == 3 and all(math.isfinite(v) for v in p)
                    vertices.append(p)
                elif parts[0] == "vt":
                    uv_count += 1
                elif parts[0] == "f":
                    faces.append(parts[1:])
            assert vertices and faces, name
            for face in faces:
                assert len(face) == 3, name
                points = []
                for token in face:
                    vi,ti = map(int,token.split("/"))
                    assert 1 <= vi <= len(vertices), name
                    assert 1 <= ti <= uv_count, name
                    points.append(vertices[vi-1])
                u = [points[1][i]-points[0][i] for i in range(3)]
                v = [points[2][i]-points[0][i] for i in range(3)]
                cross = (
                    u[1]*v[2]-u[2]*v[1],
                    u[2]*v[0]-u[0]*v[2],
                    u[0]*v[1]-u[1]*v[0],
                )
                assert sum(c*c for c in cross) > 1e-16, name
    assert len(MODELS) == 36, "Unexpected model count"

validate()

# Include the generator itself when run from a saved file.
source = Path(__file__).resolve()
if source.is_file():
    put("tools/make_echoes_pack.py", source.read_bytes())

manifest = {
    "name": "Echoes of Eternity",
    "type": "Procedural starter assets and coding-agent implementation brief",
    "unity_import": "NOT RUN",
    "unity_compilation": "NOT RUN",
    "gameplay_validation": "NOT RUN",
    "models": MODELS,
    "world_textures": len(COLORS),
    "ui_icons": len(ICON_NAMES),
    "sounds": len(SOUNDS),
    "files": [
        {"path": name, "bytes": len(data),
         "sha256": hashlib.sha256(data).hexdigest()}
        for name,data in sorted(FILES.items())
    ],
}
put("manifest.json", json.dumps(manifest, indent=2) + "\n")

for name,data in FILES.items():
    destination = ROOT/name
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(data)

archive = ROOT.with_suffix(".zip")
with zipfile.ZipFile(
    archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9
) as output:
    for name,data in sorted(FILES.items()):
        output.writestr(ROOT.name+"/"+name,data)

with zipfile.ZipFile(archive) as check:
    assert check.testzip() is None, "ZIP integrity check failed"

print("\nCreated folder:", ROOT)
print("Created ZIP:", archive)
print("Models:", len(MODELS))
print("World textures:", len(COLORS))
print("UI icons:", len(ICON_NAMES))
print("Sounds:", len(SOUNDS))
print("Asset integrity checks passed.")
print("Unity import, compilation and gameplay still require local testing.")
print("Open the folder in VS Code and give START_HERE.md to your coding agent.")