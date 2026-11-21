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
  PointerCanceled: 11, // a = pointer kind, b = pointer id
  ClipboardText: 12,  // a = the read's id, b = index of the text in takeTexts() (-1: refused)
  TextComposition: 13, // a = index of the composing text in takeTexts() ("" ends it), b = the caret in it
  SystemTheme: 14     // the system's dark or light setting changed (prefersDark() says which it is now)
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

// The page's address, for images with relative URLs (Image.fromUrl).
export function pageUrl() {
  return document.baseURI;
}

// Whether the system (or the browser) is set to dark: 1 dark, 0 light, -1 when the browser can't say.
export function prefersDark() {
  if (typeof matchMedia !== "function") return -1;
  if (matchMedia("(prefers-color-scheme: dark)").matches) return 1;
  return matchMedia("(prefers-color-scheme: light)").matches ? 0 : -1;
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

// The clipboard (DESIGN.md, "9b: clipboard and undo", decisions 22 and 23). Writing works while a key or a press
// is being handled. Reading through navigator.clipboard asks the person for permission, so after Ctrl+V or
// Shift+Insert the text is taken from the browser's own paste event, which needs none.
const never = -1e9;
let pasteKeyAt = never;   // when a paste key went down whose paste event hasn't come yet
let pasteWaiting = [];    // reads waiting for that event
let pasted = null;        // { text, at }: a paste event no read has taken yet

function answerClipboard(id, text) {
  texts.push(text);
  push(Kind.ClipboardText, id, texts.length - 1);
}

function readSystemClipboard(id) {
  const refused = () => push(Kind.ClipboardText, id, -1);
  try {
    navigator.clipboard.readText().then((text) => answerClipboard(id, text), refused);
  } catch {
    refused(); // no clipboard API (an insecure page)
  }
}

export function readClipboard(id) {
  const now = performance.now();
  if (pasted && now - pasted.at < 1000) {
    const text = pasted.text;
    pasted = null;
    answerClipboard(id, text);
  } else if (now - pasteKeyAt < 1000) {
    pasteWaiting.push(id);
    // No paste event came (the browser sent none for this key): ask the other way.
    setTimeout(() => {
      const i = pasteWaiting.indexOf(id);
      if (i >= 0) {
        pasteWaiting.splice(i, 1);
        readSystemClipboard(id);
      }
    }, 500);
  } else {
    readSystemClipboard(id);
  }
}

export function writeClipboard(text) {
  try {
    navigator.clipboard.writeText(text).catch(() => {});
  } catch {
    // no clipboard API (an insecure page)
  }
}

// Text input (DESIGN.md, "9d", decisions 50 and 53). While the focused element takes typed text, a hidden text
// field has the DOM focus: a phone shows its keyboard for it, and input methods compose into it. What it is given
// comes to .NET as TextInput and TextComposition, and it is emptied again: Scribble keeps the text.
//
// Found on a phone (the 9d spike, Android): the field can be focused from the microtask after a pointer event,
// as .NET does, but the canvas takes the focus straight back when the touch's mousedown arrives, unless that is
// prevented. Each character comes as an `input` event (its keydown says "Unidentified"); Backspace and Enter
// still come as ordinary key events, with the field empty too.
let field = null;
let textOn = false;
let composing = false;
let dropComposed = false; // the composition is being dropped from here: its end commits nothing
// Scribble.Platform.KeyboardKind, in declaration order.
const inputModes = ["text", "numeric", "decimal", "email", "url", "tel", "search", "text"];

function pushText(kind, text, b = 0) {
  texts.push(text);
  push(kind, texts.length - 1, b);
}

function ensureField() {
  if (field) return;
  field = document.createElement("textarea");
  // There, focusable and never seen. 16px: an iPhone zooms the page for smaller text.
  Object.assign(field.style, {
    position: "fixed", left: "0", top: "0", width: "2px", height: "20px", opacity: "0", fontSize: "16px",
    border: "0", padding: "0", margin: "0", resize: "none", outline: "none", background: "transparent",
    color: "transparent", caretColor: "transparent", pointerEvents: "none", overflow: "hidden", zIndex: "-1"
  });
  // Autocorrect and suggestions rewrite words already typed, which this empty field doesn't hold (decision 53).
  field.autocapitalize = "off";
  field.autocomplete = "off";
  field.spellcheck = false;
  field.setAttribute("autocorrect", "off");
  field.setAttribute("aria-hidden", "true");
  field.tabIndex = -1;
  document.body.appendChild(field);

  field.addEventListener("compositionstart", () => { composing = true; });
  field.addEventListener("compositionupdate", (e) => {
    if (!dropComposed) pushText(Kind.TextComposition, e.data || "", (e.data || "").length);
  });
  field.addEventListener("compositionend", (e) => {
    composing = false;
    if (!dropComposed) {
      pushText(Kind.TextComposition, "", 0);
      if (e.data) pushText(Kind.TextInput, e.data);
    }
    field.value = "";
  });
  field.addEventListener("input", (e) => {
    // What a composition commits comes with its end; a paste comes from the paste event; Enter as a key.
    if (composing || e.isComposing || dropComposed) return;
    if ((e.inputType === "insertText" || e.inputType === "insertReplacementText") && e.data) pushText(Kind.TextInput, e.data);
    field.value = "";
  });
  // Something else took the focus while text is still wanted (a long press gives it to the canvas: no mousedown
  // comes to prevent, as after a tap): the field takes it back at once, before the phone's keyboard goes down and
  // comes up again (DESIGN.md, "9g"). Not when the blur is ours, nor when the whole page lost the focus.
  field.addEventListener("blur", () => {
    if (textOn && !dropComposed && document.hasFocus()) field.focus({ preventScroll: true });
  });
}

// The page behind the canvas takes the app's background colour. A phone's browser can show a strip of the page
// below the canvas: while its keyboard is up, Edge on Android shows about 29 px more of the page than any of its
// sizes say (its bottom bar shrinks; found with the 9d viewport spike). In the app's colour the strip is just
// more background.
export function setBackground(css) {
  document.documentElement.style.background = css;
  document.body.style.background = css;
  const meta = document.querySelector('meta[name="theme-color"]') || document.head.appendChild(Object.assign(document.createElement("meta"), { name: "theme-color" }));
  meta.content = css;
}

// Fits the canvas to what shows of the page (decision 58).
//
// Its height can't be left at 100%: on a phone that is the page's height with the browser's bars showing, and when
// a bar hides (as one does while the keyboard is up) the page gets taller and a strip of it shows below the
// canvas. An element fixed to the bottom does follow the real edge, so the canvas ends where `bottomProbe` is.
// Where the keyboard covers the page instead of resizing it (no `interactive-widget=resizes-content`), the
// visual viewport says what is left: used while text is on and it is clearly shorter (a keyboard, not a zoom).
let bottomProbe = null;
function fitToKeyboard() {
  if (!canvas) return;
  if (!bottomProbe) {
    bottomProbe = document.createElement("div");
    Object.assign(bottomProbe.style, { position: "fixed", left: "0", bottom: "0", width: "1px", height: "0", visibility: "hidden", pointerEvents: "none" });
    document.body.appendChild(bottomProbe);
  }
  let top = 0;
  let bottom = bottomProbe.getBoundingClientRect().top;
  const view = window.visualViewport;
  if (view && textOn && bottom - (view.offsetTop + view.height) > 100) {
    top = view.offsetTop;
    bottom = view.offsetTop + view.height;
  }
  const height = Math.max(1, bottom - top) + "px";
  if (canvas.style.height !== height || canvas.style.top !== top + "px") {
    Object.assign(canvas.style, { top: top + "px", bottom: "auto", height });
  }
}

// on: 1 or 0. x, y, height: the caret's box in physical pixels. kind: Scribble.Platform.KeyboardKind's index.
export function setTextInput(on, x, y, height, kind, multiline) {
  if (on) {
    ensureField();
    const dpr = window.devicePixelRatio || 1;
    Object.assign(field.style, { left: Math.max(0, x / dpr) + "px", top: Math.max(0, y / dpr) + "px", height: Math.max(1, height / dpr) + "px" });
    const mode = inputModes[kind] || "text";
    if (field.inputMode !== mode) field.inputMode = mode;
    field.enterKeyHint = multiline ? "enter" : kind === 6 ? "search" : "done";
    textOn = true;
    if (document.activeElement !== field) field.focus({ preventScroll: true });
  } else if (textOn) {
    textOn = false;
    if (field) {
      dropComposed = true;
      field.blur();
      field.value = "";
      dropComposed = false;
      composing = false;
    }
    if (canvas) canvas.focus({ preventScroll: true });
  }
  fitToKeyboard();
}

// Drops what is being composed, committing nothing (the app took the text itself). Text input stays on.
export function clearComposition() {
  if (!field || !composing) return;
  dropComposed = true;
  field.blur();
  field.value = "";
  composing = false;
  if (textOn) field.focus({ preventScroll: true });
  dropComposed = false;
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
  if (typeof matchMedia === "function") {
    matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => push(Kind.SystemTheme));
  }

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
    // While text is being typed the hidden field keeps the focus (and a phone its keyboard).
    if (!textOn) canvas.focus();
    push(Kind.PointerPressed, ...pointerArgs(e), e.button, modifiers(e));
  });
  // The mousedown that follows a press would give the canvas the focus: not while the hidden field has it. (.NET
  // has heard of the press by now, in the microtask after pointerdown, and turned text input on if it is wanted.)
  canvas.addEventListener("mousedown", (e) => { if (textOn) e.preventDefault(); });
  // A long press gives the canvas the focus with no mousedown to prevent (a phone, DESIGN.md "9g"): while text is
  // wanted it goes straight back to the hidden field (whose own blur listener does the same, where that is enough).
  canvas.addEventListener("focus", () => { if (textOn && field) field.focus({ preventScroll: true }); });
  // The browser's own bars come and go (with the keyboard, with scrolling), and each says so its own way.
  window.addEventListener("resize", fitToKeyboard);
  if (window.visualViewport) {
    window.visualViewport.addEventListener("resize", fitToKeyboard);
    window.visualViewport.addEventListener("scroll", fitToKeyboard);
  }
  fitToKeyboard();
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
    // A key an input method takes (it composes with it; a phone's keyboard sends these for every character):
    // what comes of it arrives through the hidden field (decision 55).
    if (e.isComposing || e.keyCode === 229) return;
    const typing = textOn && document.activeElement === field;
    // The key's name as the layout gives it ("a", "A", "Enter", "ArrowLeft"); .NET maps it to Key.
    texts.push(e.key);
    push(Kind.KeyDown, texts.length - 1, e.repeat ? 1 : 0, modifiers(e));
    const isChar = [...e.key].length === 1;
    // While the hidden field has the focus, typed text comes from its input events.
    if (isChar && !typing && !e.ctrlKey && !e.metaKey && !e.altKey) {
      texts.push(e.key);
      push(Kind.TextInput, texts.length - 1);
    }
    // Keep Space, arrows and Tab for the app instead of scrolling the page. (Space into the hidden field is
    // typing: preventing it would lose the character.)
    if (["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Tab"].includes(e.key) || (e.key === " " && !typing)) e.preventDefault();
    if (claimed.has(modifiers(e) + "|" + e.key.toLowerCase())) e.preventDefault();
    // A paste key: the browser's paste event follows (it is not prevented), and answers the read.
    const key = e.key.toLowerCase();
    if ((key === "v" && (e.ctrlKey || e.metaKey) && !e.altKey) || (key === "insert" && e.shiftKey && !e.ctrlKey)) {
      pasteKeyAt = performance.now();
    }
  });
  window.addEventListener("paste", (e) => {
    const text = e.clipboardData ? e.clipboardData.getData("text/plain") : "";
    e.preventDefault();
    pasteKeyAt = never;
    const waiting = pasteWaiting;
    pasteWaiting = [];
    if (waiting.length === 0) pasted = { text, at: performance.now() };
    for (const id of waiting) answerClipboard(id, text);
  });
  window.addEventListener("keyup", (e) => {
    if (e.isComposing || e.keyCode === 229) return;
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


// Saves `bytes` (a .NET MemoryView, copied here) as a download named `name` (DESIGN.md, "Rendering to an image and
// saving it", decision 7). The browser decides where it goes, or asks.
export function saveFile(name, bytes) {
  const blob = new Blob([bytes.slice()], { type: name.toLowerCase().endsWith(".png") ? "image/png" : "application/octet-stream" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  a.style.display = "none";
  document.body.appendChild(a);
  a.click();
  a.remove();
  // Revoked once the click has started the download.
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}

//# sourceURL=scribble-platform.js

// Stored values (DESIGN.md, "Stored values"): the page's localStorage, a key per value. Storage that is turned
// off or full (a private window, a blocked site) reads as nothing stored and keeps nothing: the app runs on its
// defaults. The answer starts with "1" when the key holds text, and is "0" when it holds none.
export function readStored(key) {
  try {
    const text = globalThis.localStorage.getItem(key);
    return text === null ? "0" : "1" + text;
  } catch {
    return "0";
  }
}

// key and text in one string, parted by the first "\n" (a key has none).
export function writeStored(keyAndText) {
  try {
    const at = keyAndText.indexOf("\n");
    globalThis.localStorage.setItem(keyAndText.slice(0, at), keyAndText.slice(at + 1));
  } catch {
    // not kept
  }
}
