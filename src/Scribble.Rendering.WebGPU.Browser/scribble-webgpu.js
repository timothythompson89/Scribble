// Browser side of Scribble.Rendering.WebGPU.Browser: drives navigator.gpu for canvases.
// It mirrors the native backend: the same WGSL shape shader (passed in from .NET), the same
// shape layout (24 floats per shape), one instanced draw per frame. The browser presents a
// canvas's current texture automatically when the task that drew it ends.

const BYTES_PER_SHAPE = 24 * 4;

let device = null;
let adapterInfo = "";
let format = null;
let pipeline = null;
let globals = null;
let shapes = null;
let shapesCapacity = 0;
let bindGroup = null;
let atlas = null;      // r8unorm coverage
let colorAtlas = null; // rgba8unorm, premultiplied
const contexts = new Map();

function ensureCapacity(count) {
  if (count <= shapesCapacity) return;
  let capacity = Math.max(1024, shapesCapacity);
  while (capacity < count) capacity *= 2;
  if (shapes) shapes.destroy();
  shapes = device.createBuffer({ label: "Scribble shapes", size: capacity * BYTES_PER_SHAPE, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
  shapesCapacity = capacity;
  bindGroup = device.createBindGroup({
    layout: pipeline.getBindGroupLayout(0),
    entries: [
      { binding: 0, resource: { buffer: globals } },
      { binding: 1, resource: { buffer: shapes } },
      { binding: 2, resource: atlas.createView() },
      { binding: 3, resource: colorAtlas.createView() },
    ],
  });
}

// powerPreference: "" for the browser's choice, or "high-performance" / "low-power".
export async function init(powerPreference, shaderSource, atlasSize, colorAtlasSize) {
  if (!navigator.gpu) {
    throw new Error("WebGPU is not available in this browser (try a recent Chrome, Edge or Firefox).");
  }
  // Only pass the option when asked: Chrome warns about it on Windows, where it's ignored.
  const options = powerPreference ? { powerPreference } : {};
  let adapter = await navigator.gpu.requestAdapter(options);
  let compatibility = false;
  if (!adapter) {
    // Devices with only OpenGL ES (many Android phones) offer WebGPU's reduced "compatibility"
    // feature level, and only to pages that ask for it.
    adapter = await navigator.gpu.requestAdapter({ ...options, featureLevel: "compatibility" });
    compatibility = !!adapter;
  }
  if (!adapter) throw new Error("WebGPU: no suitable GPU adapter.");
  // Shapes live in a storage buffer read by both shader stages. Compatibility mode's default
  // allows none outside compute, so ask for what this adapter can do.
  const requiredLimits = {};
  for (const name of ["maxStorageBuffersInVertexStage", "maxStorageBuffersInFragmentStage"]) {
    const value = adapter.limits[name];
    if (value === undefined) continue;
    if (value < 1) throw new Error(`WebGPU: this GPU allows no storage buffers in shaders (${name} = 0), which Scribble needs.`);
    requiredLimits[name] = 1;
  }
  device = await adapter.requestDevice({ label: "Scribble device", requiredLimits });
  const info = adapter.info || {};
  adapterInfo =
    ([info.vendor, info.architecture, info.description].filter((s) => s).join(" ") || "unknown adapter") +
    (compatibility ? ", compatibility mode" : "");
  // The canvas format the browser composites fastest; never an sRGB format, which matches
  // Scribble's rule of writing sRGB color values as-is.
  format = navigator.gpu.getPreferredCanvasFormat();
  device.addEventListener("uncapturederror", (e) => console.error("[WebGPU]", e.error.message));
  device.lost.then((i) => { if (i.reason !== "destroyed") console.error("[WebGPU] device lost:", i.message); });

  const module = device.createShaderModule({ label: "Scribble shapes", code: shaderSource });
  const blend = { operation: "add", srcFactor: "one", dstFactor: "one-minus-src-alpha" };
  pipeline = await device.createRenderPipelineAsync({
    label: "Scribble shapes",
    layout: "auto",
    vertex: { module, entryPoint: "vs_main" },
    fragment: { module, entryPoint: "fs_main", targets: [{ format, blend: { color: blend, alpha: blend } }] },
    primitive: { topology: "triangle-list" },
  });
  globals = device.createBuffer({ label: "Scribble globals", size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
  const atlasUsage = GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST;
  atlas = device.createTexture({ label: "Scribble glyph atlas", size: [atlasSize, atlasSize], format: "r8unorm", usage: atlasUsage });
  colorAtlas = device.createTexture({ label: "Scribble color glyph atlas", size: [colorAtlasSize, colorAtlasSize], format: "rgba8unorm", usage: atlasUsage });
  ensureCapacity(1);
}

export function description() {
  return `WebGPU/Browser on ${adapterInfo} (${format})`;
}

export function createSurface(id) {
  const canvas = document.getElementById(id);
  if (!canvas) throw new Error(`No canvas with id '${id}'.`);
  const context = canvas.getContext("webgpu");
  context.configure({ device, format, alphaMode: "opaque" });
  contexts.set(id, context);
}

export function destroySurface(id) {
  const context = contexts.get(id);
  if (context) context.unconfigure();
  contexts.delete(id);
}

// Copies a changed region of a glyph atlas (tightly packed rows) to the GPU.
// which: 0 = coverage atlas (1 byte per pixel), 1 = color atlas (4 bytes per pixel).
export function uploadAtlas(which, x, y, width, height, pixels) {
  const texture = which === 1 ? colorAtlas : atlas;
  const bytesPerPixel = which === 1 ? 4 : 1;
  device.queue.writeTexture({ texture, origin: [x, y] }, pixels.slice(), { bytesPerRow: width * bytesPerPixel }, [width, height]);
}

// Clears the canvas and draws `count` shapes from `shapeBytes` (a .NET MemoryView, valid only
// during this call) in one render pass.
export function draw(id, r, g, b, a, shapeBytes, count) {
  const context = contexts.get(id);
  if (!context || context.canvas.width === 0 || context.canvas.height === 0) return;
  const width = context.canvas.width;
  const height = context.canvas.height;

  if (count > 0) {
    ensureCapacity(count);
    device.queue.writeBuffer(globals, 0, new Float32Array([width, height, 0, 0]));
    device.queue.writeBuffer(shapes, 0, shapeBytes.slice());
  }

  const encoder = device.createCommandEncoder();
  const pass = encoder.beginRenderPass({
    colorAttachments: [{ view: context.getCurrentTexture().createView(), loadOp: "clear", storeOp: "store", clearValue: { r, g, b, a } }],
  });
  if (count > 0) {
    pass.setPipeline(pipeline);
    pass.setBindGroup(0, bindGroup);
    pass.draw(6, count);
  }
  pass.end();
  device.queue.submit([encoder.finish()]);
}

export function dispose() {
  for (const id of [...contexts.keys()]) destroySurface(id);
  if (device) device.destroy();
  device = null;
}

//# sourceURL=scribble-webgpu.js
