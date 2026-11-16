// Starts the .NET runtime and runs Program.main. runMain keeps the runtime alive afterwards, so
// the browser can keep calling into Scribble for events and frames.
//
// Errors are also shown on the page (not only in the console), since phones and tablets have no
// developer console at hand: otherwise a failed start is just a black page.
import { dotnet } from "./_framework/dotnet.js";

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

const consoleError = console.error.bind(console);
console.error = (...args) => {
  consoleError(...args);
  showError(args.map((a) => (a instanceof Error ? a.stack || a.message : String(a))).join(" "));
};
window.addEventListener("error", (e) => showError("Error: " + e.message));
window.addEventListener("unhandledrejection", (e) => showError("Unhandled: " + (e.reason?.stack || e.reason)));

if (!window.isSecureContext) showError("Not a secure page (WebGPU needs HTTPS or localhost).");
if (!navigator.gpu) showError("This browser has no WebGPU (navigator.gpu is missing). " + navigator.userAgent);

try {
  const { runMain } = await dotnet.withApplicationArgumentsFromQuery().create();
  await runMain();
} catch (e) {
  showError("Start failed: " + (e?.stack || e));
}
