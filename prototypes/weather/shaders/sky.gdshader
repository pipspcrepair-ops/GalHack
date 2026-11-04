// The sky: an airbrushed gradient (1970s paperback cover), a sun, two cloud layers drifting
// with the wind, lightning lighting the clouds from inside, and a dark wall cloud over any
// tornado. The Mobile renderer has no volumetric fog, so all of the weather's depth comes
// from this plus depth fog and particles.
shader_type sky;

#include "res://shaders/weather.gdshaderinc"

uniform vec3 zenith : source_color = vec3(0.09, 0.2, 0.42);
uniform vec3 horizon : source_color = vec3(0.95, 0.77, 0.56);
uniform vec3 ground : source_color = vec3(0.35, 0.3, 0.2);
uniform vec3 cloud_lit : source_color = vec3(1.0, 0.95, 0.88);
uniform vec3 cloud_shade : source_color = vec3(0.42, 0.48, 0.6);
uniform vec3 sun_color : source_color = vec3(1.0, 0.85, 0.6);
uniform float sun_strength = 1.0;      // 0 under thick cloud
uniform float sun_size = 0.99965;
uniform vec3 flash_color : source_color = vec3(0.85, 0.8, 1.0);
uniform vec3 wall_cloud : source_color = vec3(0.12, 0.13, 0.12);
uniform sampler2D cloud_noise : filter_linear_mipmap, repeat_enable;

void sky() {
	vec3 dir = EYEDIR;
	float up = dir.y;

	// Gradient with a bright band at the horizon, the airbrushed look.
	vec3 col = mix(horizon, zenith, pow(clamp(up, 0.0, 1.0), 0.42));
	col += horizon * 0.22 * exp(-abs(up) * 14.0);
	if (up < 0.0) col = mix(horizon, ground, clamp(-up * 5.0, 0.0, 1.0));

	// Sun: disc and halo, dimmed by cloud along the way.
	vec3 to_sun = LIGHT0_DIRECTION;
	float sd = dot(dir, to_sun);
	float disc = smoothstep(sun_size - 0.0004, sun_size, sd);
	float halo = pow(max(sd, 0.0), 10.0) * 0.35 + pow(max(sd, 0.0), 220.0) * 0.8;

	float density = 0.0;
	if (up > 0.0 && !AT_CUBEMAP_PASS) {
		// Low layer: project the view ray onto the cloud plane above the camera.
		float t = (CLOUD_HEIGHT - POSITION.y) / max(up, 0.02);
		vec2 at = POSITION.xz + dir.xz * t;
		float n = cloud_noise_at(cloud_noise, at);
		density = cloud_from_noise(n);
		float toward_sun = cloud_density(cloud_noise, at + to_sun.xz * 260.0);
		float light = clamp(1.0 - toward_sun * 0.85 + density * 0.2, 0.0, 1.0);
		vec3 cloud = mix(cloud_shade, cloud_lit, light);
		// Thick cloud keeps its structure: thinner patches glow, thicker knots go dark.
		float thick = smoothstep(0.35, 0.75, n);
		cloud *= mix(1.15, 0.55, thick * weather_cloud.w) * (1.0 - weather_cloud.w * 0.35 * density);
		// Silver lining toward the sun.
		cloud += sun_color * pow(max(sd, 0.0), 6.0) * (1.0 - density) * 0.6 * sun_strength;

		// High streaky layer: cirrus, stretched along the wind.
		vec2 wind = weather_wind.xy;
		vec2 cirrus_at = POSITION.xz + dir.xz * ((CLOUD_HEIGHT * 3.0) / max(up, 0.02));
		vec2 streak = vec2(dot(cirrus_at, wind), dot(cirrus_at, vec2(-wind.y, wind.x)) * 4.0) * CLOUD_SCALE * 0.35;
		float cirrus = smoothstep(0.55, 0.85, texture(cloud_noise, streak - weather_cloud.xy * CLOUD_SCALE * 0.2).r);
		col = mix(col, cloud_lit, cirrus * 0.35 * (1.0 - weather_cloud.z * 0.6));

		// Fade the layer into the haze toward the horizon, where the projection gets noisy.
		float fade = smoothstep(0.0, 0.12, up) * exp(-t / 22000.0);
		col = mix(col, cloud, density * fade);
		density *= fade;

		// Lightning lights the clouds from inside.
		float toward_flash = max(dot(dir, normalize(weather_flash.xyz)), 0.0);
		float pockets = smoothstep(0.42, 0.62, n);
		col += flash_color * weather_flash.w * (0.08 + 0.9 * density * pockets) * (0.12 + 0.88 * pow(toward_flash, 4.0));
	}

	col += sun_color * (disc * 6.0 + halo) * sun_strength * (1.0 - density * 0.95);

	// A wall cloud lowers over a tornado: a dark, rotating knot in the cloud base.
	if (weather_tornado.z > 0.01 && up > -0.05) {
		vec3 top = vec3(weather_tornado.x, 175.0, weather_tornado.y) - POSITION;
		float dist = length(top.xz);
		vec3 to_top = normalize(top);
		float near = smoothstep(0.72, 0.99, dot(dir, to_top)) * clamp(1.6 - dist / 1500.0, 0.0, 1.0);
		// Only above the funnel's top: the cloud base, not a halo around the funnel.
		near *= smoothstep(-0.05, 0.08, dir.y - to_top.y + 0.12);
		col = mix(col, wall_cloud, near * weather_tornado.z * 0.9);
	}

	COLOR = col;
}
