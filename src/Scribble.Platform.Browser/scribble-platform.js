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

export function init(onEvents, onFrame) {
  notifyEvents = onEvents;
  notifyFrame = onFrame;
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

// Scribble.Platform.Cursor, in declaration order.
const cursors = ["default", "pointer", "text", "crosshair", "move", "ew-resize", "ns-resize", "not-allowed", "wait"];

export function setCursor(index) {
  if (canvas) canvas.style.cursor = cursors[index] || "default";
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
  Object.assign(canvas.style, { position: "fixed", inset: "0", width: "100%", height: "100%", display: "block", touchAction: "none", outline: "none" });
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

  canvas.addEventListener("pointermove", (e) => push(Kind.PointerMoved, ...pointerArgs(e), 0, modifiers(e)));
  canvas.addEventListener("pointerdown", (e) => {
    // Capture: moves and the release keep arriving while the pointer is outside the canvas.
    canvas.setPointerCapture(e.pointerId);
    canvas.focus();
    push(Kind.PointerPressed, ...pointerArgs(e), e.button, modifiers(e));
  });
  canvas.addEventListener("pointerup", (e) => push(Kind.PointerReleased, ...pointerArgs(e), e.button, modifiers(e)));
  // Fires after the release when a captured drag ended outside, and after every touch.
  canvas.addEventListener("pointerleave", (e) => push(Kind.PointerExited, pointerKind(e), e.pointerId));
  canvas.addEventListener("pointercancel", (e) => push(Kind.PointerCanceled, pointerKind(e), e.pointerId));
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
