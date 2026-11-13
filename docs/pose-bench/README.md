# Pose Bench

`pose-bench.html` is the owner's tool for fixing people's poses: a 3D figure with draggable elbows, wrists,
fingertips and palm marks for 67 keyframes (acts, blows, stances, carrying, weapon holds), a Play button that
animates each act from the edited keyframes, and per-pose hand shape (open, fist, pointing). It is a copy of a
published claude.ai artifact, so it needs the network for three.js; the owner edits the published one.

`posedump.json` is where the shoulders, elbows, wrists, feet and carried things really were in 44 poses, read
off the running rig (`HumanRig.ArmOf`, body frame, origin on the chest line, `out` toward each arm's own side)
for a 1.72 m reference person. The tool starts from it.

Re-measure after changing a pose (headless, through the Godot queue):

    godot-q.ps1 -Console -Headless --path prototypes/wildlife res://tests/BestiaryTest.tscn -After "--posedump=<out.json>"

The hand orientations in the tool's `HANDS` table are first guesses; the game had none when it was made.
