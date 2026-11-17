// Scribble's shape shader, shared by the native and browser WebGPU backends.
//
// Every shape is one instance of a 6-vertex quad. Glyphs use the same instance layout (kind 1),
// so text and shapes share one draw call and keep their drawing order.
// The fragment shader evaluates the signed
// distance to a rounded rectangle and turns it into coverage, which gives anti-aliased edges,
// rounded corners, inside borders and Gaussian-blurred shadows from a single pipeline.
// Colors are straight-alpha sRGB values; the output is premultiplied, blended with
// (One, OneMinusSrcAlpha). The Shape layout must match DrawList.fs.

struct Globals {
  viewport: vec2f,   // target size in physical pixels
  _pad: vec2f,
}

struct Shape {
  rect: vec4f,    // x, y, width, height (pixels)
  radii: vec4f,   // top-left, top-right, bottom-right, bottom-left (px); glyphs: xy = atlas texel
  fill: vec4f,    // rgba
  border: vec4f,  // rgba
  params: vec3f,  // x = border width (px), y = shadow sigma (px, 0 = solid), z = kind (0 shape, 1 glyph, 2 color glyph)
  pattern: u32,   // border pattern: 0 solid; else dash (high 16 bits) and gap (low 16 bits) in 1/16 px; dash 0xFFFF = dotted
  clip: vec4f,    // x0, y0, x1, y1 (pixels)
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var<storage, read> shapes: array<Shape>;
@group(0) @binding(2) var glyph_atlas: texture_2d<f32>;   // r8unorm coverage
@group(0) @binding(3) var color_atlas: texture_2d<f32>;   // rgba8unorm, premultiplied (emoji)

struct VertexOut {
  @builtin(position) position: vec4f,
  @location(0) local: vec2f,                       // position relative to the shape's centre
  @location(1) @interpolate(flat, either) index: u32,  // "either": the same for all six vertices; WebGPU compatibility mode (OpenGL ES phones) allows only this
}

@vertex
fn vs_main(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> VertexOut {
  let shape = shapes[instance];
  var corners = array<vec2f, 6>(
    vec2f(0.0, 0.0), vec2f(1.0, 0.0), vec2f(0.0, 1.0),
    vec2f(0.0, 1.0), vec2f(1.0, 0.0), vec2f(1.0, 1.0));
  // Grow the quad so anti-aliased edges (1 px) and blurred shadows (3 sigma) aren't cut off.
  // Glyph quads are exact: their anti-aliasing is already in the atlas.
  let margin = select(1.0 + 3.0 * shape.params.y, 0.0, shape.params.z >= 1.0);
  let p = shape.rect.xy - vec2f(margin) + corners[vertex] * (shape.rect.zw + vec2f(2.0 * margin));

  var out: VertexOut;
  out.position = vec4f(p / globals.viewport * vec2f(2.0, -2.0) + vec2f(-1.0, 1.0), 0.0, 1.0);
  out.local = p - (shape.rect.xy + 0.5 * shape.rect.zw);
  out.index = instance;
  return out;
}

// Signed distance from p to a rounded box centred at the origin with half-size b.
// Negative inside. r = radii (tl, tr, br, bl); y points down.
fn sd_rounded_box(p: vec2f, b: vec2f, r: vec4f) -> f32 {
  var radius: f32;
  if (p.x < 0.0) {
    radius = select(r.x, r.w, p.y > 0.0);
  } else {
    radius = select(r.y, r.z, p.y > 0.0);
  }
  let q = abs(p) - b + vec2f(radius);
  return min(max(q.x, q.y), 0.0) + length(max(q, vec2f(0.0))) - radius;
}

// Fast approximation of the error function (max error ~5e-4), enough for shadows.
fn erf_approx(x: f32) -> f32 {
  let s = sign(x);
  let a = abs(x);
  var t = 1.0 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
  t = t * t;
  return s - s / (t * t);
}

fn premultiply(c: vec4f) -> vec4f {
  return vec4f(c.rgb * c.a, c.a);
}

// How far along the border's centre line p lies, clockwise from where the top edge leaves the
// top-left corner (x), and the centre line's whole length (y). b = half size, r = outer radii
// (tl, tr, br, bl), w = border width. Corners follow arcs around the corner centres.
fn border_position(p: vec2f, b: vec2f, r: vec4f, w: f32) -> vec2f {
  let rc = max(r - vec4f(0.5 * w), vec4f(0.0));        // centre-line radii
  let quarter = 1.5707963;
  let top = 2.0 * b.x - r.x - r.y;
  let right = 2.0 * b.y - r.y - r.z;
  let bottom = 2.0 * b.x - r.z - r.w;
  let left = 2.0 * b.y - r.w - r.x;
  let s1 = top;
  let s2 = s1 + quarter * rc.y;
  let s3 = s2 + right;
  let s4 = s3 + quarter * rc.z;
  let s5 = s4 + bottom;
  let s6 = s5 + quarter * rc.w;
  let s7 = s6 + left;
  let total = s7 + quarter * rc.x;
  // Corners: inside the square between the corner's centre and the box's corner.
  if (r.y > 0.0 && p.x > b.x - r.y && p.y < -b.y + r.y) {
    let v = p - vec2f(b.x - r.y, -b.y + r.y);
    return vec2f(s1 + clamp(atan2(v.y, v.x) + quarter, 0.0, quarter) * rc.y, total);
  }
  if (r.z > 0.0 && p.x > b.x - r.z && p.y > b.y - r.z) {
    let v = p - vec2f(b.x - r.z, b.y - r.z);
    return vec2f(s3 + clamp(atan2(v.y, v.x), 0.0, quarter) * rc.z, total);
  }
  if (r.w > 0.0 && p.x < -b.x + r.w && p.y > b.y - r.w) {
    let v = p - vec2f(-b.x + r.w, b.y - r.w);
    return vec2f(s5 + clamp(atan2(v.y, v.x) - quarter, 0.0, quarter) * rc.w, total);
  }
  if (r.x > 0.0 && p.x < -b.x + r.x && p.y < -b.y + r.x) {
    let v = p - vec2f(-b.x + r.x, -b.y + r.x);
    var a = atan2(v.y, v.x);
    if (a < 0.0) { a = a + 4.0 * quarter; }
    return vec2f(s7 + clamp(a - 2.0 * quarter, 0.0, quarter) * rc.x, total);
  }
  // Straight sides: the nearest edge.
  let dx = b.x - abs(p.x);
  let dy = b.y - abs(p.y);
  if (dy <= dx) {
    if (p.y < 0.0) { return vec2f(p.x - (-b.x + r.x), total); }
    return vec2f(s4 + (b.x - r.z) - p.x, total);
  }
  if (p.x > 0.0) { return vec2f(s2 + p.y - (-b.y + r.y), total); }
  return vec2f(s6 + (b.y - r.w) - p.y, total);
}

@fragment
fn fs_main(in: VertexOut) -> @location(0) vec4f {
  let shape = shapes[in.index];

  // Rectangular clip, in pixels. position.xy is the pixel centre.
  let pixel = in.position.xy;
  if (pixel.x < shape.clip.x || pixel.y < shape.clip.y || pixel.x >= shape.clip.z || pixel.y >= shape.clip.w) {
    discard;
  }

  if (shape.params.z >= 1.0) {
    // Glyphs: read texel for texel; quads are pixel-aligned, so no filtering.
    let texel = vec2i(floor(pixel - shape.rect.xy)) + vec2i(shape.radii.xy);
    if (shape.params.z == 2.0) {
      // Color glyph: already premultiplied; the tint's alpha fades it.
      return textureLoad(color_atlas, texel, 0) * shape.fill.a;
    }
    let coverage = textureLoad(glyph_atlas, texel, 0).r;
    return premultiply(shape.fill) * coverage;
  }

  let half = 0.5 * shape.rect.zw;
  let radii = min(shape.radii, vec4f(min(half.x, half.y)));
  let d = sd_rounded_box(in.local, half, radii);

  let sigma = shape.params.y;
  if (sigma > 0.0) {
    // A blurred box: integrate a Gaussian across the edge.
    let coverage = 0.5 - 0.5 * erf_approx(d / (sigma * 1.41421356));
    return premultiply(shape.fill) * coverage;
  }

  // Coverage of the shape and of the area inside the border (anti-aliased over one pixel).
  let outer = clamp(0.5 - d, 0.0, 1.0);
  let width = shape.params.x;
  let inner = select(outer, clamp(0.5 - (d + width), 0.0, 1.0), width > 0.0);
  var ring = outer - inner;
  if (shape.pattern != 0u && width > 0.0 && ring > 0.0) {
    let dashBits = shape.pattern >> 16u;
    let dotted = dashBits == 0xFFFFu;
    // A box no thicker than its border is a line (a Separator): an open path along its long side,
    // with a dash or dot at both ends. Anything else is a closed path around the box.
    let isLine = min(shape.rect.z, shape.rect.w) <= width + 0.01;
    var along: vec2f;
    var across: f32;
    if (isLine) {
      let horizontal = shape.rect.z >= shape.rect.w;
      along = select(vec2f(in.local.y + half.y, shape.rect.w), vec2f(in.local.x + half.x, shape.rect.z), horizontal);
      across = select(in.local.x, in.local.y, horizontal);
    } else {
      along = border_position(in.local, half, radii, width);
      across = d + 0.5 * width;                     // from the centre line
    }
    if (dotted) {
      // Dots as wide as the line, about one width apart, evened out.
      var a: f32;
      if (isLine) {
        let n = max(1.0, round((along.y - width) / (2.0 * width)));
        let spacing = max(along.y - width, 0.001) / n;
        a = fract((along.x - 0.5 * width) / spacing + 0.5) * spacing - 0.5 * spacing;
      } else {
        let n = max(1.0, round(along.y / (2.0 * width)));
        let period = along.y / n;
        a = fract(along.x / period) * period - 0.5 * period;
      }
      ring = clamp(0.5 * width - length(vec2f(a, across)) + 0.5, 0.0, 1.0);
    } else {
      let dash = f32(dashBits) / 16.0;
      let gap = f32(shape.pattern & 0xFFFFu) / 16.0;
      var a: f32;
      var on: f32;
      if (isLine) {
        // n dashes and n - 1 gaps fill the line exactly.
        let n = max(1.0, round((along.y + gap) / (dash + gap)));
        let k = along.y / (n * (dash + gap) - gap);
        let period = (dash + gap) * k;
        on = dash * k;
        a = fract(along.x / period) * period;
      } else {
        let n = max(1.0, round(along.y / (dash + gap)));
        let period = along.y / n;
        on = dash * period / (dash + gap);
        // Each dash centred in its period, so the pattern is symmetric where it meets itself.
        a = fract(along.x / period) * period - 0.5 * (period - on);
      }
      ring = ring * clamp(min(a, on - a) + 0.5, 0.0, 1.0);
    }
  }
  return premultiply(shape.fill) * inner + premultiply(shape.border) * ring;
}
