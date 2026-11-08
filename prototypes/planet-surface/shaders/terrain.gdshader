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
// The world's prevailing wind on the ground, along a map axis: it lays sand and soil over roads from one side.
uniform vec2 road_wind = vec2(1.0, 0.0);
// The world's road paint: x yellow centre lines (else white), y a dashed centre line on roads (else solid),
// z paint at all (a low-tech world painted none).
uniform vec4 road_paint = vec4(0.0, 1.0, 1.0, 0.0);
// The world's own ground bared by wheels and feet (GroundPalette.BareSoil): its tracks and dirt roads.
uniform vec3 bare_soil : source_color = vec3(0.45, 0.36, 0.27);

varying vec3 wpos;
varying vec3 wnormal;

// What the nearest road at a point is: how far its deck's edge is (negative on it), how far from its
// middle, how far along, its half width, wear, surface and style, and how much a place's pad covers it.
struct Road {
	float edge;     // metres past the deck's edge (negative on the deck)
	float across;   // metres from the centre line
	float side;     // the same, signed: positive to the left of the way the road was laid
	vec2 dir;       // the way the road runs here (unit)
	float along;    // metres along the road from its start
	float half_width;
	float wear;
	float surface;  // StampSurface: 1 concrete, 2 tarmac, 3 gravel, 4 dirt
	float style;    // 0 road, 1 highway, 2 track, 3 rail bed, 4 drive, 5 graded dirt road
	float pad;      // a place's pad over it: markings stop
	float core;     // on a place's footprint: the road stops
	float core_end; // whether the stretch it's on runs onto one (a drive into a yard)
	float other;    // how far another road's deck edge is (junctions), m
	float other_hw; // that road's half width
	float other_id; // and which road it is
	vec2 other_dir; // and the way it runs
	float other_core; // and how far it has run onto a place's footprint (where it isn't drawn)
	float id;       // which road this is
	float town;     // 1 on a town's street
	float off;      // how far the ground here stands off its deck, m
	float lot;      // 1 near an end that runs into a place's lot (its mouth on the street is a crossover)
	float other_lot; // and the same for the other road
	float grade;    // how steeply its deck climbs here (rise over run, signed along the way it was laid)
};

Road nearest_road(vec2 at, float y) {
	Road r;
	r.edge = 1e9;
	r.other = 1e9;
	r.other_hw = 0.0;
	r.other_id = -1.0;
	r.other_dir = vec2(1.0, 0.0);
	r.other_core = 0.0;
	r.id = -1.0;
	r.off = 0.0;
	r.lot = 0.0;
	r.other_lot = 0.0;
	r.grade = 0.0;
	if (road_grid.w < 0.5) return r;
	vec2 c = mod(at, planet_size);
	ivec2 cell = ivec2(min(floor(c / road_grid.x), road_grid.yz - 1.0));
	vec2 list = texelFetch(road_cells, cell, 0).xy;
	int first = int(list.x), count = min(int(list.y), 64);
	float best_id = -1.0;
	bool best_grounded = false;
	r.along = 0.0;
	for (int k = 0; k < count; k++) {
		int e = first + k;
		int s = int(texelFetch(road_index, ivec2(e % 4096, e / 4096), 0).r);
		ivec2 b = ivec2((s % 512) * 8, s / 512);
		vec4 t0 = texelFetch(road_segments, b, 0);
		vec2 rel = c - t0.xy;
		rel -= planet_size * round(rel / planet_size);
		float u = clamp(dot(rel, t0.zw) / max(dot(t0.zw, t0.zw), 1e-6), 0.0, 1.0);
		float d = length(rel - t0.zw * u);
		vec4 t2 = texelFetch(road_segments, b + ivec2(2, 0), 0);
		float edge = d - t2.x;
		if (edge > 8.0) continue;
		vec4 t1 = texelFetch(road_segments, b + ivec2(1, 0), 0);
		float off = abs(y - mix(t1.x, t1.y, u));
		// Banks fall away past the deck: the further out, the further off it the ground may lie. Where a
		// place's pad lies over the road (a drive up to its yard), the ground is the pad's, not the deck's:
		// held to the deck there, a drive showed only in slivers where the two heights crossed.
		// (Anywhere across its width: where a yard's apron reaches over a road's edge, the ground there is the yard's.)
		vec4 t4 = texelFetch(road_segments, b + ivec2(4, 0), 0);
		float strict = road_tolerance + 6.0 * mix(t4.x, t4.y, u);
		if (off > strict + max(edge, 0.0) * 1.2) continue;
		// Only a road whose deck is the ground here makes a junction: one standing off it (a drive whose
		// deck isn't the land's) would round a kerb toward a road that isn't drawn, a pale wedge.
		bool grounded = off <= strict;
		float id = floor(t2.w / 16.0);
		float along = mix(t1.z, t1.w, u);
		// Another road, or the same one coming back past itself (a hairpin, a loop): its own markings
		// give way there, so where two stretches overlap they make one surface with one set of lines.
		bool apart = id != best_id || abs(along - r.along) > 2.0 * t2.x + 6.0;
		if (edge < r.edge) {
			// (A stretch on a place's footprint isn't drawn, so it makes no junction: no kerbs, no crossing.)
			if (apart && r.edge < r.other && r.core_end < 0.5 && best_grounded) { r.other = r.edge; r.other_hw = r.half_width; r.other_id = best_id; r.other_dir = r.dir; r.other_core = r.core; r.other_lot = r.lot; }
			vec4 t3 = texelFetch(road_segments, b + ivec2(3, 0), 0);
			r.edge = edge;
			r.across = d;
			r.off = off;
			vec2 to = rel - t0.zw * u;
			float len = max(length(t0.zw), 1e-4);
			r.grade = (t1.y - t1.x) / max(len, 0.5);
			r.dir = t0.zw / len;
			r.side = (t0.z * to.y - t0.w * to.x) / len;
			r.along = along;
			r.half_width = t2.x;
			r.wear = t2.y;
			r.surface = t2.z;
			r.style = t2.w - id * 16.0;
			r.town = r.style > 7.5 ? 1.0 : 0.0;
			r.style -= 8.0 * r.town;
			r.pad = mix(t3.x, t3.y, u);
			r.core = mix(t3.z, t3.w, u);
			r.core_end = max(t3.z, t3.w);
			r.lot = mix(t4.z, t4.w, u);
			best_id = id;
			r.id = id;
			best_grounded = grounded;
		} else if (apart && edge < r.other && grounded) {
			vec4 t3o = texelFetch(road_segments, b + ivec2(3, 0), 0);
			float oc = max(t3o.z, t3o.w);
			if (oc < 0.5) {
				r.other_core = oc;
				r.other_lot = mix(t4.z, t4.w, u);
				r.other = edge;
				r.other_hw = t2.x;
				r.other_id = id;
				r.other_dir = t0.zw / max(length(t0.zw), 1e-4);
			}
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

// Distance to the nearest crack of crazed surface in cells about `size` metres across: each cell is
// the ground nearest its own jittered point. Only ever used in small broken patches (alligator cracking).
vec2 cell_point(vec2 cell, vec2 period) {
	vec2 w = mod(cell, period);
	return vec2(hash3(vec3(w, 17.0)), hash3(vec3(w, 41.0))) * 0.8 + 0.1;
}

float crazing(vec2 p, float size) {
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

// How green (grown over) a ground colour is: 0 sand, ash, snow or rock, 1 grass.
float greenness(vec3 land) {
	return clamp((land.g - max(land.r, land.b)) / max(land.g, 1e-3) * 4.0, 0.0, 1.0);
}

// The land's own ground bared by wheels and feet: where it is grown over, its earth (the world's rock
// weathered into clay, grit or sand); where it is sand, ash or snow already, that packed down. Never a
// surface of its own: always the palette of the ground round it.
vec3 bared(vec3 land) {
	return mix(land * vec3(0.9, 0.88, 0.86), bare_soil, greenness(land));
}

// A way of the land's own ground, no surface laid: a track's two wheel ruts with a crown between where
// grass and tussocks stand, loose soil heaped beside each rut; or a graded road's wide scraped bed with only
// faint wheel tracks. Up close: tyre treads, footprints and hoof prints pressed into soft ground, twigs
// blown across. Wet, the ruts hold long narrow pools of milky water and the ground between is churned into
// ragged ridges of mud; dry again, the rut bottoms where water stood crack into clay plates. On a slope,
// washouts down its length and corrugations across it.
vec3 soil_way(Road r, vec2 p, vec3 land, float fw, float near, inout float gloss) {
	float veg = greenness(land);
	vec3 bare = bared(land);
	float x = r.across, s = r.side, hw = r.half_width;
	bool graded = r.style > 4.5 && r.style < 5.5;
	bool track = !graded && hw < 3.5;
	float wetness = weather_ground.x;
	vec3 c = bare * (0.88 + 0.24 * pn2(p, 0.35) + 0.12 * (pn2(p, 2.2) - 0.5) * near);
	// The wheel paths: a pair 1.6 m apart down a track's middle, a pair in each lane of a wider way,
	// wandering a hand's width.
	float lane_c = track ? 0.0 : hw * 0.48;
	float gauge = 0.8, rut_w = track ? 0.26 : 0.32;
	float wob = (pn2(p, 0.9) - 0.5) * 0.12;
	float dr = abs(abs(x - lane_c) - gauge + wob);
	float rut = 1.0 - smoothstep(rut_w - 0.06 - fw * 0.5, rut_w + 0.06 + fw * 0.5, dr);
	float worn = track ? 1.0 : graded ? 0.35 : 0.6;
	c *= 1.0 - 0.17 * rut * worn;
	// Loose soil thrown up beside each rut: lighter, lumpy.
	float heap = band(dr - rut_w - 0.17, 0.13, fw) * (0.5 + 0.7 * pn2(p, 3.3));
	c = mix(c, bare * 1.13, heap * 0.5 * worn);
	// A track's crown: grass and tussocks where anything grows, a ridge of loose sand where nothing does.
	float crown = 0.0;
	if (track) {
		crown = 1.0 - smoothstep(gauge - rut_w - 0.14, gauge - rut_w + 0.03, x + wob);
		float tuft = smoothstep(0.3, 0.7, pn2(p, 1.7) * 0.7 + pn2(p, 5.3) * 0.3 * near + 0.1);
		c = mix(c, land * (0.8 + 0.35 * tuft), crown * veg * (0.5 + 0.5 * tuft));
		c = mix(c, bare * 1.08, crown * (1.0 - veg) * 0.45);
	}
	// On a slope rain runs down it: gullies wander along its length, deeper on an old way, and the ground
	// between ripples into corrugations across it (and on any graded road, where the traffic hammered them in).
	float slope = abs(r.grade);
	float detail = 1.0 - smoothstep(0.08, 0.3, fw);
	float corr = (graded ? 0.45 : 0.0) + smoothstep(0.05, 0.12, slope);
	c *= 1.0 + 0.07 * min(corr, 1.0) * sin(r.along * 8.4 + pn2(p, 0.5) * 2.0) * detail;
	float wash = smoothstep(0.07, 0.15, slope) * (0.4 + 0.6 * r.wear) * smoothstep(0.3, 0.55, along_noise(r.along * 0.02, 67.0 + r.id));
	if (wash > 0.0) {
		float gx = s - (along_noise(r.along * 0.045, 61.0 + r.id) - 0.5) * hw * 1.3;
		float gw = 0.1 + 0.14 * pn2(p, 1.9);
		float gully = 1.0 - smoothstep(gw - 0.04 - fw * 0.5, gw + 0.04 + fw * 0.5, abs(gx));
		float lip = band(abs(gx) - gw - 0.07, 0.05, fw);
		c = mix(c, bare * 0.55 * (0.8 + 0.4 * pn2(p, 9.0)), gully * wash);
		c = mix(c, bare * 1.15, lip * wash * 0.6);
	}
	// Twigs and stems blown across where anything grows: a few short dark sticks, any angle.
	if (veg > 0.2 && fw < 0.08) {
		float tc = floor(r.along / 1.3);
		float th = hash3(vec3(tc, 13.0, r.id));
		if (th < 0.4 * veg) {
			vec2 rel = vec2(r.along - (tc + 0.5 + (fract(th * 7.1) - 0.5) * 0.6) * 1.3, s - (fract(th * 3.7) - 0.5) * 2.0 * (hw - 0.3));
			float ang = fract(th * 11.3) * 3.14159;
			vec2 dv = vec2(cos(ang), sin(ang));
			float len = 0.15 + 0.45 * fract(th * 5.9);
			float u = clamp(dot(rel, dv), -len, len);
			float twig = band(length(rel - dv * u), 0.01 + 0.008 * (1.0 - abs(u) / len), fw);
			c = mix(c, vec3(0.07, 0.05, 0.035) + bare * 0.15, twig * 0.85 * (1.0 - smoothstep(0.03, 0.08, fw)));
		}
	}
	// Prints in soft ground, only up close: tyre treads in the ruts, a walker's footprints, hooves.
	float soft = max(1.0 - veg, smoothstep(0.3, 0.6, wetness));
	if (fw < 0.04 && soft > 0.1) {
		float close = (1.0 - smoothstep(0.012, 0.04, fw)) * soft;
		float tread = band(fract(r.along / 0.2 + abs(dr) * 1.6) - 0.5, 0.2, fw);
		c *= 1.0 - 0.1 * tread * rut * close;
		float walker = (along_noise(r.along * 0.03, 71.0 + r.id) - 0.5) * hw * 1.2;
		if (along_noise(r.along * 0.011, 73.0 + r.id) > 0.55) {
			float k = floor(r.along / 0.36);
			vec2 q = vec2((r.along - (k + 0.5) * 0.36) / 0.13, (s - walker - (mod(k, 2.0) < 0.5 ? 0.1 : -0.1)) / 0.05);
			float d = length(q);
			c = mix(c, c * 0.72, (1.0 - smoothstep(0.8, 1.0, d)) * close);
			c = mix(c, c * 1.12, band(d - 1.12, 0.1, 0.05) * close);
		}
		float herd = (along_noise(r.along * 0.02, 79.0 + r.id) - 0.5) * hw * 0.9;
		if (along_noise(r.along * 0.008, 83.0 + r.id) > 0.62) {
			float k = floor(r.along / 0.5);
			vec2 q = vec2(r.along - (k + 0.5) * 0.5, s - herd - (mod(k, 2.0) < 0.5 ? 0.13 : -0.13));
			float d = length(q);
			float crescent = band(d - 0.055, 0.016, fw) * step(-0.02, q.x);
			c = mix(c, c * 0.65, crescent * close);
		}
	}
	// Wet: the ruts' low stretches hold long narrow pools of milky water, the ground between churned to mud.
	float mud = smoothstep(0.15, 0.5, wetness);
	float low = along_noise(r.along * 0.09, 89.0 + r.id) * 0.7 + pn2(p, 0.6) * 0.3;
	float pool = rut * smoothstep(0.72 - 0.3 * mud, 0.78 - 0.3 * mud, low) * smoothstep(0.25, 0.55, wetness) * (1.0 - smoothstep(rut_w * 0.5, rut_w, dr));
	float churn = smoothstep(0.5, 0.75, along_noise(r.along * 2.2, x * 6.0 + 3.0));
	vec3 muck = bare * (0.48 + 0.3 * churn);
	c = mix(c, muck, mud * (track ? 0.85 : 0.6) * (1.0 - crown * veg * 0.7));
	c = mix(c, mix(bare, vec3(0.52, 0.45, 0.36), 0.5) * 0.78, pool);
	gloss = max(gloss, max(pool, mud * 0.45 * (1.0 - crown * veg)));
	// Dry again: where water stood in the ruts, the mud has cracked into clay plates.
	float dry = 1.0 - smoothstep(0.08, 0.2, wetness);
	if (dry > 0.0 && fw < 0.12) {
		float plates = band(crazing(p, 0.17), 0.008, fw);
		c = mix(c, c * 0.55, plates * rut * dry * smoothstep(0.6, 0.75, low) * (1.0 - smoothstep(0.04, 0.12, fw)));
	}
	return c;
}

// The road over the land: its albedo and how much of it shows (x: deck, y: shoulder), and roughness.
vec4 paint_road(Road r, vec2 p, vec3 land, float fw, float near, out float cover, out float rough, out float joint) {
	float wear = r.wear;
	bool hard = r.surface < 2.5;          // concrete or tarmac
	bool gravel = r.surface > 2.5 && r.surface < 3.5;
	bool rail = r.style > 2.5 && r.style < 3.5;
	// The edge wanders smoothly (a laid edge barely, a crumbling or trodden one more), never in steps.
	float wander = (pn2(p, 0.25) - 0.5) * (hard ? 0.25 + 0.6 * wear : 0.9)
		+ (pn2(p, 1.0) - 0.5) * (hard ? 0.3 * wear : 0.45) * near
		+ (pn2(p, 3.7) - 0.5) * (hard ? 0.18 * wear : 0.15) * near;
	// Where two roads meet, their decks join in one surface with curved kerb corners, a tighter curve for
	// smaller roads (a smooth union of the two decks).
	// Sized for a vehicle to turn from its near lane into the near lane of the other road: about 7 m on town
	// streets, 12 on main roads, 15 where highways meet (the smooth union's width is 1.7 times the radius).
	float turn_r = r.town > 0.5 ? 7.0 : max(r.half_width, r.other_hw) > 5.0 ? 15.0 : 12.0;
	float crossing = 1.0 - abs(dot(r.dir, r.other_dir));   // 0 where the two run the same way, 1 square
	// (Not with a drive running onto a place's footprint: its deck gives way to the pad, and so do its corners.)
	// Neither where this stretch is a drive running onto a place's footprint (its corners would spread a
	// pale wedge over the yard's apron), and a small one where either road is a place's drive.
	float onto = max(smoothstep(0.0, 0.4, r.other_core), step(0.5, r.core_end));
	// A lane into a place's lot leaves the street by a crossover, its kerb dropped and barely rounded: flared
	// like a street corner, its short stub up to the lot line read as a tooth cut in the street's edge.
	float drive_r = abs(r.style - 4.0) < 0.5 || max(r.lot, r.other_lot) > 0.5 ? 1.8 : turn_r;
	float kerb = r.other_hw > 0.0 ? max(drive_r * 1.7 * smoothstep(0.1, 0.4, crossing) * (1.0 - onto), 0.5) : 3.0;
	float h_k = max(kerb - abs(r.edge - r.other), 0.0) / kerb;
	float joined = min(r.edge, r.other) - h_k * h_k * kerb * 0.25;
	float edge = joined + wander;
	float soft = hard ? 0.05 + 0.2 * wear : 0.35 + (r.surface > 3.5 ? 0.5 : 0.0);
	float deck = 1.0 - smoothstep(-soft - fw * 0.5, soft + fw * 0.5, edge);
	// How much of that is only the junction's kerb corner, not the road's own deck.
	joint = max(deck - (1.0 - smoothstep(-soft - fw * 0.5, soft + fw * 0.5, r.edge + wander)), 0.0) * (1.0 - smoothstep(0.5, 1.0, r.core));
	// The shoulder: a strip of grit and dust past the deck, fading into the land. Beside a way of bare ground,
	// its loose soil; beside a graded road, the berm the grader left along each side, lit on its outer face
	// and shadowed on its inner, so it reads from the air.
	float shoulder_w = hard ? 1.6 : 1.0;
	float shoulder = (1.0 - smoothstep(0.0, shoulder_w, edge)) * (0.35 + 0.3 * pn2(p, 0.5)) * (1.0 - deck);
	float lum = dot(land, vec3(0.3, 0.59, 0.11));
	vec3 grit = mix(land, vec3(lum) * vec3(1.02, 0.98, 0.92) * 0.9 + 0.03, 0.55);
	bool soil = !hard && !rail;
	if (soil) {
		grit = bared(land) * (1.02 + 0.12 * pn2(p, 2.6));
		if (r.style > 4.5 && r.style < 5.5) {
			float berm = 1.0 - smoothstep(1.2, 1.6 + fw, abs(edge - 0.8));
			float inner = 1.0 - smoothstep(0.0, 0.5, abs(edge - 0.3));
			grit *= 1.0 + 0.12 * berm - 0.25 * inner;
			shoulder = max(shoulder, berm * 0.9 * (1.0 - deck));
		}
	}

	vec3 c;
	float x = r.across;
	float pit_wet = 0.0;
	if (r.surface < 1.5) {
		// Concrete: slabs 5 m long, a joint down the middle (and between lanes), each slab its own shade.
		float lane = floor(x / max(r.half_width * 0.5, 1.8));
		float slab = floor(r.along / 5.0);
		float tone = hash3(vec3(slab, lane, r.style * 7.0)) - 0.5;
		c = mix(vec3(0.27, 0.262, 0.245), vec3(0.18, 0.172, 0.16), wear) * (1.0 + tone * (0.08 + 0.12 * wear)) * (0.9 + 0.2 * pn2(p, 0.15));
		// Joints: hairline saw cuts, sealed, seen only up close.
		float joints = max(band((fract(r.along / 5.0 + 0.5) - 0.5) * 5.0, 0.006, fw), band(x, 0.006, fw));
		if (r.style > 0.5 && r.style < 1.5) joints = max(joints, band(x - r.half_width * 0.5, 0.006, fw));
		c *= 1.0 - 0.3 * joints * (1.0 - smoothstep(0.03, 0.15, fw));
	} else if (r.surface < 2.5) {
		// Tarmac: black when laid, greying with age, patched.
		c = mix(vec3(0.028, 0.028, 0.03), vec3(0.07, 0.066, 0.06), wear);
		c *= 0.85 + 0.3 * pn2(p, 0.12) + 0.12 * (pn2(p, 2.0) - 0.5) * near;
	} else if (rail) {
		// Ballast: crushed stone under the sleepers.
		c = mix(vec3(0.22, 0.2, 0.17), land * 0.85, 0.3) * (0.85 + 0.3 * pn2(p, 3.0) * near + 0.15 * pn2(p, 0.2)) * 0.85;
	} else {
		c = soil_way(r, p, land, fw, near, pit_wet);
	}
	// Cracks, with the land growing or blowing into them: grass on a green world, sand on a desert.
	// Blocks a metre or two across where the surface has crazed, and a crack across the road every few
	// metres; only up close, where they are more than a blur.
	if (hard && fw < 0.6) {
		// Wear: a road's age shows in a few cracks, repairs and holes, more of them the older it is and
		// fewer on concrete. Cracks follow the wheel paths or run straight across a lane, wander a little
		// and branch; they never loop. Each salt of noise is the road's own, so no two stretches crack alike.
		float detail = 1.0 - smoothstep(0.1, 0.35, fw);   // hairlines only up close
		float many = wear * (r.surface < 1.5 ? 0.6 : 1.0);
		float hw = r.half_width;
		float lane_w = min(3.6, hw);
		float lanes = max(1.0, floor(hw / lane_w + 0.3));
		float crack = 0.0;
		float thin = 0.004 + 0.006 * wear;
		// Along the wheel paths: each crack keeps to one path for a while, wandering a hand's width.
		for (int k = 0; k < 2; k++) {
			float salt = float(k) * 31.0 + r.style * 7.0 + hw;
			float lane = min(floor(along_noise(r.along * 0.004, salt + 5.0) * 2.0 * lanes), lanes - 1.0);
			float wheel = (lane + 0.5) * lane_w + (along_noise(r.along * 0.003, salt + 9.0) > 0.5 ? 0.9 : -0.9);
			float off = wheel + (along_noise(r.along * 0.05, salt) - 0.5) * 0.35 + (pn2(p, 2.3) - 0.5) * 0.04;
			float there = smoothstep(0.6 - 0.32 * many, 0.64 - 0.32 * many, along_noise(r.along * 0.025, salt + 11.0)) * step(off, hw - 0.3);
			crack = max(crack, band(x - off, thin, fw) * there);
			// A branch now and then: it splits off, angles away a little and stops.
			float bc = floor(r.along / 13.0 + float(k) * 0.5);
			float bh = hash3(vec3(bc, salt, 2.0));
			float b0 = (bc + 0.2 * fract(bh * 7.0)) * 13.0, bl = 2.0 + 5.0 * fract(bh * 3.1);
			float bt = clamp((r.along - b0) / bl, 0.0, 1.0);
			float boff = off + (fract(bh * 11.0) > 0.5 ? 1.0 : -1.0) * bt * (0.4 + 0.6 * fract(bh * 5.3));
			crack = max(crack, band(x - boff, thin * (1.0 - 0.5 * bt), fw) * there * step(bh, 0.5) * step(b0, r.along) * step(r.along, b0 + bl));
		}
		// Straight across a lane, every so often, the line only a little ragged.
		float span = 11.0;
		float cell = floor(r.along / span);
		float h = hash3(vec3(cell, 5.0, r.style + r.half_width));
		float at = (cell + 0.2 + 0.6 * fract(h * 13.7)) * span + (pn2(p, 0.7) - 0.5) * 0.25 + (pn2(p, 2.9) - 0.5) * 0.04;
		float lo = fract(h * 7.3) < 0.5 ? 0.0 : lane_w, hi = lo + lane_w * (0.6 + 0.4 * fract(h * 3.7));
		crack = max(crack, band(r.along - at, thin, fw) * step(h, 0.12 + 0.5 * many) * step(lo, x) * (1.0 - smoothstep(hi - 0.3, hi, x)));
		// Alligator cracking, only in small patches that have broken up.
		float broken = smoothstep(0.76, 0.8, pn2(p, 0.1) + 0.1 * many);
		if (broken > 0.0) {
			crack = max(crack, band(crazing(p, 0.35), thin * 0.8, fw) * broken);
			c *= 1.0 - 0.06 * broken;
		}
		crack *= detail * (0.4 + 0.6 * wear);
		// Dark hairlines with the land blown into them: sand on a desert, soil and grass on a green world.
		c = mix(c, mix(c * 0.25, land * 0.6, 0.1 + 0.25 * wear), crack);
		// Repairs: a strip of sealant along a wheel-path crack, and patches laid square across a lane,
		// fresher and finer than the road round them, with a seam at their edge.
		float seal_off = 0.5 * lane_w + (along_noise(r.along * 0.003, 71.0 + r.style) > 0.5 ? 0.9 : -0.9) + (along_noise(r.along * 0.05, 73.0) - 0.5) * 0.35;
		float sealed = band(x - seal_off, 0.06, fw) * smoothstep(0.68, 0.71, along_noise(r.along * 0.017, 83.0 + r.style)) * step(1.5, r.surface);
		c = mix(c, vec3(0.022, 0.022, 0.024), sealed * 0.85 * (1.0 - smoothstep(0.3, 0.8, fw)));
		float pcell = floor(r.along / 17.0);
		float ph = hash3(vec3(pcell, 9.0, r.half_width));
		float p0 = (pcell + 0.15 + 0.4 * fract(ph * 5.1)) * 17.0, p1 = p0 + 2.5 + 5.0 * fract(ph * 9.7);
		float lane_lo = fract(ph * 3.3) < 0.5 ? 0.0 : lane_w, lane_hi = min(lane_lo + lane_w, hw - 0.2);
		float pe = max(abs(r.along - (p0 + p1) * 0.5) - (p1 - p0) * 0.5, abs(x - (lane_lo + lane_hi) * 0.5) - (lane_hi - lane_lo) * 0.5);
		if (ph < 0.22 * many && pe < 0.05) {
			float inside = 1.0 - smoothstep(-fw * 0.5, fw * 0.5, pe);
			vec3 fresh = (r.surface < 1.5 ? vec3(0.2, 0.195, 0.18) : vec3(0.04, 0.04, 0.042)) * (0.92 + 0.16 * pn2(p, 5.0) * near);
			c = mix(c, fresh, inside);
			c *= 1.0 - 0.45 * band(pe, 0.012, fw) * (1.0 - smoothstep(0.05, 0.2, fw));
		}
		// Potholes, now and then on an old road: a broken lip, a dark bottom, sand in it, or water when it rains.
		vec2 hole_cell = floor(p / 5.0);
		vec2 hp = floor(planet_size / 5.0);
		vec3 hh = vec3(hash3(vec3(mod(hole_cell, hp), 23.0)), hash3(vec3(mod(hole_cell, hp), 29.0)), hash3(vec3(mod(hole_cell, hp), 37.0)));
		if (hh.x < 0.07 * many * many && x < hw - 0.6) {
			vec2 centre = (hole_cell + 0.3 + 0.4 * hh.yz) * 5.0;
			float radius = 0.2 + 0.3 * fract(hh.x * 91.0);
			vec2 rel = p - centre;
			float dist = length(rel) + (pn2(p, 3.1) - 0.5) * 0.3 * radius;
			float pit = 1.0 - smoothstep(radius - fw * 0.5, radius + fw * 0.5, dist);
			float lip = band(dist - radius - 0.05, 0.05, fw) * (1.0 - pit);
			float t = clamp(dist / radius, 0.0, 1.0);
			// The far wall in light, the near one in its own shadow, the bottom darkest.
			float wall = dot(normalize(rel + vec2(1e-4)), normalize(vec2(0.6, 0.8)));
			vec3 bottom = c * mix(0.22, 0.45 + 0.2 * wall, smoothstep(0.55, 1.0, t));
			bottom = mix(bottom, land * 0.5, 0.25 * (1.0 - t) * (1.0 - weather_ground.x));
			float water = smoothstep(0.15, 0.5, weather_ground.x) * (1.0 - smoothstep(0.7, 0.9, t));
			bottom = mix(bottom, vec3(0.03, 0.035, 0.04), water * 0.8);
			float seen = 1.0 - smoothstep(0.3, 0.8, fw);
			c = mix(c, bottom, pit * seen);
			c = mix(c, c * 1.35 + 0.01, lip * 0.6 * seen);
			pit_wet = pit * water * seen;
		}
	}
	// Markings: painted lines on laid roads, worn away in flakes, generated from the classes that meet.
	// The way with priority (the wider road, or the older of two alike) runs through a junction, its lines
	// dotted across the other's mouth; the other stops at a bar set back from the edge, behind a crosswalk
	// in town. A track gets no paint, nor does any road on a world that never painted its roads.
	if (hard && r.style < 1.5 && road_paint.z > 0.5) {
		bool gives_way = r.other_id == r.id || r.other_hw > r.half_width + 0.1 || abs(r.other_hw - r.half_width) <= 0.1 && r.other_id < r.id;
		// A junction is where roads meet at an angle; one running on into another is just the same road.
		// (A lane into a place's lot leaves by a crossover: no junction paint either way.)
		bool meets = r.other < 30.0 && r.other_id != r.id && abs(dot(r.dir, r.other_dir)) < 0.85 && max(r.lot, r.other_lot) < 0.5;
		float setback = r.town > 0.5 ? 4.6 : 1.6;           // a crosswalk 3 m deep and a metre clear of it, in town
		float flake = smoothstep(0.25, 0.7, pn2(p, 0.8) + 0.45 - 0.35 * wear) * (1.0 - 0.7 * wear) * (1.0 - smoothstep(0.2, 0.6, r.pad));
		float lines = 0.0;
		if (r.half_width >= 3.5) {
			float dash = band(fract(r.along / 9.0) * 9.0 - 1.5, 1.5, fw);
			bool dashed_centre = road_paint.y > 0.5;
			if (r.style < 0.5) {
				lines = band(x, 0.07, fw) * (dashed_centre ? dash : 1.0);
				if (r.half_width >= 4.0) lines = max(lines, band(x - (r.half_width - 0.55), 0.06, fw));
			} else {
				lines = max(band(x - 0.18, 0.06, fw), band(x - (r.half_width - 0.6), 0.08, fw));
				lines = max(lines, band(x - r.half_width * 0.5, 0.07, fw) * dash);
			}
			if (meets && gives_way) lines *= smoothstep(setback + 0.3, setback + 1.2, r.other);
			// Across the other road's mouth the way through is only dotted.
			if (meets && !gives_way && r.other < 0.5) lines *= band(fract(r.along / 1.8) * 1.8 - 0.45, 0.3, fw);
		}
		vec3 centre_ink = road_paint.x > 0.5 ? vec3(0.6, 0.45, 0.08) : vec3(0.62, 0.62, 0.6);
		vec3 ink = x < 0.4 && (r.style > 0.5 || road_paint.x > 0.5) ? centre_ink : vec3(0.62, 0.62, 0.6);
		c = mix(c, ink, lines * flake);
		if (meets && gives_way) {
			// The stop bar, across the way in, and in town a crosswalk of wide stripes in front of it.
			float bar = band(r.other - setback, 0.22, fw) * step(x, r.half_width - 0.35);
			float walk = 0.0;
			if (r.town > 0.5) {
				float stripe = band(fract((r.side + r.half_width) / 1.2) * 1.2 - 0.6, 0.28, fw);
				walk = stripe * band(r.other - (setback - 2.3), 1.5, fw) * step(x, r.half_width - 0.25);
			}
			c = mix(c, vec3(0.62, 0.62, 0.6), max(bar, walk) * flake);
		}
	}
	// The land takes it back from the edges in: grass and soil creep in along both, sand blows in from the
	// side the wind comes from in long tapering tongues, and the middle goes last. It lies over the markings.
	vec2 n = vec2(-r.dir.y, r.dir.x);
	float inward = -joined;   // metres in from the nearest edge of the joined surface
	float creep = r.half_width * (0.06 + 0.3 * wear * wear) * (0.45 + 0.9 * pn2(p, 0.15)) + (pn2(p, 1.1) - 0.5) * 0.3 * near;
	// A way of bare ground has no edge but where the scrub stops: it crowds in and leans over, raggedly.
	if (soil) creep += (r.half_width < 3.5 ? 0.3 : 0.12) * (0.4 + pn2(p, 0.7) + 0.6 * (pn2(p, 3.1) - 0.5) * near) * greenness(land);
	// Tongues lie along the road's own line, swelling and tapering as it goes: smooth at any angle to the map.
	float streak = smoothstep(0.45, 0.85, along_noise(r.along * 0.035, 51.0 + r.half_width) * 0.75 + pn2(p, 0.08) * 0.25);
	float windward = max(0.0, (r.side > 0.0 ? -1.0 : 1.0) * dot(n, road_wind));
	float reach = creep + 1.3 * r.half_width * wear * streak * streak * windward;
	float ruffle = (pn2(p, 2.7) - 0.5) * 0.12 * near;
	float over = 1.0 - smoothstep(reach - 0.25 - fw, reach + fw, inward + ruffle);
	over *= 0.92;
	c = mix(c, land, over);
	cover = deck * (1.0 - smoothstep(0.5, 1.0, r.core));
	rough = mix(mix(r.surface < 2.5 ? 0.82 : 0.95, 0.93, over), 0.08, pit_wet);
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
	// Not on a far ring inside the voxels' square: there it is only the wall it dives under them by, which
	// sticks up as a sliver where the coarse ground stands higher, and painted it would show as a floating sheet.
	// (Nor on a far ring's curtains, upright walls under each chunk's edge: painted, they show as pale sheets.)
	if (road_grid.w > 0.5 && up > (far_ring ? 0.3 : 0.7) && !(far_ring && inside_hole(wpos.xz) > 0.0)) {
		Road r = nearest_road(wpos.xz, wpos.y);
		if (r.edge < 8.0) {
			float fw = length(fwidth(wpos.xz)) * 0.7;
			float joint;
			vec4 road = paint_road(r, wpos.xz, soil, fw, near, road_cover, road_rough, joint);
			// Up close only level ground takes it (not the wall of a pit dug in it). A far grid is too
			// coarse to hold a deck's level through a tight bend, so there the road's own deck is painted
			// whole and only its shoulders keep off steep ground: one clean surface, not a smear. Not a
			// junction's kerb corners, though, nor a deck running onto a yard (whose ground is the yard's):
			// painted whole they made dark slabs edged by the coarse grid's triangles. They take only
			// level ground lying at the deck's height.
			float flat_ = smoothstep(0.72, 0.88, up);   // a deck is near level: a bank or fin beside it keeps the land's colour
			float keep = flat_;
			if (far_ring) {
				float level = 1.0 - smoothstep(1.5, 3.0, r.off);
				float own = max(road_cover - joint, 0.0) * (1.0 - smoothstep(0.2, 0.6, r.pad));
				keep = max(own, flat_ * mix(1.0, level, clamp(joint * 4.0, 0.0, 1.0)));
			}
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
