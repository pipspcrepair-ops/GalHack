shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

// Space dust: a few hundred specks that wrap round the camera in a box, so there is always
// something drifting past to show speed and turning. All on the GPU: the instances never move.

uniform float box_size = 600.0;
uniform float speck_size = 0.35;
uniform vec4 tint : source_color = vec4(0.9, 0.85, 0.75, 1.0);

varying float fade;

void vertex() {
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	vec3 cam = CAMERA_POSITION_WORLD;
	vec3 wrapped = mod(origin - cam + box_size * 0.5, box_size) - box_size * 0.5;
	vec3 right = INV_VIEW_MATRIX[0].xyz;
	vec3 up = INV_VIEW_MATRIX[1].xyz;
	float dist = length(wrapped);
	vec3 world = cam + wrapped + (right * VERTEX.x + up * VERTEX.y) * speck_size * (1.0 + dist * 0.004);
	fade = smoothstep(box_size * 0.5, box_size * 0.25, dist) * smoothstep(2.0, 12.0, dist);
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
}

void fragment() {
	vec2 uv = UV * 2.0 - 1.0;
	float spot = max(0.0, 1.0 - dot(uv, uv));
	ALBEDO = tint.rgb * spot * fade * 0.8;
}
