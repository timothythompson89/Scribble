// Starts the .NET runtime and runs Program.main. runMain keeps the runtime alive afterwards, so
// the browser can keep calling into Scribble for events and frames.
//
// Errors are also shown on the page (not only in the console), since phones and tablets have no
// developer console at hand: otherwise a failed start is just a black page.

function showError(text) {
  let box = document.getElementById("scribble-errors");
  if (!box) {
    box = document.createElement("pre");
    box.id = "scribble-errors";
    box.style.cssText =
      "position:fixed;left:0;right:0;bottom:0;max-height:60%;overflow:auto;margin:0;padding:12px;" +
      "background:#300;color:#fcc;font:13px/1.4 monospace;white-space:pre-wrap;z-index:1000";
    document.body.appendChild(box);
  }
  box.textContent += text + "\n";
}

// A page that can't run here says so in plain words, in the middle of the page: not an error of the app's, so
// not the red box. `detail` is for whoever looks into it (small, below).
function showNotice(heading, text, detail) {
  const box = document.createElement("div");
  box.id = "scribble-notice";
  box.setAttribute("role", "alert");
  box.style.cssText =
    "position:fixed;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;" +
    "padding:24px;box-sizing:border-box;text-align:center;background:#1a1a24;color:#e8e8f0;" +
    "font:16px/1.5 system-ui,-apple-system,'Segoe UI',sans-serif";
  const h = document.createElement("h1");
  h.textContent = heading;
  h.style.cssText = "margin:0 0 12px;font-size:22px;font-weight:600";
  const p = document.createElement("p");
  p.textContent = text;
  p.style.cssText = "margin:0;max-width:34em";
  const d = document.createElement("p");
  d.textContent = detail;
  d.style.cssText = "margin:20px 0 0;max-width:60em;font-size:12px;color:#8a8aa0;word-break:break-word";
  box.append(h, p, d);
  document.body.appendChild(box);
}

const consoleError = console.error.bind(console);
console.error = (...args) => {
  consoleError(...args);
  showError(args.map((a) => (a instanceof Error ? a.stack || a.message : String(a))).join(" "));
};
window.addEventListener("error", (e) => showError("Error: " + e.message));
window.addEventListener("unhandledrejection", (e) => showError("Unhandled: " + (e.reason?.stack || e.reason)));

if (!window.isSecureContext) {
  // WebGPU is only offered to secure pages: the runtime would start and then find nothing to draw with.
  showNotice(
    "This page needs HTTPS",
    "It draws with WebGPU, which browsers offer only to pages opened over https:// or on localhost.",
    location.href);
} else if (!navigator.gpu) {
  // Nothing is downloaded or started: the app could not draw.
  showNotice(
    "This page needs WebGPU",
    "This browser doesn't offer it, so the page can't draw. Try a current version of Chrome or Edge.",
    navigator.userAgent);
} else {
  try {
    // Imported here, not at the top: where the page can't run, the runtime isn't fetched.
    const { dotnet } = await import("./_framework/dotnet.js");
    const { runMain } = await dotnet.withApplicationArgumentsFromQuery().create();
    await runMain();
  } catch (e) {
    showError("Start failed: " + (e?.stack || e));
  }
}
