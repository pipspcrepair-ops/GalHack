// Shared by the sky, terrain and foliage shaders: the weather's global uniforms (declared in
// project.godot and set every frame by WeatherFx) and the cloud layer, so cloud shadows on
// the ground line up with the clouds in the sky.

global uniform vec4 weather_wind;    // xy wind direction (x, z), z speed m/s, w distance the air has travelled (m)
global uniform vec4 weather_cloud;   // xy cloud drift (m), z cover 0..1, w darkness 0..1
global uniform vec4 weather_ground;  // x wetness, y snow cover, z dust cover, w sun visibility 0..1
global uniform vec4 weather_flash;   // xyz direction toward the last lightning flash, w intensity
global uniform vec4 weather_tornado; // xy nearest tornado (x, z), z strength 0..1, w its radius
global uniform vec4 weather_air;     // x gustiness, y rain, z embers, w time of the last strike

const float CLOUD_HEIGHT = 900.0;
const float CLOUD_SCALE = 1.0 / 4200.0;

// The cloud field's raw noise above a world XZ point, about 0.5 ± 0.1.
float cloud_noise_at(sampler2D noise, vec2 world_xz) {
	vec2 uv = (world_xz - weather_cloud.xy) * CLOUD_SCALE;
	return texture(noise, uv).r * 0.7
		+ texture(noise, uv * 2.7 + vec2(0.37, 0.71)).r * 0.22
		+ texture(noise, uv * 7.3 + vec2(0.13, 0.29)).r * 0.08;
}

// Cloud density for a given noise value. The threshold is set so roughly `cover` of the sky is
// cloud, with crisp cumulus edges; at full cover everything is cloud.
float cloud_from_noise(float n) {
	float cover = weather_cloud.z;
	float threshold = 0.64 - 0.28 * cover - 0.22 * pow(cover, 4.0);
	return smoothstep(threshold - 0.03, threshold + 0.09, n);
}

// Density of the cloud layer above a world XZ point, 0..1.
float cloud_density(sampler2D noise, vec2 world_xz) {
	return cloud_from_noise(cloud_noise_at(noise, world_xz));
}

// How much a point on the ground is shaded by the cloud above it toward the sun.
float cloud_shadow(sampler2D noise, vec3 world, vec3 to_sun) {
	vec2 at = world.xz + to_sun.xz / max(to_sun.y, 0.2) * (CLOUD_HEIGHT - world.y);
	return cloud_density(noise, at) * weather_ground.w;
}

// The gust in the air at a point, 0..~1.6: fronts travel downwind with the wind itself.
float gust_at(vec2 world_xz) {
	vec2 dir = weather_wind.xy;
	float along = dot(world_xz, dir);
	float phase = (weather_wind.w - along) / 80.0;
	float g = sin(phase * 6.2831) * 0.55 + sin(phase * 18.8496 + 1.3) * 0.25; // whole periods per 80 m, so the travel can wrap
	return max(0.1, 1.0 + weather_air.x * g);
}
