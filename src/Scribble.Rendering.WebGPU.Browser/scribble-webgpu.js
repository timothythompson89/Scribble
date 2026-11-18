// Browser side of Scribble.Rendering.WebGPU.Browser: drives navigator.gpu for canvases.
// It mirrors the native backend: the same WGSL shape shader (passed in from .NET), the same
// shape layout (24 floats per shape), one instanced draw per run of shapes on the same image page
// (one per frame unless images span pages). The browser presents a
// canvas's current texture automatically when the task that drew it ends.

const BYTES_PER_SHAPE = 24 * 4;

let device = null;
let adapterInfo = "";
let format = null;
let pipeline = null;
let shaderSourceKept = null; // for the offscreen pipeline
let globals = null;
let shapes = null;
let shapesCapacity = 0;
// Bind groups per image page (-1: the 1 × 1 placeholder), dropped when the shapes buffer is reallocated.
const bindGroups = new Map();
const imagePages = new Map(); // id → rgba8unorm texture (DESIGN.md, "M8 step 3b", (c))
let placeholder = null;
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
  bindGroups.clear();
}

function bindGroupFor(page) {
  let group = bindGroups.get(page);
  if (!group) {
    const texture = page < 0 ? placeholder : imagePages.get(page);
    group = device.createBindGroup({
      layout: pipeline.getBindGroupLayout(0),
      entries: [
        { binding: 0, resource: { buffer: globals } },
        { binding: 1, resource: { buffer: shapes } },
        { binding: 2, resource: atlas.createView() },
        { binding: 3, resource: colorAtlas.createView() },
        { binding: 4, resource: texture.createView() },
      ],
    });
    bindGroups.set(page, group);
  }
  return group;
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

  shaderSourceKept = shaderSource;
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
  placeholder = device.createTexture({ label: "Scribble image placeholder", size: [1, 1], format: "rgba8unorm", usage: atlasUsage });
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

// The largest texture this device takes (compatibility mode on phones may give 4096).
export function maxTextureSize() {
  return device.limits.maxTextureDimension2D;
}

// Image pages: RGBA8 textures the image atlas (ImageAtlas.fs) fills.
export function createImagePage(id, width, height) {
  imagePages.set(id, device.createTexture({ label: `Scribble image page ${id}`, size: [width, height], format: "rgba8unorm", usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST }));
}

export function destroyImagePage(id) {
  const texture = imagePages.get(id);
  if (texture) texture.destroy();
  imagePages.delete(id);
  bindGroups.delete(id);
  offscreenBindGroups.delete(id);
}

// Copies a mip level (tightly packed premultiplied RGBA rows) into a page at (x, y).
export function uploadImage(id, x, y, width, height, pixels) {
  device.queue.writeTexture({ texture: imagePages.get(id), origin: [x, y] }, pixels.slice(), { bytesPerRow: width * 4 }, [width, height]);
}

// Clears the canvas and draws `count` shapes from `shapeBytes` (a .NET MemoryView, valid only
// during this call) in one render pass: a draw call per run of shapes on the same image page
// (`runBytes`: int32 first, count, page for each run).
export function draw(id, r, g, b, a, shapeBytes, count, runBytes) {
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
    const runs = new Int32Array(runBytes.slice().buffer);
    for (let i = 0; i + 2 < runs.length; i += 3) {
      pass.setBindGroup(0, bindGroupFor(runs[i + 2]));
      pass.draw(6, runs[i + 1], 0, runs[i]);
    }
  }
  pass.end();
  device.queue.submit([encoder.finish()]);
}


// --- Offscreen rendering (DESIGN.md, "Rendering to an image and saving it", decision 3) ----------------------

// The pipeline for offscreen targets (rgba8unorm, which the canvas's preferred format may not be) and its bind
// groups ("auto" layouts don't match across pipelines), made on first use.
let offscreenPipeline = null;
const offscreenBindGroups = new Map();
let offscreenShapesCapacity = 0;
// The last offscreen render's pixels, until .NET takes them (takeReadback).
let readback = null;

function offscreenPipelineFor() {
  if (offscreenShapesCapacity !== shapesCapacity) {
    offscreenBindGroups.clear();
    offscreenShapesCapacity = shapesCapacity;
  }
  if (!offscreenPipeline) {
    if (format === "rgba8unorm") {
      offscreenPipeline = pipeline;
    } else {
      const module = device.createShaderModule({ label: "Scribble shapes (offscreen)", code: shaderSourceKept });
      const blend = { operation: "add", srcFactor: "one", dstFactor: "one-minus-src-alpha" };
      offscreenPipeline = device.createRenderPipeline({
        label: "Scribble shapes (offscreen)",
        layout: "auto",
        vertex: { module, entryPoint: "vs_main" },
        fragment: { module, entryPoint: "fs_main", targets: [{ format: "rgba8unorm", blend: { color: blend, alpha: blend } }] },
        primitive: { topology: "triangle-list" },
      });
    }
  }
  return offscreenPipeline;
}

function offscreenBindGroupFor(page) {
  if (offscreenPipeline === pipeline) return bindGroupFor(page);
  let group = offscreenBindGroups.get(page);
  if (!group) {
    const texture = page < 0 ? placeholder : imagePages.get(page);
    group = device.createBindGroup({
      layout: offscreenPipeline.getBindGroupLayout(0),
      entries: [
        { binding: 0, resource: { buffer: globals } },
        { binding: 1, resource: { buffer: shapes } },
        { binding: 2, resource: atlas.createView() },
        { binding: 3, resource: colorAtlas.createView() },
        { binding: 4, resource: texture.createView() },
      ],
    });
    offscreenBindGroups.set(page, group);
  }
  return group;
}

// As `draw`, into a width × height rgba8unorm texture, then reads it back (tightly packed rows, premultiplied):
// resolves when the pixels are ready for takeReadback. The shapes are copied before the first await (the .NET
// MemoryViews are valid only during this call).
export async function renderOffscreen(width, height, r, g, b, a, shapeBytes, count, runBytes) {
  const shapeCopy = count > 0 ? shapeBytes.slice() : null;
  const runs = new Int32Array(runBytes.slice().buffer);
  if (count > 0) ensureCapacity(count);
  const pipe = offscreenPipelineFor();
  const texture = device.createTexture({
    label: "Scribble offscreen",
    size: [width, height],
    format: "rgba8unorm",
    usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC,
  });
  const paddedRow = Math.ceil((width * 4) / 256) * 256;
  const buffer = device.createBuffer({ label: "Scribble readback", size: paddedRow * height, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST });
  try {
    if (count > 0) {
      device.queue.writeBuffer(globals, 0, new Float32Array([width, height, 0, 0]));
      device.queue.writeBuffer(shapes, 0, shapeCopy);
    }
    const encoder = device.createCommandEncoder();
    const pass = encoder.beginRenderPass({
      colorAttachments: [{ view: texture.createView(), loadOp: "clear", storeOp: "store", clearValue: { r, g, b, a } }],
    });
    if (count > 0) {
      pass.setPipeline(pipe);
      for (let i = 0; i + 2 < runs.length; i += 3) {
        pass.setBindGroup(0, offscreenBindGroupFor(runs[i + 2]));
        pass.draw(6, runs[i + 1], 0, runs[i]);
      }
    }
    pass.end();
    encoder.copyTextureToBuffer({ texture }, { buffer, bytesPerRow: paddedRow, rowsPerImage: height }, [width, height]);
    device.queue.submit([encoder.finish()]);
    await buffer.mapAsync(GPUMapMode.READ);
    const mapped = new Uint8Array(buffer.getMappedRange());
    const tight = new Uint8Array(width * 4 * height);
    for (let y = 0; y < height; y++) tight.set(mapped.subarray(y * paddedRow, y * paddedRow + width * 4), y * width * 4);
    buffer.unmap();
    readback = tight;
  } finally {
    buffer.destroy();
    texture.destroy();
  }
}

// The pixels of the render that just finished (once).
export function takeReadback() {
  const pixels = readback;
  readback = null;
  return pixels;
}

export function dispose() {
  for (const id of [...contexts.keys()]) destroySurface(id);
  if (device) device.destroy();
  device = null;
}

//# sourceURL=scribble-webgpu.js
