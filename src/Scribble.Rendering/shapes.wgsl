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
  params: vec4f,  // x = border width (px), y = shadow sigma (px, 0 = solid), z = kind (0 shape, 1 glyph, 2 color glyph)
  clip: vec4f,    // x0, y0, x1, y1 (pixels)
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var<storage, read> shapes: array<Shape>;
@group(0) @binding(2) var glyph_atlas: texture_2d<f32>;   // r8unorm coverage
@group(0) @binding(3) var color_atlas: texture_2d<f32>;   // rgba8unorm, premultiplied (emoji)

struct VertexOut {
  @builtin(position) position: vec4f,
  @location(0) local: vec2f,                       // position relative to the shape's centre
  @location(1) @interpolate(flat) index: u32,
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
  return premultiply(shape.fill) * inner + premultiply(shape.border) * (outer - inner);
}
