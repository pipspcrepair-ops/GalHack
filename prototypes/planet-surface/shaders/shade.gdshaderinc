// The far land's shade, past the sun's real-time shadows (FarShade, marched on the graphics card): a
// square of the land, each cell holding how steeply the land, woods and places rise toward the sun from
// its own ground. A cell whose rise is above the sun is in shade, softly either side. The shade only stands in where the real
// shadows have faded (shade_reach: the sun's shadow distance), and joins them before they fade, so
// the two never show a line: a point is as lit as the darker of the two says.
//
// A shader that includes this lights itself (light() below, the engine's Burley diffuse and GGX
// specular) with the shade applied to the sun alone, and sets far_sun in its fragment():
//     far_sun = far_lit(world position, CAMERA_POSITION_WORLD);

uniform sampler2D shade_map : filter_linear, repeat_disable;
uniform vec4 shade_rect = vec4(0.0, 0.0, 0.0, 0.0);     // x0, z0, side (0: none)
uniform vec4 shade_sun = vec4(0.0, 1.0, 0.0, 1.0);     // toward the sun (unit), and its rise (a tangent)
uniform float shade_reach = 320.0;                       // the sun's real-time shadow distance, m
uniform vec2 shade_join = vec2(0.7, 0.85);               // where the far shade comes in, and is full, as a share of that distance (FarShade.JoinFrom, JoinFull)

varying float far_sun;

const float SHADE_SOFT = 0.025;

float shade_lit(vec2 p) {
	if (shade_rect.z <= 0.0) return 1.0;
	vec2 uv = (p - shade_rect.xy) / shade_rect.z;
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return 1.0;
	float rise = texture(shade_map, uv).r;
	return 1.0 - smoothstep(-SHADE_SOFT, SHADE_SOFT, rise - shade_sun.w);
}

// How much of the sun a point gets by the far shade: 1 inside the real shadows' reach, the shade
// coming in over their last stretch (from 70% to 85% of it, before they fade from 85%).
float far_lit(vec3 p, vec3 eye) {
	float away = length(p.xz - eye.xz);
	float w = smoothstep(shade_reach * shade_join.x, shade_reach * shade_join.y, away);
	if (w <= 0.0) return 1.0;
	return mix(1.0, shade_lit(p.xz), w);
}

void light() {
	float cndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	float att = ATTENUATION;
	// The far shade is the sun's: not the moon's or a lamp's.
	if (LIGHT_IS_DIRECTIONAL && dot(LIGHT, shade_sun.xyz) > 0.98) att = min(att, far_sun);
	if (cndl > 0.0 && att > 0.0) {
		vec3 h = normalize(VIEW + LIGHT);
		float cndv = max(dot(NORMAL, VIEW), 1e-4);
		float cldh = clamp(dot(LIGHT, h), 0.0, 1.0);
		float cndh = clamp(dot(NORMAL, h), 0.0, 1.0);
		// Burley diffuse, as the engine has it.
		float fd90 = 2.0 * cldh * cldh * ROUGHNESS - 0.5;
		float fdv = 1.0 + fd90 * pow(1.0 - cndv, 5.0);
		float fdl = 1.0 + fd90 * pow(1.0 - cndl, 5.0);
		DIFFUSE_LIGHT += (1.0 / PI) * fdv * fdl * cndl * att * LIGHT_COLOR;
		// GGX specular (a dielectric).
		float alpha = ROUGHNESS * ROUGHNESS;
		float a2 = alpha * alpha;
		float dd = cndh * cndh * (a2 - 1.0) + 1.0;
		float d = a2 / (PI * dd * dd);
		float v = 0.5 / mix(2.0 * cndl * cndv, cndl + cndv, alpha);
		float f0 = 0.16 * SPECULAR_AMOUNT * SPECULAR_AMOUNT;
		float f = f0 + (1.0 - f0) * pow(1.0 - cldh, 5.0);
		SPECULAR_LIGHT += cndl * d * v * f * att * LIGHT_COLOR;
	}
}
