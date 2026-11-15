// Browser side of Scribble.Text.HarfBuzz.Browser: HarfBuzz compiled to WebAssembly (the
// harfbuzz.wasm from the official harfbuzzjs package), loaded without harfbuzzjs's JavaScript
// wrapper. The module only needs five small imports, and it exports the plain HarfBuzz C API,
// which we call directly, exactly like the native shaper does.

let hb = null;          // the wasm exports
let wasmBytes = null;
let buffer = 0;         // one reusable hb_buffer_t
const fonts = new Map(); // Scribble font id -> hb_font_t pointer

const imports = {
  env: {
    _emscripten_runtime_keepalive_clear: () => {},
    _abort_js: () => { throw new Error("HarfBuzz aborted."); },
    _setitimer_js: () => 0,
    // malloc asks for more memory: grow the wasm memory to at least `requested` bytes.
    emscripten_resize_heap: (requested) => {
      const memory = hb.memory;
      const pages = Math.ceil(((requested >>> 0) - memory.buffer.byteLength) / 65536);
      try { memory.grow(pages); return 1; } catch { return 0; }
    },
  },
  wasi_snapshot_preview1: {
    proc_exit: (code) => { throw new Error(`HarfBuzz exited with code ${code}.`); },
  },
};

// Step 1 (synchronous, so .NET can pass the bytes as a short-lived MemoryView): keep a copy.
export function stageWasm(bytes) {
  wasmBytes = bytes.slice();
}

// Step 2: compile and instantiate.
export async function init() {
  const { instance } = await WebAssembly.instantiate(wasmBytes, imports);
  wasmBytes = null;
  hb = instance.exports;
  if (hb.__wasm_call_ctors) hb.__wasm_call_ctors();
  buffer = hb.hb_buffer_create();
}

export function hasFont(id) {
  return fonts.has(id);
}

// Copies a font file into wasm memory (kept for the page's lifetime) and creates an hb_font_t
// whose scale is the font's units per em, so results come back in font units.
export function loadFont(id, bytes) {
  const data = bytes.slice();
  const pointer = hb.malloc(data.length);
  new Uint8Array(hb.memory.buffer).set(data, pointer);
  const blob = hb.hb_blob_create(pointer, data.length, 1 /* HB_MEMORY_MODE_READONLY */, 0, 0);
  const face = hb.hb_face_create(blob, 0);
  const font = hb.hb_font_create(face);
  const upem = hb.hb_face_get_upem(face);
  hb.hb_font_set_scale(font, upem, upem);
  fonts.set(id, font);
}

// Shapes `text` and returns 6 numbers per glyph:
// glyph id, cluster, x advance, y advance, x offset, y offset (font units).
export function shape(id, text) {
  const font = fonts.get(id);
  const length = text.length;
  const pointer = hb.malloc(Math.max(2, length * 2));
  const utf16 = new Uint16Array(hb.memory.buffer, pointer, length);
  for (let i = 0; i < length; i++) utf16[i] = text.charCodeAt(i);

  hb.hb_buffer_clear_contents(buffer);
  hb.hb_buffer_add_utf16(buffer, pointer, length, 0, length);
  hb.hb_buffer_guess_segment_properties(buffer);
  hb.hb_shape(font, buffer, 0, 0);
  hb.free(pointer);

  const count = hb.hb_buffer_get_length(buffer);
  const infos = hb.hb_buffer_get_glyph_infos(buffer, 0) >>> 2;      // hb_glyph_info_t: 5 × u32
  const positions = hb.hb_buffer_get_glyph_positions(buffer, 0) >>> 2; // hb_glyph_position_t: 5 × i32
  const u32 = new Uint32Array(hb.memory.buffer);
  const i32 = new Int32Array(hb.memory.buffer);
  const result = new Array(count * 6);
  for (let g = 0; g < count; g++) {
    const info = infos + g * 5;
    const position = positions + g * 5;
    result[g * 6] = u32[info];
    result[g * 6 + 1] = u32[info + 2];
    result[g * 6 + 2] = i32[position];
    result[g * 6 + 3] = i32[position + 1];
    result[g * 6 + 4] = i32[position + 2];
    result[g * 6 + 5] = i32[position + 3];
  }
  return result;
}

//# sourceURL=scribble-harfbuzz.js
