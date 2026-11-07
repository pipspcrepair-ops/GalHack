// The ground, near and far. Vertex colour is the palette's colour (biome soil, the world's rock,
// ore) with alpha saying how bare it is; UV carries glow (x) and bedrock (y). On top: soil and
// rock detail from periodic noise (seamless across the planet's edges), horizontal strata on
// rock faces, snow settling on flat ground, rain darkening and glossing everything, dust.
// The far rings cut a hole where voxel chunks are loaded, so the two never overlap (see hole_sink).
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/noise.gdshaderinc"

global uniform vec2 planet_size;
global uniform vec4 weather_ground;   // wetness, settled snow, dust, solar

uniform bool far_ring = false;
uniform vec4 hole = vec4(0.0);        // x0, z0, x1, z1: where the finer level covers the ground
// Far rings and horizon: vertices at least hole_sink_from inside the hole drop hole_sink metres, and it
// is only cut hole_cut inside it. Where the coarser ground sits above the finer ground's edge, it
// then dives under that edge as a wall instead of leaving a gap the sky shows through.
uniform float hole_sink = 0.0;
uniform float hole_sink_from = 0.0;
uniform float hole_cut = 0.0;
uniform float strata = 0.4;
uniform float snow_altitude = 100000.0; // above this, flat ground is snowy whatever the weather
uniform float base_snow = 0.0;          // snow on flat ground everywhere (ice worlds)
uniform vec3 glow_color : source_color = vec3(1.0, 0.32, 0.06);
uniform float glow_strength = 3.0;
uniform vec3 dust_color : source_color = vec3(0.62, 0.5, 0.36);

uniform vec3 rim = vec3(0.0, 0.0, -1.0);  // the horizon mesh's edge thins into the far field: see past_rim

// The roads, as segments (RoadField.cs): the nearest one to a pixel paints it, from its exact distance.
uniform sampler2D road_segments : filter_nearest;
uniform sampler2D road_cells : filter_nearest;
uniform sampler2D road_index : filter_nearest;
uniform vec4 road_grid = vec4(16.0, 0.0, 0.0, 0.0); // cell size, cells across, cells down, segments
// How far the mesh may stand off a road's deck and still be painted as road: the voxels follow it
// closely, the coarse far grids less so. Below that (a pit dug in it, a cave under it) it isn't road.
uniform float road_tolerance = 1.2;

varying vec3 wpos;
varying vec3 wnormal;

// What the nearest road at a point is: how far its deck's edge is (negative on it), how far from its
// middle, how far along, its half width, wear, surface and style, and how much a place's pad covers it.
struct Road {
	float edge;     // metres past the deck's edge (negative on the deck)
	float across;   // metres from the centre line
	float along;    // metres along the road from its start
	float half_width;
	float wear;
	float surface;  // StampSurface: 1 concrete, 2 tarmac, 3 gravel, 4 dirt
	float style;    // 0 road, 1 highway, 2 track, 3 rail bed, 4 drive
	float pad;      // a place's pad over it: markings stop
	float core;     // on a place's footprint: the road stops
	float other;    // how far another road's deck edge is (junctions), m
};

Road nearest_road(vec2 at, float y) {
	Road r;
	r.edge = 1e9;
	r.other = 1e9;
	if (road_grid.w < 0.5) return r;
	vec2 c = mod(at, planet_size);
	ivec2 cell = ivec2(min(floor(c / road_grid.x), road_grid.yz - 1.0));
	vec2 list = texelFetch(road_cells, cell, 0).xy;
	int first = int(list.x), count = min(int(list.y), 48);
	float best_id = -1.0;
	for (int k = 0; k < count; k++) {
		int e = first + k;
		int s = int(texelFetch(road_index, ivec2(e % 4096, e / 4096), 0).r);
		ivec2 b = ivec2((s % 1024) * 4, s / 1024);
		vec4 t0 = texelFetch(road_segments, b, 0);
		vec2 rel = c - t0.xy;
		rel -= planet_size * round(rel / planet_size);
		float u = clamp(dot(rel, t0.zw) / max(dot(t0.zw, t0.zw), 1e-6), 0.0, 1.0);
		float d = length(rel - t0.zw * u);
		vec4 t2 = texelFetch(road_segments, b + ivec2(2, 0), 0);
		float edge = d - t2.x;
		if (edge > 4.0) continue;
		vec4 t1 = texelFetch(road_segments, b + ivec2(1, 0), 0);
		float off = abs(y - mix(t1.x, t1.y, u));
		// Banks fall away past the deck: the further out, the further off it the ground may lie.
		if (off > road_tolerance + max(edge, 0.0) * 1.2) continue;
		float id = floor(t2.w / 8.0);
		if (edge < r.edge) {
			if (id != best_id) r.other = min(r.other, r.edge);
			vec4 t3 = texelFetch(road_segments, b + ivec2(3, 0), 0);
			r.edge = edge;
			r.across = d;
			r.along = mix(t1.z, t1.w, u);
			r.half_width = t2.x;
			r.wear = t2.y;
			r.surface = t2.z;
			r.style = t2.w - id * 8.0;
			r.pad = mix(t3.x, t3.y, u);
			r.core = mix(t3.z, t3.w, u);
			best_id = id;
		} else if (id != best_id) {
			r.other = min(r.other, edge);
		}
	}
	return r;
}

// How much of a pixel fw metres wide a band |x| < w covers (a box filter): crisp up close, and thin
// lines fade to their share of the pixel far off instead of shimmering.
float band(float x, float w, float fw) {
	fw = max(fw, 1e-3);
	return clamp((min(x + fw * 0.5, w) - max(x - fw * 0.5, -w)) / fw, 0.0, 1.0);
}

float pn2(vec2 p, float freq) {
	return pnoise(vec3(p.x * freq, 0.0, p.y * freq), planet_size * freq);
}

// How far a point is from the nearest edge of an irregular block pattern `size` metres across: a
// jittered grid's cells, each the ground nearest its own point (the crazing of old tarmac and concrete).
vec2 cell_point(vec2 cell, vec2 period) {
	vec2 w = mod(cell, period);
	return vec2(hash3(vec3(w, 17.0)), hash3(vec3(w, 41.0))) * 0.8 + 0.1;
}

float crack_edge(vec2 p, float size) {
	vec2 q = p / size, i = floor(q), f = q - i, period = floor(planet_size / size);
	vec2 mr = vec2(0.0), mg = vec2(0.0);
	float md = 8.0;
	for (int y = -1; y <= 1; y++)
	for (int x = -1; x <= 1; x++) {
		vec2 g = vec2(float(x), float(y));
		vec2 rr = g + cell_point(i + g, period) - f;
		float d = dot(rr, rr);
		if (d < md) { md = d; mr = rr; mg = g; }
	}
	md = 8.0;
	for (int y = -1; y <= 1; y++)
	for (int x = -1; x <= 1; x++) {
		vec2 g = mg + vec2(float(x), float(y));
		vec2 rr = g + cell_point(i + g, period) - f;
		vec2 dd = rr - mr;
		if (dot(dd, dd) > 1e-5) md = min(md, dot(0.5 * (mr + rr), normalize(dd)));
	}
	return md * size;
}

// The road over the land: its albedo and how much of it shows (x: deck, y: shoulder), and roughness.
vec4 paint_road(Road r, vec2 p, vec3 land, float fw, float near, out float cover, out float rough) {
	float wear = r.wear;
	bool hard = r.surface < 2.5;          // concrete or tarmac
	bool gravel = r.surface > 2.5 && r.surface < 3.5;
	bool rail = r.style > 2.5 && r.style < 3.5;
	// The edge wanders smoothly (a laid edge barely, a crumbling or trodden one more), never in steps.
	float wander = (pn2(p, 0.25) - 0.5) * (hard ? 0.25 + 0.6 * wear : 0.9)
		+ (pn2(p, 1.0) - 0.5) * (hard ? 0.25 * wear : 0.45) * near;
	float edge = r.edge + wander;
	float soft = hard ? 0.05 + 0.2 * wear : 0.35 + (r.surface > 3.5 ? 0.5 : 0.0);
	float deck = 1.0 - smoothstep(-soft - fw * 0.5, soft + fw * 0.5, edge);
	// The shoulder: a strip of grit and dust past the deck, fading into the land.
	float shoulder_w = hard ? 1.6 : 1.0;
	float shoulder = (1.0 - smoothstep(0.0, shoulder_w, edge)) * (0.35 + 0.3 * pn2(p, 0.5)) * (1.0 - deck);
	float lum = dot(land, vec3(0.3, 0.59, 0.11));
	vec3 grit = mix(land, vec3(lum) * vec3(1.02, 0.98, 0.92) * 0.9 + 0.03, 0.55);

	vec3 c;
	float x = r.across;
	if (r.surface < 1.5) {
		// Concrete: slabs 5 m long, a joint down the middle (and between lanes), each slab its own shade.
		float lane = floor(x / max(r.half_width * 0.5, 1.8));
		float slab = floor(r.along / 5.0);
		float tone = hash3(vec3(slab, lane, r.style * 7.0)) - 0.5;
		c = mix(vec3(0.27, 0.262, 0.245), vec3(0.18, 0.172, 0.16), wear) * (1.0 + tone * (0.08 + 0.12 * wear)) * (0.9 + 0.2 * pn2(p, 0.15));
		float joints = max(band(fract(r.along / 5.0 + 0.5) - 0.5, 0.012, fw / 5.0), band(x, 0.06, fw));
		if (r.style > 0.5 && r.style < 1.5) joints = max(joints, band(x - r.half_width * 0.5, 0.06, fw));
		c *= 1.0 - 0.45 * joints * (1.0 - smoothstep(0.1, 0.6, fw));
	} else if (r.surface < 2.5) {
		// Tarmac: black when laid, greying with age, patched.
		c = mix(vec3(0.028, 0.028, 0.03), vec3(0.07, 0.066, 0.06), wear);
		c *= 0.85 + 0.3 * pn2(p, 0.12) + 0.12 * (pn2(p, 2.0) - 0.5) * near;
	} else if (gravel) {
		c = mix(vec3(0.22, 0.2, 0.17), land * 0.85, 0.3) * (0.85 + 0.3 * pn2(p, 3.0) * near + 0.15 * pn2(p, 0.2));
		if (rail) c *= 0.85;
	} else {
		// Packed dirt: the land's own ground, trodden darker and drier.
		c = mix(land * vec3(0.78, 0.72, 0.66), vec3(lum * 0.75) + vec3(0.04, 0.025, 0.01), 0.3) * (0.9 + 0.2 * pn2(p, 0.4));
	}
	// Wheel ruts on gravel and dirt: two per lane, and on a track grass down the middle.
	if (!hard && !rail) {
		float lane_c = r.half_width >= 3.0 ? r.half_width * 0.45 : 0.0;
		float rut = band(abs(x - lane_c) - 0.85, 0.22, fw) * (1.0 - smoothstep(0.2, 0.8, fw));
		c *= 1.0 - 0.18 * rut;
		if (lane_c == 0.0) c = mix(c, land, band(x, 0.35, fw) * (0.4 + 0.4 * wear));
	}
	// Cracks, with the land growing or blowing into them: grass on a green world, sand on a desert.
	// Blocks a metre or two across where the surface has crazed, and a crack across the road every few
	// metres; only up close, where they are more than a blur.
	if (hard && fw < 0.3) {
		float block = r.surface < 1.5 ? 2.6 : 1.4;
		// Crazed only in patches, and each crack broken along its length: a few hairlines, not a net.
		float crazed = smoothstep(0.6, 0.8, pn2(p, 0.12) + 0.15 * wear) * smoothstep(0.35, 0.55, pn2(p, 0.7));
		float crack = crazed > 0.0 ? band(crack_edge(p, block), 0.004 + 0.008 * wear, fw) * crazed : 0.0;
		float cut = fract(r.along / 6.1 + 0.08 * pn2(p, 1.0));
		crack = max(crack, band((cut - 0.5) * 6.1, 0.004 + 0.01 * wear, fw) * step(0.6, hash3(vec3(floor(r.along / 6.1), 3.0, r.style))) * smoothstep(0.3, 0.5, pn2(p, 0.5)));
		crack *= (0.35 + 0.65 * wear) * (1.0 - smoothstep(0.08, 0.3, fw));
		c = mix(c, mix(c * 0.35, land, 0.1 + 0.4 * wear), crack);
	}
	// Markings: painted lines on laid roads, worn away in flakes, and not where it meets another road or a place.
	if (hard && r.style < 1.5 && r.half_width >= 3.5) {
		float keep = (1.0 - smoothstep(0.2, 0.6, r.pad)) * smoothstep(0.5, 3.0, r.other) * (1.0 - 0.7 * wear);
		keep *= smoothstep(0.25, 0.7, pn2(p, 0.8) + 0.45 - 0.35 * wear);
		float paint = 0.0;
		float dash = band(fract(r.along / 9.0) * 9.0 - 1.5, 1.5, fw);
		if (r.style < 0.5) {
			paint = band(x, 0.07, fw) * dash;
			if (r.half_width >= 4.0) paint = max(paint, band(x - (r.half_width - 0.55), 0.06, fw));
		} else {
			paint = max(band(x - 0.18, 0.06, fw), band(x - (r.half_width - 0.6), 0.08, fw));
			paint = max(paint, band(x - r.half_width * 0.5, 0.07, fw) * dash);
		}
		vec3 ink = r.style > 0.5 && x < 0.4 ? vec3(0.6, 0.45, 0.08) : vec3(0.62, 0.62, 0.6);
		c = mix(c, ink, paint * keep);
	}
	// The weather takes it back in drifts: the land's own sand, soil or grass over it, most along its edges.
	float drift = pn2(p, 0.06) * 0.65 + pn2(p, 0.3) * 0.35 + smoothstep(-2.5, 0.0, edge) * 0.25;
	float over = smoothstep(0.6 - 0.1 * wear, 0.85 - 0.1 * wear, drift) * (0.15 + 0.3 * wear);
	c = mix(c, land, over);
	cover = deck * (1.0 - smoothstep(0.5, 1.0, r.core));
	rough = mix(r.surface < 2.5 ? 0.82 : 0.95, 0.93, over);
	return vec4(mix(grit, c, deck), max(deck, shoulder) * (1.0 - smoothstep(0.5, 1.0, r.core)));
}

// How far a point is inside the hole (negative outside).
float inside_hole(vec2 p) {
	return min(min(p.x - hole.x, hole.z - p.x), min(p.y - hole.y, hole.w - p.y));
}

void vertex() {
	if (hole_sink > 0.0 && inside_hole((MODEL_MATRIX * vec4(VERTEX, 1.0)).xz) >= hole_sink_from) VERTEX.y -= hole_sink;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnormal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	if (far_ring && inside_hole(wpos.xz) > hole_cut) discard;
	if (far_ring && past_rim(wpos.xz, rim, FRAGCOORD.xy)) discard;

	vec3 base = srgb_to_linear(COLOR.rgb);
	float up = wnormal.y;
	float faces_up = up;
	// A coarse grid across a cliff makes long thin triangles whose corners have nothing to do with
	// the wall between them: on a spur or in a gully the normal says flat, or it turns away from the
	// camera, and at that grazing angle it catches the sky as a pale streak, like a tear in the rock.
	// So on a steep far triangle a normal that says flat takes the triangle's true facing, and the
	// sheen goes where the normal turns from the eye. A wall keeps the normals that light it smoothly.
	// (Not curtains, which are upright and painted as they should be, nor the wall a far ring dives
	// under the voxels by: those stay the ground whose small step they close.)
	float sheen = 1.0;
	if (far_ring && inside_hole(wpos.xz) <= 0.0) {
		vec3 facing = normalize(cross(dFdx(wpos), dFdy(wpos)));
		vec3 to_eye = normalize(CAMERA_POSITION_WORLD - wpos);
		if (dot(facing, to_eye) < 0.0) facing = -facing;
		if (facing.y >= 0.01 && facing.y < 0.5) {
			vec3 n = normalize(wnormal);
			float says_flat = smoothstep(0.6, 0.85, n.y) * (1.0 - smoothstep(0.2, 0.4, facing.y));
			if (says_flat > 0.0) n = normalize(mix(n, facing, says_flat));
			float seen = dot(n, to_eye);
			if (seen < 0.05) n = normalize(n + to_eye * (0.05 - seen));
			if (says_flat > 0.0 || seen < 0.05) NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
			sheen = smoothstep(0.0, 0.4, seen);
			up = n.y;
			faces_up = min(up, facing.y);
		}
	}
	// The vertex alpha already says how bare the ground is from its slope (soil thins out from
	// about 30 degrees). The pixel's own normal only adds rock on real cliffs, over a wide band:
	// voxel normals wobble at the metre scale, and a narrow band turned slopes into blotches.
	float bare = max(COLOR.a, 1.0 - smoothstep(0.3, 0.62, faces_up));

	float fine = pfbm(wpos, 0.5, planet_size);
	float broad = pfbm(wpos, 0.0625, planet_size);
	// Close up, grit and grain: a finer layer that fades out with distance (it would only shimmer).
	float near = 1.0 - smoothstep(20.0, 60.0, length(wpos - CAMERA_POSITION_WORLD));
	float grain = near > 0.0 ? pnoise(wpos * 4.0, planet_size * 4.0) - 0.5 : 0.0;

	// Soil: patchy at two scales, so a meadow or a dune field never reads as flat paint.
	vec3 soil = base * (0.8 + 0.32 * broad) * (0.9 + 0.2 * fine) * (1.0 + grain * 0.28 * near);

	// Rock: strata that wander a little, plus chips and streaks.
	float band = sin(wpos.y * 1.7 + broad * 9.0) * 0.5 + 0.5;
	float band2 = sin(wpos.y * 0.43 + fine * 3.0 + broad * 4.0) * 0.5 + 0.5;
	vec3 rock = (COLOR.a > 0.5 ? base : base * 0.95);
	rock *= mix(1.0, 0.72 + 0.4 * band * band2, strata) * (0.82 + 0.3 * fine) * (1.0 + grain * 0.35 * near);
	vec3 albedo = mix(soil, rock, bare);

	// Bedrock: a faint grid, so players learn what can't be cut.
	if (UV.y > 0.5) {
		vec3 g = abs(fract(wpos) - 0.5);
		float edge = smoothstep(0.46, 0.5, max(max(g.x, g.y), g.z));
		albedo = mix(albedo, albedo * 2.2 + vec3(0.03, 0.03, 0.06), edge * 0.6);
	}

	// Roads, painted from their exact lines (on level ground facing up, not a dug wall).
	float road_cover = 0.0;
	float road_rough = 0.93;
	if (road_grid.w > 0.5 && up > 0.45) {
		Road r = nearest_road(wpos.xz, wpos.y);
		if (r.edge < 4.0) {
			float fw = length(fwidth(wpos.xz)) * 0.7;
			vec4 road = paint_road(r, wpos.xz, soil, fw, near, road_cover, road_rough);
			float flat_ = smoothstep(0.45, 0.7, up);
			albedo = mix(albedo, road.rgb, road.a * flat_);
			road_cover *= flat_;
		}
	}

	// Snow settles on ground that faces up: the weather's fresh snow, plus high or polar ground.
	float settled = clamp(weather_ground.y + base_snow + smoothstep(snow_altitude, snow_altitude + 120.0, wpos.y + broad * 60.0), 0.0, 1.0);
	float snow = smoothstep(0.62, 0.8, up + fine * 0.15) * smoothstep(0.05, 0.55, settled + fine * 0.2 - 0.1);
	// A road's open line keeps snow thin and patchy, so it reads under a white world.
	snow *= 1.0 - road_cover * (0.9 - 0.4 * smoothstep(0.4, 0.8, fine));
	albedo = mix(albedo, vec3(0.82, 0.85, 0.88), snow);

	// Dust drifts over everything a dust storm crosses.
	albedo = mix(albedo, srgb_to_linear(dust_color), weather_ground.z * 0.5 * smoothstep(0.3, 0.9, up));

	// Rain darkens soil and puts a sheen on rock.
	float wet = weather_ground.x * (1.0 - snow);
	albedo *= 1.0 - wet * 0.35;
	ALBEDO = albedo;
	ROUGHNESS = mix(mix(0.93, road_rough, road_cover), 0.45, wet * (0.4 + 0.6 * max(bare, road_cover)));
	SPECULAR = (0.3 + wet * 0.3) * sheen;

	if (UV.x > 0.01) {
		// Glowing ground: lava cracks run in veins, glow moss and ore shine softly.
		float vein = 1.0 - smoothstep(0.0, 0.05, abs(pnoise(wpos * 0.25, planet_size * 0.25) - 0.5));
		// Soft glows only show once the light falls: at noon the moss is just moss.
		EMISSION = UV.x > 0.6 ? glow_color * glow_strength * (vein + 0.05) : base * UV.x * 1.5 * (1.0 - weather_ground.w * 0.7);
	}
}
