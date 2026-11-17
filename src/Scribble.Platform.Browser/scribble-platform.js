// Browser side of Scribble.Platform.Browser.
//
// DOM events are queued as fixed-size numeric records and handed to .NET in batches: one call
// tells .NET that events are waiting, and .NET pulls them with takeEvents()/takeTexts(). This keeps
// the interop surface to numbers and strings, which F# can marshal without code generation.
//
// Record layout (7 numbers): kind, a, b, c, d, e, f. Kinds must match BrowserPlatform.fs.
// Modifiers are bit flags: 1 Shift, 2 Control, 4 Alt, 8 Meta (as Scribble.Platform.Modifiers).

const RECORD = 7;
const Kind = {
  Resized: 0,         // a = width px, b = height px
  ScaleChanged: 1,    // a = devicePixelRatio
  PointerMoved: 2,    // a = pointer kind, b = pointer id, c = x px, d = y px, f = modifiers
  PointerPressed: 3,  // ... e = button
  PointerReleased: 4, // ... e = button
  Wheel: 5,           // a = x px, b = y px, c = dx notches, d = dy notches, e = modifiers
  KeyDown: 6,         // a = index of the key name (e.key) in takeTexts(), b = repeat (0/1), c = modifiers
  KeyUp: 7,           // a = index of the key name in takeTexts(), c = modifiers
  TextInput: 8,       // a = index into takeTexts()
  Focus: 9,           // a = 1 focused / 0 blurred
  PointerExited: 10,  // a = pointer kind, b = pointer id
  PointerCanceled: 11 // a = pointer kind, b = pointer id
};

let events = [];
let texts = [];
let notifyEvents = null;
let notifyFrame = null;
let notifyScheduled = false;
let frameRequested = false;
let canvas = null;

function push(kind, a = 0, b = 0, c = 0, d = 0, e = 0, f = 0) {
  events.push(kind, a, b, c, d, e, f);
  if (!notifyScheduled) {
    notifyScheduled = true;
    // A microtask still runs before the browser paints, so a resize handled here can be drawn
    // in the same visual frame as the new canvas size.
    queueMicrotask(() => {
      notifyScheduled = false;
      if (notifyEvents) notifyEvents();
    });
  }
}

let notifyWake = null;
let wakeScheduled = false;

let notifyTimer = null;
let timer = null;

export function init(onEvents, onFrame, onWake, onTimer) {
  notifyEvents = onEvents;
  notifyFrame = onFrame;
  notifyWake = onWake;
  notifyTimer = onTimer;
}

// Delayed work (IPlatform.After): one timer, for the earliest; .NET keeps the queue and re-arms it.
export function setTimer(ms) {
  if (timer !== null) clearTimeout(timer);
  timer = ms < 0 ? null : setTimeout(() => {
    timer = null;
    if (notifyTimer) notifyTimer();
  }, ms);
}

// Scribble's dispatcher has work queued: drain it soon (a macrotask, so a burst of wakes and the
// promise continuations around them become one call).
export function wake() {
  if (wakeScheduled) return;
  wakeScheduled = true;
  setTimeout(() => {
    wakeScheduled = false;
    if (notifyWake) notifyWake();
  }, 0);
}

export function takeEvents() {
  const taken = events;
  events = [];
  return taken;
}

export function takeTexts() {
  const taken = texts;
  texts = [];
  return taken;
}

export function scale() {
  return window.devicePixelRatio || 1;
}

export function requestFrame() {
  if (frameRequested) return;
  frameRequested = true;
  requestAnimationFrame(() => {
    frameRequested = false;
    if (notifyFrame) notifyFrame();
  });
}

export function setTitle(title) {
  document.title = title;
}

// Key combinations the app uses itself, as "modifiers|key" with the key in lower case
// ("0|f12", "3|c" for Ctrl+Shift+C): the browser's own shortcut is prevented.
const claimed = new Set();

export function claimKey(id) {
  claimed.add(id);
}

// Scribble.Platform.Cursor, in declaration order.
const cursors = ["default", "pointer", "text", "crosshair", "move", "ew-resize", "ns-resize", "not-allowed", "wait", "nwse-resize"];

export function setCursor(index) {
  if (canvas) canvas.style.cursor = cursors[index] || "default";
}

// Image cursors (Cursor.Image): CSS values by id, made once per image and density.
const imageCursors = new Map();

// Premultiplied BGRA bytes (a .NET MemoryView, valid only during the call) as a PNG data URL.
function cursorPng(width, height, pixels) {
  const src = pixels.slice();
  const c = document.createElement("canvas");
  c.width = width;
  c.height = height;
  const ctx = c.getContext("2d");
  const image = ctx.createImageData(width, height);
  const d = image.data;
  for (let i = 0; i < src.length; i += 4) {
    const a = src[i + 3];
    if (a === 0) continue;
    d[i] = Math.min(255, Math.round((src[i + 2] * 255) / a));
    d[i + 1] = Math.min(255, Math.round((src[i + 1] * 255) / a));
    d[i + 2] = Math.min(255, Math.round((src[i] * 255) / a));
    d[i + 3] = a;
  }
  ctx.putImageData(image, 0, 0);
  return c.toDataURL("image/png");
}

// The image at the screen's density and at 1x (hotspots in their own pixels). Where the browser
// takes image-set in cursors, the sharp one is used (its hotspot in CSS pixels, the 1x one's);
// elsewhere the 1x image.
export function makeImageCursor(id, width, height, hotX, hotY, pixels, width1, height1, hotX1, hotY1, pixels1, density) {
  const one = `url("${cursorPng(width1, height1, pixels1)}")`;
  let css = `${one} ${hotX1} ${hotY1}, auto`;
  if (density !== 1) {
    const sharp = `image-set(url("${cursorPng(width, height, pixels)}") ${density}x, ${one} 1x) ${hotX1} ${hotY1}, auto`;
    if (CSS.supports("cursor", sharp)) css = sharp;
  }
  imageCursors.set(id, css);
}

// Forgets an image cursor the page no longer keeps (its data URLs go with it).
export function forgetImageCursor(id) {
  imageCursors.delete(id);
}

export function setImageCursor(id) {
  if (canvas) canvas.style.cursor = imageCursors.get(id) || "default";
}

function pointerKind(e) {
  return e.pointerType === "touch" ? 1 : e.pointerType === "pen" ? 2 : 0;
}

function modifiers(e) {
  return (e.shiftKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.altKey ? 4 : 0) | (e.metaKey ? 8 : 0);
}

function pointerArgs(e) {
  const dpr = window.devicePixelRatio || 1;
  return [pointerKind(e), e.pointerId, e.offsetX * dpr, e.offsetY * dpr];
}

function watchScale() {
  // Fires when the page moves to a screen with another density, or the user zooms.
  const query = matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
  query.addEventListener("change", () => {
    push(Kind.ScaleChanged, window.devicePixelRatio || 1);
    watchScale();
  }, { once: true });
}

// Creates the canvas that acts as the window. It fills the page; its backing store is sized in
// physical pixels (CSS size × devicePixelRatio) so drawing is crisp at any density.
export function createCanvas(id) {
  canvas = document.getElementById(id);
  if (!canvas) {
    canvas = document.createElement("canvas");
    canvas.id = id;
    document.body.appendChild(canvas);
  }
  // touch-action none: every touch comes to Scribble (no browser panning or zooming, no double-tap
  // zoom); overscroll-behavior none on the page: no pull-to-refresh or bounce.
  Object.assign(canvas.style, { position: "fixed", inset: "0", width: "100%", height: "100%", display: "block", touchAction: "none", outline: "none" });
  document.documentElement.style.overscrollBehavior = "none";
  document.body.style.overscrollBehavior = "none";
  canvas.tabIndex = 0;

  const dpr = window.devicePixelRatio || 1;
  canvas.width = Math.max(1, Math.round(canvas.clientWidth * dpr));
  canvas.height = Math.max(1, Math.round(canvas.clientHeight * dpr));

  const observer = new ResizeObserver((entries) => {
    const entry = entries[entries.length - 1];
    let width, height;
    if (entry.devicePixelContentBoxSize) {
      // Exact physical pixels, no rounding guesswork (Chromium, Firefox).
      width = entry.devicePixelContentBoxSize[0].inlineSize;
      height = entry.devicePixelContentBoxSize[0].blockSize;
    } else {
      const ratio = window.devicePixelRatio || 1;
      width = Math.round(entry.contentBoxSize[0].inlineSize * ratio);
      height = Math.round(entry.contentBoxSize[0].blockSize * ratio);
    }
    if (width !== canvas.width || height !== canvas.height) {
      canvas.width = width;
      canvas.height = height;
    }
    push(Kind.Resized, width, height);
  });
  try {
    observer.observe(canvas, { box: "device-pixel-content-box" });
  } catch {
    observer.observe(canvas, { box: "content-box" });
  }
  watchScale();

  // Every press ends with a release or a cancel. The browser can lose a pointerup (input that
  // arrives while the page is paused, as under a debugger, is dropped; capture can be taken away),
  // so the pointers seen pressed are tracked here, and one whose release went missing is canceled:
  // no click, since where the button came up isn't known.
  const down = new Set();
  const cancel = (e) => {
    if (down.delete(e.pointerId)) push(Kind.PointerCanceled, pointerKind(e), e.pointerId);
  };
  canvas.addEventListener("pointermove", (e) => {
    if (e.buttons === 0) cancel(e);
    push(Kind.PointerMoved, ...pointerArgs(e), 0, modifiers(e));
  });
  canvas.addEventListener("pointerdown", (e) => {
    cancel(e); // already pressed: its release was lost
    down.add(e.pointerId);
    // Capture: moves and the release keep arriving while the pointer is outside the canvas.
    canvas.setPointerCapture(e.pointerId);
    canvas.focus();
    push(Kind.PointerPressed, ...pointerArgs(e), e.button, modifiers(e));
  });
  canvas.addEventListener("pointerup", (e) => {
    if (!down.delete(e.pointerId)) return; // its press was canceled already
    push(Kind.PointerReleased, ...pointerArgs(e), e.button, modifiers(e));
  });
  // Normally after the pointerup (nothing left to do then); without one, the press is canceled.
  canvas.addEventListener("lostpointercapture", cancel);
  // Fires after the release when a captured drag ended outside, and after every touch.
  canvas.addEventListener("pointerleave", (e) => push(Kind.PointerExited, pointerKind(e), e.pointerId));
  canvas.addEventListener("pointercancel", (e) => {
    down.delete(e.pointerId);
    push(Kind.PointerCanceled, pointerKind(e), e.pointerId);
  });
  canvas.addEventListener("contextmenu", (e) => e.preventDefault());
  canvas.addEventListener("wheel", (e) => {
    e.preventDefault();
    const dpr = window.devicePixelRatio || 1;
    // Normalise to "notches", positive = away from the user, like Win32.
    const unit = e.deltaMode === 0 ? 100 : e.deltaMode === 1 ? 3 : 1;
    push(Kind.Wheel, e.offsetX * dpr, e.offsetY * dpr, e.deltaX / unit, -e.deltaY / unit, modifiers(e));
  }, { passive: false });

  window.addEventListener("keydown", (e) => {
    // The key's name as the layout gives it ("a", "A", "Enter", "ArrowLeft"); .NET maps it to Key.
    texts.push(e.key);
    push(Kind.KeyDown, texts.length - 1, e.repeat ? 1 : 0, modifiers(e));
    const isChar = [...e.key].length === 1;
    if (isChar && !e.ctrlKey && !e.metaKey && !e.altKey) {
      texts.push(e.key);
      push(Kind.TextInput, texts.length - 1);
    }
    // Keep Space, arrows and Tab for the app instead of scrolling the page.
    if ([" ", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Tab"].includes(e.key)) e.preventDefault();
    if (claimed.has(modifiers(e) + "|" + e.key.toLowerCase())) e.preventDefault();
  });
  window.addEventListener("keyup", (e) => {
    texts.push(e.key);
    push(Kind.KeyUp, texts.length - 1, 0, modifiers(e));
  });
  window.addEventListener("focus", () => push(Kind.Focus, 1));
  window.addEventListener("blur", () => push(Kind.Focus, 0));

  return [canvas.width, canvas.height];
}

export function removeCanvas() {
  if (canvas) canvas.remove();
  canvas = null;
}

//# sourceURL=scribble-platform.js
