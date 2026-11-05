// The glass of every in-world screen: shows a SubViewport's page with phosphor glow,
// scanlines, a slow rolling band and the cursor.
//
// Everything that moves on its own (the band, the flicker, the cursor, an alert pulse) lives
// here rather than in the page, so the SubViewport only redraws when the page itself changes.
shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled;

uniform sampler2D content : source_color, filter_linear, repeat_disable;
uniform vec2 pixels = vec2(1280.0, 720.0);
uniform float brightness = 1.0;           // kept under the scene glow threshold: blooming blocks would wash out dark text on them
uniform float glow = 0.55;                // halo around bright strokes
uniform float scanlines = 0.16;           // strength; fades out when lines get smaller than a pixel
uniform vec2 cursor = vec2(-100.0);       // in page pixels
uniform float cursor_on = 0.0;
uniform float cursor_size = 30.0;         // the arrow's height, in page pixels
uniform vec4 cursor_fill : source_color = vec4(1.0, 0.86, 0.32, 1.0);
uniform vec4 cursor_edge : source_color = vec4(0.04, 0.05, 0.08, 1.0);
uniform float alert = 0.0;                // red pulse around the edge during an alert
uniform vec4 alert_color : source_color = vec4(1.0, 0.18, 0.22, 1.0);

// Signed distance to a triangle (Inigo Quilez). Used for the chunky arrow cursor.
float sd_triangle(vec2 p, vec2 p0, vec2 p1, vec2 p2) {
	vec2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
	vec2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
	vec2 pq0 = v0 - e0 * clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
	vec2 pq1 = v1 - e1 * clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
	vec2 pq2 = v2 - e2 * clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
	float s = sign(e0.x * e2.y - e0.y * e2.x);
	vec2 d = min(min(vec2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
	                 vec2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
	                 vec2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
	return -sqrt(d.x) * sign(d.y);
}

void fragment() {
	vec2 uv = UV;
	vec2 px = uv * pixels;

	// Viewport textures have no mipmaps: supersample a little when the screen is far away,
	// so small text shimmers less.
	vec2 fw = fwidth(uv);
	vec2 ss = fw * 0.3;
	vec3 c = (texture(content, uv + vec2(-ss.x, -ss.y)).rgb + texture(content, uv + vec2(ss.x, -ss.y)).rgb
	        + texture(content, uv + vec2(-ss.x, ss.y)).rgb + texture(content, uv + vec2(ss.x, ss.y)).rgb) * 0.25;

	// Phosphor halo: bright strokes bleed a few pixels into the dark glass.
	vec2 g = 3.0 / pixels;
	vec3 halo = texture(content, uv + vec2(g.x, 0.0)).rgb + texture(content, uv - vec2(g.x, 0.0)).rgb
	          + texture(content, uv + vec2(0.0, g.y)).rgb + texture(content, uv - vec2(0.0, g.y)).rgb
	          + texture(content, uv + g).rgb + texture(content, uv - g).rgb
	          + texture(content, uv + vec2(g.x, -g.y)).rgb + texture(content, uv + vec2(-g.x, g.y)).rgb;
	halo *= 0.125;
	// Glow bleeds bright strokes into dark glass, but must not wash out dark text printed on a
	// bright block (a wanted row, a button): when the surroundings are mostly bright, hold back.
	float around = dot(halo, vec3(0.3, 0.5, 0.2));
	float bleed = smoothstep(0.2, 0.7, max(halo.r, max(halo.g, halo.b))) * (1.0 - smoothstep(0.3, 0.55, around));
	c += max(halo - c, vec3(0.0)) * glow * bleed;

	// Scanlines every 3 page pixels, faded out once they would alias.
	float line_px = fwidth(px.y);
	float line_fade = clamp(1.6 - line_px, 0.0, 1.0);
	float line = 1.0 - scanlines * line_fade * (0.5 + 0.5 * cos(px.y * TAU / 3.0));

	// A soft band rolling slowly down the glass, and the faintest flicker.
	float band = 1.0 + 0.05 * exp(-pow((fract(uv.y - TIME * 0.07) - 0.5) * 9.0, 2.0));
	float flicker = 1.0 + 0.012 * sin(TIME * 61.0) * sin(TIME * 7.3);

	// Rounded-corner vignette, like a slightly curved tube.
	vec2 q = uv * 2.0 - 1.0;
	float vignette = 1.0 - 0.28 * pow(max(abs(q.x), abs(q.y)), 6.0) - 0.08 * dot(q, q);

	vec3 col = c * line * band * flicker * vignette * brightness;

	// Alert: a red frame pulsing round the edge of the glass. Kept to the margin so the
	// countdown on the page stays readable.
	float edge = smoothstep(0.9, 0.99, max(abs(q.x), abs(q.y)));
	col = mix(col, alert_color.rgb, alert * edge * (0.45 + 0.4 * sin(TIME * 9.0)));

	// The cursor: a chunky arrow with a dark outline, drawn 30 units tall and scaled to cursor_size.
	if (cursor_on > 0.5) {
		vec2 p = (px - cursor) * (30.0 / max(cursor_size, 1.0));
		float d = sd_triangle(p, vec2(0.0, 0.0), vec2(0.0, 30.0), vec2(22.0, 22.0));
		float aa = max(fwidth(d), 0.5);
		float fill = 1.0 - smoothstep(-aa, aa, d);
		float rim = 1.0 - smoothstep(-aa, aa, d - 4.0);
		col = mix(col, cursor_edge.rgb, rim);
		col = mix(col, cursor_fill.rgb * 1.35, fill);
	}

	// A faint sheen on the glass itself, brighter near the top.
	col += vec3(0.035, 0.04, 0.05) * smoothstep(0.2, -0.6, q.y + q.x * 0.25);

	ALBEDO = col;
}
