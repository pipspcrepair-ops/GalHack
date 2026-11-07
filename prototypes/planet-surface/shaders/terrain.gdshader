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
	r.along = 0.0;
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
		float along = mix(t1.z, t1.w, u);
		// Another road, or the same one coming back past itself (a hairpin, a loop): its own markings
		// give way there, so where two stretches overlap they make one surface with one set of lines.
		bool apart = id != best_id || abs(along - r.along) > 2.0 * t2.x + 6.0;
		if (edge < r.edge) {
			if (apart) r.other = min(r.other, r.edge);
			vec4 t3 = texelFetch(road_segments, b + ivec2(3, 0), 0);
			r.edge = edge;
			r.across = d;
			r.along = along;
			r.half_width = t2.x;
			r.wear = t2.y;
			r.surface = t2.z;
			r.style = t2.w - id * 8.0;
			r.pad = mix(t3.x, t3.y, u);
			r.core = mix(t3.z, t3.w, u);
			best_id = id;
		} else if (apart) {
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

// Smooth noise along a road (s in metres times a frequency), one stream per salt.
float along_noise(float s, float salt) {
	return pnoise(vec3(s, salt, 0.5), vec2(1048576.0));
}

// How far a point is from a contour of noise, in metres: an irregular line that wanders and closes
// on itself, never a grid (the network of a small patch of alligator cracking).
float contour(vec2 p, float freq) {
	float n = pn2(p, freq) - 0.5;
	vec2 g = vec2(pn2(p + vec2(0.05, 0.0), freq) - 0.5 - n, pn2(p + vec2(0.0, 0.05), freq) - 0.5 - n) / 0.05;
	return abs(n) / max(length(g), 0.05);
}

// The road over the land: its albedo and how much of it shows (x: deck, y: shoulder), and roughness.
vec4 paint_road(Road r, vec2 p, vec3 land, float fw, float near, out float cover, out float rough) {
	float wear = r.wear;
	bool hard = r.surface < 2.5;          // concrete or tarmac
	bool gravel = r.surface > 2.5 && r.surface < 3.5;
	bool rail = r.style > 2.5 && r.style < 3.5;
	// The edge wanders smoothly (a laid edge barely, a crumbling or trodden one more), never in steps.
	float wander = (pn2(p, 0.25) - 0.5) * (hard ? 0.25 + 0.6 * wear : 0.9)
		+ (pn2(p, 1.0) - 0.5) * (hard ? 0.3 * wear : 0.45) * near
		+ (pn2(p, 3.7) - 0.5) * (hard ? 0.18 * wear : 0.15) * near;
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
	if (hard && fw < 0.6) {
		// Wear: a road's age shows in a few long cracks, repairs and holes, more of them the older it is,
		// fewer on concrete. Each salt of noise is the road's own, so no two stretches crack alike.
		float detail = 1.0 - smoothstep(0.1, 0.35, fw);   // hairlines only up close
		float many = wear * (r.surface < 1.5 ? 0.6 : 1.0);
		float hw = r.half_width;
		float crack = 0.0;
		float thin = 0.004 + 0.006 * wear;
		// Long cracks running with the road, wandering across it, broken here and there.
		for (int k = 0; k < 2; k++) {
			float salt = float(k) * 31.0 + r.style * 7.0;
			float off = hw * (0.12 + 0.7 * along_noise(r.along * 0.013, salt)) + (pn2(p, 0.45) - 0.5) * 0.7 + (pn2(p, 2.3) - 0.5) * 0.08;
			float there = smoothstep(0.56 - 0.3 * many, 0.62 - 0.3 * many, along_noise(r.along * 0.021, salt + 11.0)) * smoothstep(0.32, 0.45, pn2(p, 0.55 + 0.2 * float(k)));
			crack = max(crack, band(x - off, thin, fw) * there);
		}
		// Cracks across it, every so often, reaching part way over.
		float span = 11.0;
		float cell = floor(r.along / span);
		float h = hash3(vec3(cell, 5.0, r.style + r.half_width));
		float at = (cell + 0.2 + 0.6 * fract(h * 13.7)) * span + (pn2(p, 0.4) - 0.5) * 1.6 + (pn2(p, 2.1) - 0.5) * 0.1;
		float reach = hw * (0.35 + 0.65 * fract(h * 7.3));
		crack = max(crack, band(r.along - at, thin, fw) * step(h, 0.15 + 0.55 * many) * (1.0 - smoothstep(reach - 0.4, reach, x)));
		// Alligator cracking, only in small patches that have broken up.
		float broken = smoothstep(0.7, 0.76, pn2(p, 0.08) + 0.12 * many);
		if (broken > 0.0) {
			float net = min(contour(p, 1.1), contour(p + vec2(17.3, 5.1), 2.3));
			crack = max(crack, band(net, thin * 0.8, fw) * broken);
			c *= 1.0 - 0.08 * broken;
		}
		crack *= detail * (0.4 + 0.6 * wear);
		// Dark hairlines with the land blown into them: sand on a desert, soil and grass on a green world.
		c = mix(c, mix(c * 0.25, land * 0.6, 0.1 + 0.25 * wear), crack);
		// Repairs: a strip of sealant along an old crack, and patches laid square across a lane.
		float seal_off = hw * (0.2 + 0.6 * along_noise(r.along * 0.009, 71.0 + r.style)) + (pn2(p, 0.3) - 0.5) * 0.4;
		float sealed = band(x - seal_off, 0.07, fw) * smoothstep(0.66, 0.7, along_noise(r.along * 0.017, 83.0 + r.style)) * step(1.5, r.surface);
		float pcell = floor(r.along / 17.0);
		float ph = hash3(vec3(pcell, 9.0, r.half_width));
		float p0 = (pcell + 0.15 + 0.4 * fract(ph * 5.1)) * 17.0, p1 = p0 + 2.5 + 5.0 * fract(ph * 9.7);
		float lane_lo = fract(ph * 3.3) < 0.5 ? 0.0 : hw * 0.5, lane_hi = lane_lo + hw * 0.5;
		float patch = band(r.along - (p0 + p1) * 0.5, (p1 - p0) * 0.5, fw) * band(x - (lane_lo + lane_hi) * 0.5, (lane_hi - lane_lo) * 0.5, fw) * step(ph, 0.2 * many);
		c = mix(c, c * 0.62, max(sealed * 0.8, patch * 0.7) * (1.0 - smoothstep(0.3, 0.8, fw)));
		// Potholes, now and then on an old road: ragged, dark, with sand or water lying in them.
		vec2 hole_cell = floor(p / 5.0);
		vec3 hh = vec3(hash3(vec3(mod(hole_cell, floor(planet_size / 5.0)), 23.0)), hash3(vec3(mod(hole_cell, floor(planet_size / 5.0)), 29.0)), hash3(vec3(mod(hole_cell, floor(planet_size / 5.0)), 37.0)));
		if (hh.x < 0.07 * many * many && x < hw - 0.6) {
			vec2 centre = (hole_cell + 0.3 + 0.4 * hh.yz) * 5.0;
			float radius = 0.2 + 0.3 * fract(hh.x * 91.0);
			float dist = length(p - centre) + (pn2(p, 3.1) - 0.5) * 0.35;
			float pit = 1.0 - smoothstep(radius - fw * 0.5, radius + 0.04 + fw * 0.5, dist);
			c = mix(c, mix(c * 0.3, land * 0.45, 0.3), pit * (1.0 - smoothstep(0.3, 0.8, fw)));
		}
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
	// Drifts lie in clean tongues with a wind-ruffled edge, not a haze over the road.
	drift += (pn2(p, 1.3) - 0.5) * 0.06 * near;
	float over = smoothstep(0.7 - 0.12 * wear, 0.74 - 0.12 * wear + fw * 0.02, drift) * (0.2 + 0.45 * wear);
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
	if (road_grid.w > 0.5 && up > (far_ring ? 0.0 : 0.45)) {
		Road r = nearest_road(wpos.xz, wpos.y);
		if (r.edge < 4.0) {
			float fw = length(fwidth(wpos.xz)) * 0.7;
			vec4 road = paint_road(r, wpos.xz, soil, fw, near, road_cover, road_rough);
			// Up close only level ground takes it (not the wall of a pit dug in it). A far grid is too
			// coarse to hold a deck's level through a tight bend, so there the deck is painted whole
			// and only its shoulders keep off steep ground: one clean surface, not a smear.
			float flat_ = smoothstep(0.45, 0.7, up);
			float keep = far_ring ? max(road_cover, flat_) : flat_;
			albedo = mix(albedo, road.rgb, road.a * keep);
			road_cover *= keep;
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
