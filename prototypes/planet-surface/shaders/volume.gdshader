// A body of something the light passes through (a dust storm, later a fog bank or a close cloud), drawn
// on any renderer by stepping through moving 3D noise inside it. The mesh is only a box round it.
// The shape is a
// rounded footprint with a top that can stand taller on its leading side (a wall), its outline pushed
// in and out by the noise so it heaps and rolls. It is depth-aware: it fills only the space in front of
// the first surface seen through a pixel, so what stands in front of it stays in front and what is in
// it fades by how deep in it is. What the environment fog already draws at the eye is taken off its
// density, so from inside, where the fog has thickened to its murk, it only adds its billows. It is lit
// by the same lights and sky as everything else (the sun, the night light, the sky's own ambient), so
// it never stands darker or brighter than the land at that hour: the march only works out how much of
// it the sky and the light reach.
//
// Seen from outside its box it is drawn on the box's near faces, depth-tested like anything else: at a silhouette
// the card keeps it off each sample of a near thing and on each of the sky behind, so nothing in front of it gets
// a halo. From inside its box (volume_inside.gdshader) it is drawn on the far faces with no test: nothing can be in
// front of where it starts.
shader_type spatial;
render_mode cull_back, depth_draw_never, shadows_disabled, specular_disabled;

#include "res://shaders/noise.gdshaderinc"
#include "res://shaders/volume.gdshaderinc"
