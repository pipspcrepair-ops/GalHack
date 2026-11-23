// The general volume seen from inside its box: drawn on the box's far faces with no depth test, as nothing can stand
// in front of where it starts (volume.gdshader has the rest).
shader_type spatial;
render_mode cull_front, depth_draw_never, depth_test_disabled, shadows_disabled, specular_disabled;

#include "res://shaders/noise.gdshaderinc"
#include "res://shaders/volume.gdshaderinc"
