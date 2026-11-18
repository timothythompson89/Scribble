# Scribble

A UI framework for F#, written from the GPU up. [`docs/DESIGN.md`](docs/DESIGN.md) has the goals, architecture,
roadmap and every decision; [`GLOSSARY.md`](GLOSSARY.md) the words we use (view, element, setting, theme value, ...).

**Status** (2026-11-17): milestones 0–7b done; M8 (core controls) under way: scrolling and virtualized lists, Grid,
DockPanel, Canvas, WrapPanel, toggles, CheckBox, Switch, Slider, TabControl, ListBox, RadioButton. Apps are F# view
functions: `App.run` starts one, on the desktop or in the browser; views are built from settings lists
(`StackPanel.create [ ... ]`), and on each update Scribble diffs the new view against the last one and patches only
what changed. There are pointer and keyboard input with focus, components with hooks, async, layers, a
ScrollViewer and virtualized ItemsView, an inspector, styles, light and dark themes, app values, semantics for
assistive technology, flyouts and tooltips, images, icons (with the
Material Symbols package) and icon cursors. Underneath: WPF-style layout (measure/arrange, lengths in px, % and star), our own text stack
(TrueType parser, rasterizer, HarfBuzz shaping, emoji fallback), and a GPU draw list rendered in one draw call per
frame, on Windows and in the browser. Scribble draws only when something asks for a frame.

## Your first app

```fsharp
open Scribble.Core

type Msg = Increment | Decrement

let update msg count =
    match msg with
    | Increment -> count + 1
    | Decrement -> count - 1

let view count dispatch =
    StackPanel.create [
        StackPanel.margin 24.0
        StackPanel.spacing 16.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text $"Count: {count}"; TextBlock.fontSize 28.0 ]
            StackPanel.create [
                StackPanel.orientation Horizontal
                StackPanel.spacing 8.0
                StackPanel.children [
                    Button.create [ Button.text "−"; Button.onClick (fun _ -> dispatch Decrement) ]
                    Button.create [ Button.text "+"; Button.onClick (fun _ -> dispatch Increment) ]
                ]
            ]
        ]
    ]

[<EntryPoint>]
let main _ = App.run [ App.title "Counter"; App.program 0 update view ]
```

Text and controls take their colours from the theme (dark by default; `App.theme (Theme.light [])` for light), so
the app sets none.

The same code runs on the desktop and in the browser: only the project file differs, in its SDK and the one
Scribble package it references (which brings the platform, the GPU renderer and text shaping along). The
packages aren't published yet: until they are, reference the projects instead
(`<ProjectReference Include="path/to/Scribble/src/Scribble.Desktop/Scribble.Desktop.fsproj" />`), as the samples do.

```xml
<!-- Desktop -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><Compile Include="Program.fs" /></ItemGroup>
  <ItemGroup><PackageReference Include="Scribble.Desktop" /></ItemGroup>
</Project>

<!-- Browser (WebAssembly): no web files needed, Scribble.Browser brings the page -->
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><Compile Include="Program.fs" /></ItemGroup>
  <ItemGroup><PackageReference Include="Scribble.Browser" /></ItemGroup>
</Project>
```

On the desktop `App.run` returns when the window closes; in the browser it starts the app and returns at once (the
page keeps it running). A browser app that wants its own page adds `wwwroot/index.html` and `main.js`, and
Scribble's are left out. For an app on both, put the app in a library and add a small host project per platform,
as the samples do (`samples/Counter` is the app above, `samples/Counter.Desktop` and `samples/Counter.Browser` its
hosts, with the same one-line Program.fs; `dotnet run --project samples/Counter.Desktop` runs it). A grown-up version
(with a command) opens the Gallery's Components page, `samples/Gallery/ComponentsPage.fs`.

## Requirements

- Windows 10 1703 or later (development platform for now)
- .NET 10 SDK
- A GPU with Direct3D 12 or Vulkan drivers

## Run the sample

```powershell
dotnet run --project samples/Hello.Desktop
```

The first build downloads wgpu-native (pinned to `v29.0.1.1`) into `native/` (git-ignored) and copies
`wgpu_native.dll` next to the executable. If the download fails (offline, proxy), download
`wgpu-windows-x86_64-msvc-release.zip` from the
[wgpu-native v29.0.1.1 release](https://github.com/gfx-rs/wgpu-native/releases/tag/v29.0.1.1)
and extract it to `native/wgpu-native-v29.0.1.1/win-x64/`, so that `lib/wgpu_native.dll` sits inside it.
It also downloads SDL3 (pinned to 3.4.20, the desktop platform) and copies `SDL3.dll` next to the executable. If that
fails, extract `SDL3-3.4.20-win32-x64.zip` from the
[SDL 3.4.20 release](https://github.com/libsdl-org/SDL/releases/tag/release-3.4.20) to `native/SDL3-3.4.20/win-x64/`.

Set `SCRIBBLE_GPU_BACKEND` to `d3d12` or `vulkan` to force a backend (default on Windows: Direct3D 12, then
Vulkan; elsewhere Vulkan or Metal; OpenGL only when nothing else gives an adapter). On Windows, Direct3D 12
presents through Windows' compositor (DirectComposition), as browsers do: with an ordinary window swap chain,
G-SYNC monitors made the cursor stutter over Scribble windows, because the monitor then refreshed only when the
app drew. Vulkan on Windows still has that problem with G-SYNC.

### What to check

1. A dark dashboard of cards with emoji avatars, titles, subtitles and live percentages; the progress bars animate.
   Text should look crisp at every scaling, and the percentages stay right-aligned as they change. The dashboard is
   a view function of the animation time and the hovered card (`samples/Hello/Dashboard.fs`), rebuilt and diffed
   every frame; it should look exactly as before. The title bar shows frames per second, layout time and shapes.
2. Resize the window: the cards reflow into more or fewer columns and stay sharp.
3. Move the window between monitors with different scaling (or change Windows scaling): borders stay crisp,
   1 DIP lines stay one clean line, and the console prints `Scale changed to …`.
4. Hover a card: its border turns its accent color and its shadow grows.
5. Space pauses the animation: frames stop and CPU/GPU use drops to about zero. Moving the mouse onto another card
   still updates the hover (one frame per change); moving within a card draws nothing.
6. Escape or the close button exits cleanly, with no errors in the console.
7. The toolbar: Pause/Resume (the label follows Space too), Restart, Remove card / Add card (1 to 8 cards; the rest
   stay put) and Shuffle (cards are keyed, so each keeps its element when it moves).
8. Keyboard: Tab moves a blue ring through the toolbar buttons (Shift+Tab backwards, wrapping); Enter clicks the
   focused one, Space presses it while held and clicks on release (Escape first cancels). Clicking a button focuses
   it without a ring, so Space then presses *that* button; click the background to give Space back to the app.
9. The inspector (both samples, desktop and browser; M6 step 2): F12 shows a panel on the right with the element
   tree of every layer (the app first; types in blue, components in purple, `#names` in amber, a TextBlock's text
   in quotes, hidden elements dimmed). The app doesn't move or resize while it's open.
   - Click a triangle to fold or unfold a row; click a row to select it. Hovering a row highlights its element in
     the app: a blue box, any margin in orange, and a label with the type and size. The selected element gets only
     blue corner marks a little outside it (no fill, no label, sides clear), so its own looks and edges stay visible.
   - Ctrl+Shift+C (or the arrow button) starts pick mode: a crosshair, the element under the pointer is highlighted, and a
     click selects it (the tree opens down to it and scrolls to its row) without reaching the app. The wheel still
     scrolls the app while picking. Escape cancels.
   - The toolbar's buttons are icons; hovering one shows what it does in place of the title. The dock buttons
     move the panel; × or F12 hides it. The app keeps working everywhere the panel
     isn't; with the app animating, the tree keeps up (about 10 updates a second).
   - In the browser, F12 and Ctrl+Shift+C should open the inspector, not the browser's own developer tools, while
     the page has focus.
10. The panel's modes (step 2b):
    - Drag the title ("Inspector") to undock the panel: it floats and stays inside the window, even when the
      window shrinks. Drag the dots at its bottom-right corner to resize it; the dock buttons dock it again.
    - Docked, drag its inner edge (the cursor turns into a left-right arrow) to make it narrower or wider.
    - The minimize button (a bar) or a double-click on the title folds it to its title bar; the square restores it.
    - Desktop only: the pop-out button (a box with an arrow) moves it into its own window ("Inspector – <app>"); hovering rows still highlights in
      the app, pick mode still works there, and F12 works in both windows. Closing the pop-out (or its pop-in button) puts
      the panel back where it was; closing the app closes both. The browser has no pop-out button.
11. The properties (step 3): pick a card in the dashboard (or click a row).
    - Below the tree: the element's type and #name, its states, layout numbers and properties, each tagged
      `view`, `code` or `type`. Hover the card and press on it: "hovered" and "pressed" appear (within a
      tenth of a second). Tick "Show defaults" for the common properties at their defaults (dimmed).
    - Drag the divider between the tree and the properties up and down.
    - In the Gallery, pick a component row (purple, e.g. ElmishCounter): its hooks and their values, and the
      model changing as you click "+".
    - The footer shows the last frame's layout/render times, shapes and the app's frames per second; when the
      app stops changing it shows "app idle" about a second later, and nothing redraws after that.

The Gallery (`dotnet run --project samples/Gallery.Desktop`) shows everything, one
page per tab (Components, Layout, Controls, Scrolling, Layers, Styles, Images), each opening
with its title and a line on what it shows, in a 720 × 560 window (a page scrolls when it doesn't fit; Scrolling instead fills the window, its list taking the height left). Every visited page stays in the tree and only the
selected one has `isVisible true`, so each page keeps its state while hidden.

1. Change something on each page (a count, the number in the load, the list), switch away and back: it's all still
   there. The selected tab is blue; the others look and react like normal buttons. Tab (the key) only reaches the
   visible page's buttons. Visit Components more than a second after another page: its first result is already there (hidden
   pages keep running).
2. Components, Elmish: the Elmish counter inside a component (`useElmishWithCmd`): the buttons change the count (a
   press and a release on the same button counts as a click), Reset is disabled (faded, no hand cursor) at zero.
   "+1 in a second" adds one a second later (`Cmd.ofAsync`); press it several times quickly and each one lands.
   Try it while dragging or resizing the window too: the count should still go up during the drag.
3. Components, useAsync: shows "loading…" for a second, then `1² = 1`. Press − or + quickly several times: it stays on
   "loading…" and only the last number's result appears (older loads are cancelled). Negative numbers work.
4. Components, useState and useAnimated: a list of counters, each with its own count (`useState`); the bar slides to the new count (0 to 10,
   red when negative) over a quarter second (`useAnimated`). Add, Remove first and Reverse change the list: counts
   stay with their names (keyed). Clicking one counter re-runs only that counter.
5. Buttons sink slightly (97%) while pressed and spring back; their faint shadow flattens. Pressing with Space
   does the same. Text stays sharp while sinking.
6. Nothing redraws while you don't click; an animation draws until it arrives, then stops. A pending load or
   "+1 in a second" draws one frame when it finishes, not continuously while waiting.
7. Tab, Enter and Space work as in the Hello sample.
8. The Scrolling page (10,000 messages in an ItemsView, M8 step 1) and the Layers page (layers, M6):
   - 10,000 messages of different lengths scroll smoothly (wheel notches glide; a touchpad follows directly); only
     those on screen exist (the inspector says "realized N of 10,000"). "Jump to #1", "#5,000", "#9,000" and
     "Random" put that message at the top; "Add message" adds one at the end and scrolls to it. The bar: drag the
     thumb, click the track to page; it widens under the pointer. After a click in the list, arrows, PageUp/Down
     and Home/End scroll it.
   - "Show a toast" puts a blue toast at the bottom right for 2.5 seconds; several stack up. The app keeps
     working everywhere except on the toast itself (a toast over the list or a button blocks only its own area).
   - "Open a modal layer" dims the window and shows a dialog. Nothing below reacts: no hover, no clicks, no wheel,
     and Tab cycles only through the dialog's two buttons (the app's focus ring goes away and comes back after).
     "Toast" in the dialog shows a toast under the dimming; Close brings the app back exactly as it was.
   - Hover a button, then open the dialog without moving the mouse: the button stops looking hovered.
9. The Styles page (M7 step 1, verified on Windows and in the browser 2026-11-16):
   - Primary is blue and Danger red (classes); the selected tab's blue comes from a style too.
   - Hover the card: its border and all three lines of text turn light blue (`Style.on States.hover` on the card,
     and text matched by `Style.inside Border.Type [ Style.cls "card"; States.hover ]`); the line below the card stays
     grey. Leave it: they go back.
   - "Hover me" lights up under the pointer (an inline `Border.on States.hover` block).
   - The chips at the bottom (Fast, Quiet, Cheap): Fast starts blue with white text. Clicking a chip toggles it
     (a `selected` state made with `States.ofProp` on the chip, and `Style.prop selected true` on its text through
     `Style.inside`); hovering a chip lightens it, selected or not. In the inspector, pick a selected chip: its
     states line ends with `selected`, and its `background` is "from Border.chip:selected". Unselect it and
     `selected` goes away.
   - "Compact buttons" swaps the page's style list: every button on the page gets smaller padding; again to undo.
   - "Disable Danger" fades it (the theme's disabled look); hover and click no longer work on it.
   - With the inspector (F12): pick Primary: its `classes` reads `.primary`, and its `background` shows `style`,
     "from Button.primary". Pick a line of the card's text: `foreground` comes "from Border.card TextBlock"; hover
     the card and it changes to "from Border.card:hover TextBlock".
   - To clear the inspector's selection, click "Deselect" next to the element's name, or click its tree row again:
     the panel shows its "Click a row, or Pick…" hint.
10. Themes (M7 step 2a, verified; the looks now come from `Theme.dark`, the default):
   - At rest, everything looks as before, in both samples and the inspector.
   - Hovering a button now **fades** its tint in and out (100 ms) instead of switching at once; the tint is a touch
     softer on the border. Pressing still sinks it to 97% with a flattening shadow (now a style with transitions);
     holding Space does the same. Primary and Danger keep their colors and get the same hover and press.
   - Disabled buttons (Reset at zero, "Disable Danger") fade as before; their label is barely lighter (50%).
   - Moving between buttons quickly draws only while a fade runs, then stops (the frame counter in the title goes
     idle).
11. Light and dark (M7 step 2b, verified): the Gallery has a "Light" switch at the right above the tab row.
   - Light: a light grey window (the window's background follows the theme), white buttons with grey borders, dark
     text; the selected tab, Primary and Danger keep white text. Hover darkens a button slightly, press darkens it
     more and sinks it; disabled buttons fade.
   - Every page follows: the counters' bars, the Scrolling page's rows, scrollbar, toast and dialog (open it after
     switching), the Styles page's cards and chips. "Dark" switches back to exactly the old look (the count and
     list text is now the theme's text color, a hair softer than white).
   - The inspector stays dark either way.
   - Inspector: pick a button and hover it: `overlay` shows `theme`, "from Button:hover"; `background` is `theme`,
     "from Button" (Primary's: `style`, "from Button.primary": the app's own style). Pick a button's label: `foreground` shows
     `inherited`. Tab focus rings and the Scrolling page's scrollbar look as before.
   - Inspector: a button at rest still lists what the theme can set, without "Show defaults": `overlay`
     (transparent, `default`), `scale`, `opacity`, `shadow`, with no line under them; hover it and `overlay` gets
     `theme`, "from Button:hover". Plain defaults like `margin` stay behind "Show defaults".
   - Forcing states: pick a button; the STATES section shows toggles (hover, pressed, focus, focus-visible, disabled,
     and every custom state, such as `selected`).
     Click "hover": it turns teal, the button looks hovered with the mouse
     elsewhere, the tree marks it "• forced" and its `overlay` shows `theme`, "from Button:hover"; scroll the panel freely.
     "pressed" sinks it, "focus-visible" draws the ring, "disabled" fades it, yet the button still clicks. Click a
     toggle again to stop forcing. A state that's on for real is forced off instead (a teal outline, the name struck
     through): a ticked CheckBox's "checked" shows it unticked. "Clear forced" (next to Deselect) stops them all, and so does closing the
     inspector (F12). On the Styles page, forcing `selected` on an unselected chip makes it look selected.
12. The title (an app value, `WindowTitle.current`; verified): the window, or the browser tab, is called "Gallery – Components" at
   the start and names whichever tab you select. A popped-out inspector ("Inspector – Gallery – …") follows it.
13. Icons: the Light/Dark switch shows Material's `light_mode` (or, in light, `dark_mode`) before its text, in the
   button's text color: light grey in dark, dark in light; it fades with the button when hovered or pressed like the
   text. Inspector: pick the switch; its `icon` property shows `light_mode` or `dark_mode`. (Hello's toolbar buttons
   have Material icons too.)
14. Layout (M8 step 2, not yet checked): the Layout page shows a form on a Grid (the fields' column keeps at least
   120 DIPs; the notes span two columns), an app shell on a DockPanel with Separators (one dashed), boxes to drag
   on a Canvas (a press brings one to the front and gives it a dashed outline) and chips in a WrapPanel (a new row
   after Elm; dotted and dashed chips). Inspector: select the form's Grid to see its lines (dashed) and gaps
   (shaded); pick a field: `Grid.row`/`Grid.column` are listed as written.
15. Controls (M8 step 3a, not yet checked): the tabs are a TabControl (an underline slides to the selected tab;
   ←/→ on a focused tab switch; on a phone the tab row scrolls sideways) and Light/Dark is a Switch. The Controls
   page has CheckBoxes ("All toppings" shows a dash while some are picked), Switches, a ToggleButton and Sliders
   (controlled and own-value side by side, a continuous one, three vertical ones).
16. Lists (M8 step 4b, not yet checked): a ListBox of 10,000 items; the radio buttons above it pick Single, Multiple
   or None. Click, Ctrl+click, Shift+click, Ctrl+A; Tab in and use ↑/↓, Home/End, PageUp/PageDown; double-click or
   Enter invokes (the line above the list says what). "Reverse order" keeps the selection. Below, a list of colour
   swatches with its own view per item.
The browser version is `samples/Gallery.Browser` (port 5281); the Counter's is `samples/Counter.Browser` (port 5283).

The icon gallery (`dotnet run --project samples/Icons.Desktop`; browser: `samples/Icons.Browser`,
port 5282) shows all 4,304 Material Symbols in an ItemsView (`ItemsLayout.wrap`: only the tiles on screen exist):

1. Outlined/Filled switches every icon. The search field (click it, then type; Backspace, Escape clears) scrolls
   to the first icon whose name starts with (else contains) what you typed and outlines it. It's a stand-in that
   takes keys until M9's TextBox, so it doesn't open a phone's keyboard.
2. A click on a tile shows the F# to write for it above the grid: `Material.arrowUpward`, with `.filled` when
   filled, ``Material.``class`` `` for keywords, `Material._10k` for a leading digit.
3. Tiles light up under the pointer, and the pointer becomes that tile's icon (white with a black outline,
   pointing at its center): a quick way to see how any icon works as a cursor. Scrolling (wheel, a finger on a
   phone) is smooth.

## Run the browser sample

Needs a browser with WebGPU (current Chrome or Edge; Firefox 141+ on Windows).

```powershell
dotnet run --project samples/Hello.Browser
```

This builds the app to WebAssembly, serves it on http://localhost:5280 and opens the browser.
Output from `Console.WriteLine` appears in the browser's developer console (F12). It should look the same as the
desktop sample.

If `dotnet run` can't serve it, publish and use any static file server, for example:

```powershell
dotnet publish samples/Hello.Browser -c Release
dotnet tool install -g dotnet-serve
dotnet serve -d samples/Hello.Browser/bin/Release/net10.0/publish/wwwroot -o
```

For a faster release build, compile to WebAssembly ahead of time (needs `dotnet workload install wasm-tools`
once; publishing takes minutes, and the download is larger):

```powershell
dotnet publish samples/Hello.Browser -c Release -p:BrowserAot=true
dotnet serve -d samples/Hello.Browser/bin/Release/net10.0/publish/wwwroot -o
```

### On other devices (phones, tablets, other computers)

WebGPU only runs on secure pages: `https://`, or `http://localhost` on the same machine. So another device needs
HTTPS with a certificate it trusts. `tools/Scribble.Serve` does this:

```powershell
dotnet run --project tools/Scribble.Serve                    # publish the samples, then serve them
dotnet run --project tools/Scribble.Serve -- --no-build      # serve what was published last
dotnet run --project tools/Scribble.Serve -- --aot           # WebAssembly AOT (publishing takes minutes)
dotnet run --project tools/Scribble.Serve -- --self-signed   # nothing to install; each device warns instead
```

It prints a QR code for **the hub**, `http://<this machine>:5279/` (plain HTTP, so a device can open it before it
trusts anything): scan it with the phone's camera, or type the address printed under it. The hub links the four
samples (Hello on port 5280, the Gallery on 5281, Icons on 5282, Counter on 5283, HTTPS) using the address the device reached it by,
checks whether the device trusts the certificate yet (and if not, offers it with install steps for Android,
iPhone/iPad, Windows and macOS), and shows whether the samples were published with WebAssembly AOT and when. The
hub's address is the machine's main network (an adapter with a gateway, not a virtual one); the others are listed
too. The first time, the tool makes a small certificate authority for this machine (kept in
`artifacts/serve/certificates`, git-ignored); install it once per device. The server's own certificate is made
fresh on every run, naming this machine and its current addresses. Windows asks once whether to let the tool through the firewall: allow it
on private networks.

How the browser backend talks to JavaScript: F# can't run .NET's `[JSImport]` generator, so
`Scribble.Platform.Browser/Js.fs` calls the API that generator emits (`JSFunctionBinding`) directly.
Each library's JavaScript is embedded in its DLL and loaded as a `data:` URL, so apps ship no extra files.

## Tests

```powershell
dotnet test
```

- `Scribble.Tests`: unit tests (no GPU). Tests that assert wall-clock time are skipped unless `SCRIBBLE_TIMING=1`;
  run them in Release: `$env:SCRIBBLE_TIMING = "1"; dotnet test tests/Scribble.Tests -c Release --filter Category=Timing`.
- `Scribble.Tests.Rendering`: renders offscreen through WebGPU and checks the pixels. Needs a GPU.
  Screenshot tests compare with PNGs in `tests/Scribble.Tests.Rendering/References` exactly, pixel for pixel (and
  D3D12 with Vulkan, which must agree too). On a mismatch the actual image is written to `tests/Scribble.Tests.Rendering/bin/snapshots`.
  To accept new output as the reference, run `$env:SCRIBBLE_UPDATE_SNAPSHOTS = "1"; dotnet test`.

## Text

```fsharp
let text = TextSystem.create device (new HarfBuzzShaper()) []       // browser: BrowserHarfBuzzShaper.CreateAsync()
drawList.DrawText(text, "Hello", Fonts.SemiBold(), 15.0, x, y, color) // (x, y) = top-left of the line box, in DIPs
let m = text.Measure("Hello", Fonts.Regular(), 15.0)                  // Width, Height, Baseline, LineHeight
```

Fallback and emoji: pass fallback fonts to the text system. Characters the requested font lacks are drawn with
the first fallback that has them; emoji prefer a color font. Text is split into grapheme clusters first, so
sequences like 👨‍👩‍👧, 👍🏽, 🇺🇸 and 1️⃣ stay intact.

```fsharp
let text = TextSystem.create device shaper [ TextSystem.fallbacks [ Emoji.Twemoji() ] ]   // Scribble.Text.Emoji package; in an app: App.fonts
```

**Attribution:** the emoji art is Twemoji by Twitter, Inc. and other contributors, under CC-BY 4.0. Apps that use
`Scribble.Text.Emoji` should credit it (e.g. in an About box); `Emoji.Attribution` has the text.

On Windows, HarfBuzz comes from the `HarfBuzzSharp.NativeAssets.Win32` NuGet package (SkiaSharp's build;
just the native DLL). In the browser it's the official harfbuzzjs WebAssembly module, embedded in
`Scribble.Text.HarfBuzz.Browser`. Both give identical results.

Licenses: Inter is under the SIL Open Font License 1.1 (`src/Scribble.Text/Fonts/Inter-OFL.txt`);
harfbuzzjs/HarfBuzz under the MIT-style "Old MIT" license (`src/Scribble.Text.HarfBuzz.Browser/harfbuzzjs-LICENSE.txt`).

## Views, updates and elements

A view is a description: `Border.create [ ... ]` returns a `View` (cheap to make), and the app's view function
builds a new one from the model on every update. Scribble turns the first one into elements and, after that,
compares each new view with the previous one: an element of the same type in the same place is patched setting by
setting, anything else is rebuilt, and an unchanged View object is skipped. Only what changed is laid out and drawn
again. Children match by position; in lists that insert, remove or reorder, give them a key so each item keeps its
element: `TextBlock.key todo.Id`, or let the panel take keys from the collection:

```fsharp
StackPanel.create [ StackPanel.items todos _.Id (fun todo -> TextBlock.create [ TextBlock.text todo.Title ]) ]
```

In one settings list each property is set once: when two settings set the same property the last one wins, and
Debug builds say so in the debugger's output (once per element type and property). A helper that takes extra
settings should take what it lets callers change as parameters rather than let them be set again.

The app holds the model (`App.program init update view`, the Elmish pattern). Messages update it straight away;
the view is rebuilt on the next frame, once, however many messages arrived, and not at all if the model didn't
change. Other app settings: `App.title`, `App.size`, `App.background`, `App.fonts` (fallbacks such as Twemoji),
`App.showStats`, `App.closeOnEscape`, `App.onKey` and `App.onFrame` (animation: frames continue only while the
model keeps changing; the first frame's `Delta` is zero, and a second frame follows it). `Button` is a Border with a default look (it stretches to fill its space like other elements; CheckBox and Switch keep to the left) that lightens under the pointer, darkens while
pressed and fades when disabled; it takes keyboard focus, and Enter or Space clicks it.

### Pointer input

Every element takes pointer settings:

```fsharp
Border.create [
    Border.onPointerEnter (fun _ -> dispatch (Hover(Some card)))  // enter/leave: not routed
    Border.onPointerLeave (fun _ -> dispatch (Hover None))
    Border.onPointerDown (fun e -> if e.Modifiers.HasFlag Modifiers.Shift then e.Handle())
    Border.onClick (fun e -> dispatch (Open e.Modifiers))         // left press + release over it (or Enter/Space)
    Border.cursor Cursor.Hand
    Border.isEnabled model.CanOpen                                // false: no input here or inside
]
```

- **Routing:** down, up, move and wheel go to the element under the pointer and then to its ancestors (bubbling).
  `onPreviewPointerDown`, `onPreviewPointerUp` and `onPreviewWheel` run first, from the root down (tunneling).
  `e.Handle()` stops the event; a press handled in Preview is taken by that element (no click below it).
- **Capture:** the element that gets a press keeps getting the moves and the release until the last button is up,
  even outside the window. Dragging off a button and releasing doesn't click; dragging back on and releasing does.
- **States:** element types read `ctx.IsPointerOver`, `ctx.IsPressed` and `ctx.IsEnabled` in Render. Views don't
  read states (that's styles, Milestone 7); use enter/leave.
- Positions are DIPs: `e.Position` (window) and `e.LocalPosition` (the element whose handler runs).

### Keyboard and focus

```fsharp
Border.create [
    Border.focusable true                                         // Tab stops here; a press focuses it
    Border.onKeyDown (fun e ->                                    // from the focused element up to the root
        if e.Key = Key.Char 's' && e.Modifiers.HasFlag Modifiers.Control then
            dispatch Save
            e.Handle())
]

App.onKey (fun key mods -> match key with Key.Space -> Some TogglePause | _ -> None)
```

- **Keys** are logical and follow the keyboard layout: `Key.Char 's'` (letters always lowercase), `Key.Enter`,
  `Key.Left`, `Key.F 5`, `Key.Other "..."`; modifiers come separately. The browser uses `e.key`; the desktop uses SDL's keycodes,
  which follow the active layout.
- **Routing:** `onPreviewKeyDown`/`onPreviewKeyUp` run from the root down to the focused element (or the root
  when nothing has focus), then `onKeyDown`/`onKeyUp` from there back up. `e.Handle()` stops the event and also
  the built-in behavior below.
- **Focus:** `focusable` (Buttons are by default). Tab and Shift+Tab move through focusable elements in view order
  (the order written in the view, not where they end up on screen) and wrap; a press focuses the nearest focusable element under the pointer, or clears focus. Disabling, hiding or
  removing the focused element clears focus.
- **Focus ring:** element types draw it when `ctx.IsFocusVisible` (`FocusRing.draw ctx list radius`): after Tab,
  not after a press, and not while the window is inactive. `ctx.IsFocused` is true either way.
- **Activation:** Enter clicks the focused element (if it has `onClick`); Space shows it pressed and clicks on
  release; Escape while Space is held cancels; key repeats never click.
- **App.onKey** runs last and only for keys nothing handled, so a focused button's Space doesn't also reach the
  app's Space shortcut. Repeats are left out. `App.closeOnEscape` likewise.

### Components

A component is a piece of view with its own state. Define it once, at module level; it's a function from its
props to a View, so it goes anywhere a view does:

```fsharp
let counter = Component.define "Counter" (fun ctx (name: string) ->
    let count = ctx.useState 0                                    // survives re-runs
    let fill = ctx.useAnimated(float count.Current / 10.0, TimeSpan.FromMilliseconds 250.0)
    StackPanel.create [
        StackPanel.orientation Horizontal
        StackPanel.children [
            TextBlock.create [ TextBlock.text $"{name}: {count.Current}" ]
            Button.create [ Button.text "+"; Button.onClick (fun _ -> count.Update(fun c -> c + 1)) ]
        ]
    ])

StackPanel.items names id counter                                // keyed: state follows the name
```

- **What re-runs:** a component runs again on the next frame when its state changes, and only its part of the tree
  is patched. When its parent re-runs, it runs again only if its props changed (F# equality; props that hold
  functions always count as changed). No props: `fun ctx () -> ...`, used as `counter ()`.
- **Hooks** (call them unconditionally, in the same order every time; a change of order fails with the
  component's name):
  - `ctx.useState initial`: `.Current`, `.Set v`, `.Update f` (an equal value changes nothing; setting state
    while the component runs fails).
  - `ctx.useMemo(deps, fun d -> ...)`: computed again only when `deps` change.
  - `ctx.useEffect(deps, fun d -> ...)`: after the frame that shows the component, and again when `deps` change
    (`()` for once; a tuple for several values).
  - `ctx.useSubscription(deps, fun d -> disposable)`: disposed when `deps` change and when the component goes.
  - `ctx.useElmish(init, update)`: `let model, dispatch = ...`; `ctx.useElmishWithCmd((model, cmd), update)`
    when `update` returns `model, cmd` (see Async below).
  - `ctx.useAsync(deps, fun d ct -> task)`: see Async below.
  - `ctx.useAnimated(target, duration, ?easing)`: a `float` or `Color` that moves to each new target; the
    component re-runs every frame while it moves.
- **For element authors:** `ctx.Animate("key", target, duration, ?easing)` in a render function animates
  without re-running any view, and `Element.transform (fun ctx -> Transform.ScaleAround(...))` changes how an
  element and its children are drawn without touching layout or input. Button uses both to sink while pressed.
  Easings: `Easing.linear`, `easeIn`, `easeOut` (the default), `easeInOut`, or any `float -> float`.
- **An app made only of components** has no messages. App settings carry the app's message type (`AppSetting<'Msg>`,
  so `App.onKey` and `App.onFrame` can return messages), and with none F# can't tell what it is. Written inline,
  `App.run [ App.title "Notes"; App.view (root ()) ]` compiles as it is; a list kept in a value, as the samples do
  to share it between their desktop and browser hosts, needs its type once:
  `let app: AppSetting<unit> list = [ App.title "Notes"; App.view (root ()) ]` (without it, F# reports a "value
  restriction", FS0030).

**App values:** state declared once and shared by any component, without passing it down:

```fsharp
let compactMode = AppValue.define "CompactMode" false            // a name (for the inspector), a default

let compactSwitch = Component.define "CompactSwitch" (fun ctx () ->
    let compact = ctx.useAppValue compactMode                    // the usual State: .Current, .Set, .Update
    Button.create [ Button.text (if compact.Current then "Roomy" else "Compact"); Button.onClick (fun _ -> compact.Update not) ])

App.run [ App.value compactMode true; ... ]                      // optional starting value
```

- Every component that reads it re-runs when it changes (an equal value changes nothing); others don't.
- One set of values per app, shared by its windows and layers (toasts and dialogs see the same); a bare
  `ElementTree` has its own (`tree.AppValues`), so tests stay apart.
- Outside components: `AppContext` from `App.onStart` (`ctx.Get compactMode`, `ctx.Set(compactMode, true)`), and
  `Cmd.setAppValue compactMode true` from an Elmish update. Sets work from any thread.
- The app's data belongs in its Elmish model; app values are for UI-wide things (a compact layout, the selected
  section, the signed-in user's name). The two work together. The window's theme is one: `Theme.current` (see
  Styles). The inspector lists `useAppValue CompactMode` with its value among a component's hooks.
- The window's title (the tab's title in the browser) is one too: `WindowTitle.current`. `App.title` sets where it
  starts; `ctx.useAppValue WindowTitle.current` then `.Set "Counter – Styles"` (or `AppContext.Set`,
  `Cmd.setAppValue`) renames the window. `App.showStats` adds its numbers after the current title.
- Code outside components can watch a value: `values.Subscribe(compactMode, fun v -> ...)` (dispose the result to
  stop).

### Async

Everything in the UI runs on one thread. Work that waits (files, network, delays) runs elsewhere and comes back:

```fsharp
let square = ctx.useAsync(n, fun n ct -> task { do! Task.Delay(1000, ct); return n * n })
match square with
| AsyncValue.Loading -> "loading…"
| AsyncValue.Loaded v -> string v
| AsyncValue.Failed e -> e.Message
```

- **`ctx.useAsync(deps, fun d ct -> ...)`** returns a `Task` or an F# `Async`. It starts after the frame that
  shows the component, and again whenever `deps` change; the old load's token is cancelled and its result
  ignored, so a slow old answer never overwrites a newer one. Removing the component cancels too. Reloading shows
  `Loading` again.
- **Commands:** `App.programWithCmd (model, cmd) update view` and `ctx.useElmishWithCmd` take an `update` that
  returns `model, cmd`. `Cmd.none`, `Cmd.ofMsg`, `Cmd.batch`, `Cmd.map`, `Cmd.ofSub (fun dispatch -> ...)`,
  `Cmd.ofTask work arg onSuccess onError` and `Cmd.ofAsync` (same shape). `App.program` and `useElmish` stay.
- **From other threads:** `State.Set`/`Update` and Elmish `dispatch` may be called from any thread; they're queued
  to the UI thread. Elements themselves are UI-thread only. `await` inside `task { }` started on the UI thread
  resumes there (Scribble installs a `SynchronizationContext`).
- **Errors:** an exception from app code goes to `App.onError (fun e -> ...)`, and the app keeps running, losing
  only what failed: an event handler (a failed `update` leaves the model as it was), the view (the last one stays
  on screen until the model changes again), a component (it keeps its last output), an element's render (it's
  left out of that frame), an ItemsView item's `view` (that item is left out), an effect, a command, queued work,
  a task nobody awaited. A custom element's measure or arrange that throws drops the frame. An error that repeats
  every frame (a render during an animation) is reported once until a frame goes through without it. **Without
  `App.onError` the error is rethrown and the app ends** (the debugger stops there). `useAsync` failures become
  `Failed e` instead.
- **The dispatcher** (`Scribble.Core.Dispatcher`: `Post`, `PostAfter`, `InvokeAsync`, `CheckAccess`,
  `RunPending`) is what all of this uses; apps rarely need it. Time comes from the platform (`IPlatform.Elapsed`,
  `IPlatform.After`), so tests can control it. A host that owns its loop (a game engine) calls `RunPending()` each tick.

### Layers

Toasts, popups, dialogs and the inspector go in layers: element trees drawn above the app, each filling the
window and laid out on its own (a layer never moves the app).

```fsharp
App.layer [ Layer.name "Toasts"; Layer.view (toasts ()) ]
App.layer [ Layer.name "Dialog"; Layer.order 200; Layer.modal true; Layer.view (dialog ()) ]
```

- **Pointer:** goes to the topmost layer with an element under it and falls through everywhere else, so give a
  layer's content a size or an alignment (a toast aligned to the bottom right) rather than letting it stretch over
  the window. A component's own host element doesn't count, only what it shows. A press stays with its layer
  until release; dragged over another layer, it's over nothing in its own.
- **Modal:** `Layer.modal true` takes all pointer and keyboard input while the layer shows something; showing
  nothing (hidden content), it blocks nothing, so a dialog layer can stay in place.
- **Keyboard:** each layer has its own focus and Tab order. Keys go to the layer last clicked, unless a modal layer
  is above it; the other layers' focus rings hide until they get the keyboard back.
- **Order:** the app is 0, `Layer.order` defaults to 100; equal orders stack in the order added.
- **Wheel:** `Layer.letWheelThrough true` sends the wheel on to the layers below even where this one has an
  element: an overlay that takes clicks but shouldn't stop scrolling (the inspector's pick mode uses it).
- **At run time:** `host.AddLayer [ ... ]` returns the `Layer` (`Update view`, `Focused`, `MoveFocus`), and
  `host.RemoveLayer layer` takes it away (its components stop). Dialog and Toast come with M8 step 4d.

**Flyouts** (M8 step 4a): any element can have one. A flyout is content shown in its own layer next to the
element, from wherever the view declares it. Give `flyout` the content alone, or the Flyout's settings when it needs
some:

```fsharp
Button.create [ Button.text "Copy…"; Button.flyout menu ]

Button.create [
    Button.text "Actions"
    Button.flyout [
        Flyout.placement Placement.Below         // Above, Left, Right; flips when it doesn't fit
        Flyout.alignment FlyoutAlignment.Start   // Center, End, along the button
        Flyout.content menu
    ]
]
```

The settings list is a value like any other: keep one in a `let` and give it to several elements.

- **Opening:** the element's click opens and closes it (for a Button, Enter and Space too). Give `Flyout.isOpen`
  to control it from the view; `Flyout.onOpenChanged` hears every open and close (when controlled, a request).
- **Closing:** Escape, or a press outside the content and the element, closes it, and that press does nothing else.
  `Flyout.lightDismiss false` keeps it open until the element is clicked again (or the view says). It also closes
  when its element scrolls out of view or goes away.
- **Focus:** moves into the content when it opens and back when it closes.
- **Placement:** 4 DIPs from the element, flipped and kept inside the window, following the element as it moves.
  `Flyout.matchAnchorWidth true` makes it at least as wide.
- **Stacking:** each open flyout is a layer of its own, stacked in opening order (a flyout from a flyout shows
  above it), above the app's layers and below the inspector.
- **Look:** the Flyout is also the box its content sits in, a Border the theme gives the surface colour, a line,
  rounded corners and a floating shadow. One flyout takes Border's settings (`Flyout.padding 0.0`,
  `Flyout.cornerRadius 12.0`); `Flyout.style [] [ ... ]` restyles them all.
- **Styled as if inside its element:** styles reach a flyout through its element, so you can style particular
  elements' flyouts. Inherited text values (colour, font) come only from the theme, not from the element.
  ```fsharp
  Button.styles [ Flyout.style [] [ Flyout.padding 0.0 ] ]                         // this button's flyout
  Flyout.style [ Style.parent Button.Type [ Style.cls "toolbar" ] ] [ ... ]        // toolbar buttons' flyouts
  Flyout.style [ Style.inside ComboBox.Type [] ] [ ... ]                           // every ComboBox's list
  ```
  A `theme` around the element is the flyout's theme too. Tooltips are styled the same way.

**Tooltips:** `Button.tooltip "Save (Ctrl+S)"` (any element), or `tooltipView view` for any content.
- **Showing:** after the mouse rests on the element for 500 ms. Moving straight on to another element with one
  shows the next at once.
- **Placement:** below the pointer, kept inside the window.
- **Hiding:** a press, the wheel or a key hides it until the pointer leaves the element.
- **Never in the way:** it takes no focus or clicks, and there are none on touch.
- **Look:** `TooltipPresenter.style` restyles it.

**ScrollViewer** (M6, completed in M8 step 1): `ScrollViewer.create [ ScrollViewer.height 300.0; ScrollViewer.content list ]`
scrolls its content when it's bigger than the ScrollViewer. Give it a bounded height.
- **Directions:** `ScrollViewer.vertical` and `ScrollViewer.horizontal` take `ScrollBarMode.Auto` (scrolls, with a
  bar), `Hidden` (scrolls, no bar) or `Disabled` (the content gets the viewer's size, so text wraps). Defaults:
  vertical `Auto`, horizontal `Disabled`.
- **The bar:** a 12-DIP gutter is kept on each `Auto` side (`ScrollViewer.scrollBarSpace Reserved`, default), whether
  or not the content overflows, so the content's width never changes when it starts to (it can't re-wrap and flip
  back). `Overlay` draws the bar over the content instead (the app leaves room; the Hello dashboard does). The thumb
  is 4 DIPs at rest and widens with a faint track under the pointer; drag it, or click the track to page.
  `barSize`, `thumb` and `track` are theme values (`Palette.scrollThumb`, `Palette.scrollTrack`).
- **Wheel:** a notch scrolls 48 DIPs, animated over 100 ms; a touchpad's fine deltas apply directly. Shift+wheel
  and sideways deltas scroll horizontally. At an end the wheel passes to whatever is outside.
- **Keys:** a press inside focuses the ScrollViewer (Tab doesn't stop on it: `isTabStop false`), and keys nothing
  handled bubble to it: arrows (one step), PageUp/PageDown, Home/End. Tab to something inside scrolls it into view.
- **Touch:** a finger that moves more than 8 DIPs scrolls (the press it started on is canceled), and a fling
  slows to a stop after release; a touch stops a fling. No bounce at the ends.
- **From code:** `let scroller = ctx.useScroller ()`, `ScrollViewer.scroller scroller`, then `scroller.ScrollToOffset`,
  `ScrollBy`, `ScrollToTop`, or `ScrollTo key` (an element with that `key`, or an item of an ItemsView inside).
  They animate briefly (`animate = false` jumps; far away always jumps) and apply at the next layout. An element
  can bring itself into view: `ctx.BringIntoView()` (input context).
- **Element authors:** `Element.wheel`, `Element.pan`/`panEnded`, `Element.pointerDown`/`pointerMove`/`pointerUp`
  (a type's own pointer behavior; handling a press captures the pointer), `Element.keyDown`, `Element.renderAfter`
  and `Element.clipChildren` are what it uses.

**ItemsView** (M8 step 1): shows many items, realizing only those the ScrollViewers around it show plus a little
more, so 10,000 items cost about what 40 do (tested: the same elements realized for 100 and 10,000, a scroll step
within 1.5×, scrolling to item 9,000 without realizing those between).

```fsharp
let messages =
    Component.define "Messages" (fun ctx (items: Message array) ->
        let scroller = ctx.useScroller ()
        ScrollViewer.create [
            ScrollViewer.content (
                ItemsView.create [
                    ItemsView.source items                          // array, list, ResizeArray, any seq
                    ItemsView.keySelector (fun (m: Message) -> m.Id)        // identifies an item across updates
                    ItemsView.viewSelector (fun (m: Message) -> TextBlock.create [ TextBlock.text m.Text ])
                    ItemsView.scroller scroller                     // scroller.ScrollTo(id, ScrollAlign.Start)
                ])
        ])
```

- **Layouts:** `ItemsLayout.stack` (default: each item as tall as it wants; measured as realized, others estimated
  from the average, so the bar may adjust a little), `fixedStack height`, `wrap width height` and `grid columns`
  (exact, by arithmetic).
- **Sideways:** in a ScrollViewer with `ScrollViewer.horizontal ScrollBarMode.Auto`, a `stack` or `fixedStack`
  measures its items at their natural width (text in them doesn't wrap) and is as wide as the widest item measured
  so far that's still in the source: the bar grows when a wider item first scrolls in, doesn't shrink while you
  scroll, and shrinks when that item leaves the source. Every item spans that width (or the viewport's, if wider).
  Shift+wheel scrolls sideways; scrolling to an item or offset leaves the sideways position alone. `wrap` and `grid`
  keep flowing to the viewport's width.
- **Scroll anchor** (`stack`): the first visible item, by key, keeps its place on screen while anything above it
  changes: estimates corrected, items inserted, removed or resized, the width changed (then the same fraction of
  it stays scrolled past). If it's removed, the next item takes its place. Filter hidden items out of the source
  rather than giving them `isVisible false`: a long run of zero-height items is realized all at once (debug builds
  write a warning when one pass realizes over 1,000 items).
- **Anywhere inside a ScrollViewer:** headers above it scroll with it; several on a page each virtualize. Outside
  any ScrollViewer it realizes everything.
- **Recycling:** items scrolled out give their elements to items scrolling in (patched by the differ). A component
  inside an item loses its state when it scrolls out (keep what must survive in the model, by key). The item with
  focus stays realized.
- **Scrolling to items:** `scroller.ScrollTo(key, ?align, ?animate)`, `ScrollToIndex`, `ScrollTo(position)` with
  `scroller.FirstVisible` (key and offset, to come back exactly), `ScrollAlign.Nearest` (default: no move when
  already visible), `Start`, `Center`, `End`. From Elmish: `Cmd.scrollTo "messages" key align` finds the
  ItemsView or ScrollViewer by its `name`. A key added in the same handler or update works (requests apply at the
  next layout).
- **Item text:** a view selector returning a string shows it as a TextBlock (`viewSelector (fun (p: Person) ->
  p.Name)`); with no view selector an item shows `string item` (strings, numbers, enums; unions and records print
  with `%A`, which NativeAOT can't run). A view selector needs the item type written (`fun (m: Message) -> ...`): F# can't infer it from
  `source` across settings.

### Inspector

`Scribble.Inspector` (a separate, opt-in package) adds a panel over the app with the element tree of every layer,
pick mode, a highlight, the selected element's properties and frame stats:

```fsharp
open Scribble.Inspector

App.run (settings @ [
#if DEBUG
    Inspector.enable [ Inspector.side DockSide.Left ]
#endif
])
```

- **Keys:** F12 shows and hides it, Ctrl+Shift+C picks an element (Escape cancels); change them with
  `Inspector.toggleKey (key, modifiers)` and `Inspector.pickKey`. They work whatever has focus, and in the browser
  they're kept from the browser's own developer tools. `Inspector.isOpen true` shows it from the start.
- **Panel:** docked right or left (drag its inner edge to change the width), floating (drag the title; resize from
  the corner grip), folded to its title bar (the minimize button or a double-click on the title), or, on the desktop, popped out
  into a window of its own (it closes with the app). Selection and folded rows stay as they are when
  it moves.
- It's built from Scribble's own elements and lives in two layers on top of everything (the panel, and a
  highlight that only takes the pointer in pick mode), so the app's layout never changes. Its toolbar icons are
  Google's Material Symbols (Apache License 2.0). Its tree is an ItemsView: only the rows in view have elements,
  so an app of 10,000 elements costs the panel what one of 100 does (a refresh: about 2 ms at 10,000 rows, Release).
  Deep trees scroll sideways (Shift+wheel or the bar); a pick brings the selected row's name into view.
- Custom hosts: `Inspector.attach (host, [ Inspector.platform platform ])` (the platform enables popping out),
  disposable. Packages that plug into an app this way use
  `App.onStart (fun ctx -> ...)` (`ctx.Host`, `ctx.Platform`, `ctx.Title`), `host.AddKeyPreview` (keys before
  any layer), `host.Closed`, `host.LayerChanged` and
  `host.FrameDrawn`. An element's `ComponentName` tells a component's host element apart.
- **Properties** of the selected element, below the tree (drag the divider between them): its states (hovered,
  pressed, focused, enabled, hidden); layout numbers (the space offered, desired size, bounds in its parent,
  position in the window); every property with a value and where it came from (`view`, `code` for
  `element.Set`, `type` for the element type's defaults such as Button's look); with "Show defaults", also
  the common properties still at their defaults (dimmed); and a component's hooks with their current values
  (`useState`, `useElmish`'s model, `useMemo`, `useAnimated`, `useAsync` loading/loaded/failed).
- **Values** are written by the inspector's own code (colors as hex, thicknesses, lengths, alignments, handlers
  as "handler", lists as a count and the first few items); other types show their type name, never
  `ToString()` on F# records or unions (that's `%A`, which crashes under NativeAOT). Teach it your types with
  `Inspector.format (function :? Model as m -> ValueSome m.Title | _ -> ValueNone)`.
- **Frame stats** at the bottom: the last frame's layout and render time and shape count (the inspector's own
  share included), and how many frames the app caused in the last second ("app idle" when none). They update
  with the tree, at most 10 times a second and only while the app changes: an idle app stays idle with the
  inspector open.
- The same data is public in Core for other tools: `element.Values` (`PropertyValue`: prop, value,
  `ValueSource`), `element.OfferedSize` and `element.Hooks` (`HookValue`: kind, value, status).

Also new with it: `Layoutable.isHitTestVisible false` lets the pointer go through an element and everything inside
it, and `IWindow.ClaimKey` keeps a key combination from the platform's own shortcuts.

### Styles

A style gives every element it matches some settings; a theme (M7 step 2) will be a list of them. Selectors are
typed lists, and each element module has a `style` next to `create`, so a style only takes its own type's settings:

```fsharp
let styles = [
    Button.style [ Style.cls "primary" ] [               // classes are plain names
        Button.background accent
        Style.on States.hover [ Button.borderBrush accentLight ]   // while hovered (blocks nest: states combine)
    ]
    TextBlock.style [ Style.inside Border.Type [ Style.cls "card" ] ] [ TextBlock.foreground light ]
    Button.style [ Style.cls "primary"; States.hover ] [ Button.borderBrush white ]   // a state in a selector list
    TextBlock.style [ Style.parent StackPanel.Type [] ] [ TextBlock.margin 2.0 ]
    Style.any [ States.focusVisible ] [ Layoutable.cursor Cursor.Hand ]
]

App.run [ App.styles styles; ... ]                                   // the whole app
Border.create [ Border.styles cardStyles; Border.content (...) ]       // an element and what's inside it
Button.create [ Button.classes [ "primary"; if wide then "wide" ]; Button.text "Save" ]
Border.create [ Border.on States.hover [ Border.background lighter ] ]  // inline, for this element alone
```

- **Selectors:** `X.style` matches type X exactly (a `Border.style` doesn't touch Buttons); `Style.any` matches
  every type. Parts: `Style.cls "name"`, `Style.name "x"`, any state (`States.hover`, `pressed`, `focused`,
  `focusVisible`, `disabled`, or your own), `Style.inside T [ ... ]` (any ancestor), `Style.parent T [ ... ]` (the direct parent),
  `Style.prop p v` (a property equals a value) and `Style.where p (fun v -> ...)` (a test on it).
- **Classes are plain names**, matched exactly (case counts). For typo safety, define a name once
  (`let primary = "primary"`) and use that.
- **Property selectors read the element's own value** (else its default), never a value set by a style, so a style
  can't turn itself on or off. They re-match by themselves when the value changes.
- **Precedence:** the view's (or code's) own setting wins; then the element's inline `on` blocks; then styles, the
  nearest scope first (its own `styles`, its ancestors', then `App.styles`), and within a list later beats earlier;
  then the element type's defaults. No CSS specificity. A setting the view drops lets the style show again.
- **Re-matching** happens at the start of layout, only where needed: an element joining the tree, classes, name,
  `isEnabled` or styles changing, a state changing (and the subtree when a style tests an ancestor's state, as with
  `inside Button.Type [ States.hover ]`). An app without styles pays nothing.
- Styles compare by reference: define them once at module level, not inside a view function.
- **Styles set how things look and behave, never app state:** a style has no model, no `dispatch` and no idea which
  element it's on. So it can't set handlers (`onClick`, any `on…`), controlled values (`isChecked`, `value`,
  `selected`, `Flyout.isOpen`), content (`content`, `children`, tabs, headers, items), a `flyout` or a tooltip. Nor
  can it set what decides matching (classes, styles, name, key, theme). Each throws when the style (or a state block,
  `Button.on States.hover [ … ]`) is made, saying
  why; for "these buttons get this menu", use a function or a shared settings list in the view.
- Element authors: add `let style parts (settings: Setting<MyElement> list) = Style.forType Type parts settings`
  next to `create`; a state read from a property is `States.ofProp "checked" CheckBoxProps.isChecked id` (matched
  again by itself); any other state is `States.define "name" (fun e -> ...)`, and `element.RefreshStates()` says it
  may have changed.
- The inspector shows a value a style set as `style`, with the selector under it (`from Button.primary:hover`). Its
  own layers ignore `App.styles` (`Layer.appStyles false`). Its STATES section is a row of toggles: every built-in
  state and every custom one (`States.customStates ()`; bright when on, `element.ActiveStates`). Clicking one forces the opposite of what it
  shows (on if it's off, off if it's on; again to stop), for looks only (styles and render functions see it, input doesn't), so you can inspect hover or pressed with the mouse
  elsewhere; tools can do the same with `element.ForceState(state, on)` (`element.UnforceState state` stops it;
  `element.ForcedStates` lists `(state, on)` pairs). Forced states spread like real ones: a
  forced `disabled` (on or off) reaches everything inside the element (forced off, except what has its own
  `isEnabled false`), a forced-on `hover` its ancestors. A state read from a bool
  property (`checked`, `indeterminate`, `selected`, or an app's `States.ofProp "x" prop id`) forced makes render
  functions read that property as `true` or `false` (`ctx.Get`): a forced checked CheckBox draws its tick, a Switch its knob on
  the right. Its real value doesn't change. (A TabControl's underline is the TabControl's: a forced `selected`
  header changes only the header's own look.)

**Themes** (M7 step 2a). The built-in looks are styles made by a theme, matched below every other style:

```fsharp
let theme = Theme.dark [ Theme.accent (Color.Hex 0x3D59A1); Theme.cornerRadius 8.0 ]   // Theme.dark [] is the default
App.run [ App.theme theme; ... ]
Border.create [ Border.theme otherTheme; Border.content (...) ]       // a different theme for what's inside
Button.style [ Style.cls "primary" ] [ Button.background Palette.accent ]  // a theme value (below)
```

- **Precedence:** own value > inline blocks > styles > `App.styles` > the theme > type defaults. Hosts made by hand
  (`ElementHost.create`) get `Theme.dark []` too; a bare `ElementTree` has none (types' base looks, no state looks).
- **State looks are general settings**, so app styles can change them:
  - `Border.overlay`: a translucent color laid over the whole box (background and border), used for hover and
    press. The theme lightens on hover (white at 8%) and darkens on press (black at 20%) this way, so the look
    works whatever the background color is, and a class color like `.primary` keeps its state looks.
    Transparent by default: `Button.style [ States.hover ] [ Button.overlay (Color.White.WithAlpha 0.15f) ]`.
  - `opacity` (disabled) and `scale` plus `shadow` (the press): `Button.style [ States.pressed ] [ Button.scale 0.9 ]`
    presses deeper.
- **Inherited text settings:** `foreground`, `font` and `fontSize` on any element reach the text inside
  (`Border.foreground c`, `Button.foreground c` for its label): own value, then styles, then the type default, then
  the nearest ancestor's, then the theme's.
- **Transitions:** `Style.transition BorderProps.overlay (TimeSpan.FromMilliseconds 100.0)` in a style's settings
  fades that property on every change (not on first show). Only properties that affect drawing alone (colors,
  numbers, shadows, corner radius, `opacity`, `scale`); others throw. Frames only while one runs.
- The inspector shows values from the theme's styles as `theme` ("from Button:hover"), the app's and elements' styles as
  `style`, and inherited text values as `inherited` (`ValueSource.Theme`, `Style`, `Inherited` in `element.Values`).
  It keeps its own dark theme whatever the app's is.

**Theme values and light/dark** (M7 step 2b). Colors, shadows, corner radius, font and size can be given by
**theme value** instead of by value: the setting then reads the value from the nearest theme, so one view and one style
list serve every theme, and nothing passes the theme around.

```fsharp
// The built-in theme values are in Palette; an app declares its own like props:
let cardLine = ThemeValue.define "CardLine" (Color.Hex 0x34345A)       // the default (no theme, or not set)

let dark  = Theme.dark  [ Theme.set cardLine (Color.Hex 0x34345A) ]      // make themes once
let light = Theme.light [ Theme.set cardLine (Color.Hex 0xDCDCE6); Theme.accent (Color.Hex 0x2F5BD3) ]

Border.style [ Style.cls "card" ] [
    Border.background Palette.surface        // a theme value, not a color: each theme gives its own
    Border.borderBrush cardLine
    Style.on States.hover [ Border.borderBrush Palette.accent ]
]

// The window's theme is an app value, Theme.current: App.theme sets where it starts, and setting it
// from anywhere switches the whole window (every layer that takes app styles, and the background):
App.run [ App.theme dark; ... ]
let themeSwitch = Component.define "ThemeSwitch" (fun ctx () ->
    let theme = ctx.useAppValue Theme.current
    let isLight = obj.ReferenceEquals(theme.Current, light)
    Button.create [ Button.text (if isLight then "Dark" else "Light")
                    Button.onClick (fun _ -> theme.Set(if isLight then dark else light)) ])
```

- **`Palette`:** `background`, `surface`, `control`, `border`, `text`, `textDim`, `accent`, `danger`, `onAccent`
  (text on accent or danger backgrounds, white in both), `focusRing`, `hoverTint`, `pressTint`, `scrollThumb`,
  `shadow` (a button at rest; the theme flattens it when pressed or disabled), `cornerRadius`, `font`, `fontSize`.
  Their defaults are the dark theme's values. The built-in looks are written with them, so `Theme.dark` and
  `Theme.light` differ only in values.
- **Setting values:** `Theme.set themeValue value`, or the shorthands for the built-ins (`Theme.accent c`,
  `Theme.cornerRadius 8.0`). The nearest theme decides; a theme value it doesn't set has its default.
- **Reading a value in code:** `theme.Get Palette.accent`; in a custom element's render function,
  `ctx.Theme Palette.accent`. Element authors give a setting a theme value overload with `Setting.fromTheme prop themeValue`.
- `App.theme` is the starting theme. Outside components, switch with `AppContext.Set(Theme.current, t)` or
  `Cmd.setAppValue Theme.current t`. A `theme` on an element applies only inside it (the root's too). A theme switch
  doesn't animate (hover and press still fade). The inspector shows the theme value under the value (`Palette.surface`).
- Not yet: following the OS's light/dark setting (later).

### Icons

Icons are shapes from SVG path data, drawn in the text color around them:

```fsharp
let menu = Icon.fromPath "M3 18h18v-2H3zm0-5h18v-2H3zm0-7v2h18V6z" [ Icon.name "menu" ]   // a 24 × 24 box by default
let logo = Icon.fromPath "M0 0h48v48H0z" [ Icon.viewBox (0.0, 0.0, 48.0, 48.0) ]

IconView.create [ IconView.icon menu; IconView.size 24.0 ]              // default size 20
IconView.create [ IconView.icon (menu |> Icon.rotate 90.0); IconView.foreground c ]
Button.create [ Button.icon menu; Button.text "Menu" ]                   // icon before the text, or alone
```

- Path data: M, L, H, V, C, S, Q, T, A and Z, absolute and relative. Shapes fill with the nonzero rule, as fonts do
  (SVG's even-odd rule isn't supported). Bad data throws a `FormatException` when the icon is first drawn.
- An icon is a plain F# record (name, path data, viewBox, rotation, flips). Its settings are `Icon -> Icon`
  functions, so the same ones change an existing icon: `menu |> Icon.rotate 90.0` (clockwise, replacing any
  rotation), `|> Icon.flipHorizontal`, `|> Icon.flipVertical` (these set the flip; applying one twice doesn't undo
  it). `Icon.describe` gives "menu-rotate90-fliph" (the inspector shows it). Equal icons compare equal, so a rebuilt
  view with the same icon changes nothing.
- Color: the inherited `foreground` (so themes, styles and `opacity` apply); `IconView.foreground` sets its own.
- Rendering: rasterized once per pixel size into the glyph atlas, like text; sharp at any scale, drawn in the same
  single draw call. `Button.iconSize` (default 18) sets a button's icon size.
- Beside `Button.text`, the icon sits at the text's optical middle (halfway between the middle of its capitals
  and of its lowercase letters, from the font's OS/2 cap height and x-height): about 0.6 DIP below the button's
  middle at 14 px, since mostly-lowercase labels look low beside an exactly centered icon. An icon alone, or
  beside other content, stays centered.
- An icon set is just a module of icon values.

**Material Symbols** (opt-in package `Scribble.Icons.Material`): Google's 4,304 Material Symbols (Outlined style,
weight 400), each outlined or filled. © Google, Apache License 2.0: credit them in your app.

```fsharp
open Scribble.Icons

Button.create [ Button.icon Material.home; Button.text "Home" ]
IconView.create [ IconView.icon Material.star.filled ]   // .outlined goes back
icons |> List.map Material.filled          // the same as functions, for piping
Material.arrowUpward |> Icon.rotate 90.0
Material._10k                              // a leading digit gets "_"
Material.``class``                         // the 7 names that are F# keywords: class, done, event, function, public, select, try
Material.icon "star-filled-rotate90"       // from text (name, then options: filled, outlined, rotateN, fliph, flipv)
Material.tryIcon name                      // ValueNone for unknown names (Material.icon throws)
Material.names ()                          // every name
```

- Names are Google's (https://fonts.google.com/icons) in camelCase. `Icon.describe` gives text `Material.icon` reads
  back ("star-filled-rotate90"); a filled icon's name is "star-filled".
- Size: the paths are stored as plain text (the browser's .NET has no Brotli decoder), so the package adds about
  5 MB to a desktop app, and about 0.6 MB to a browser app's download (a browser publish Brotli-compresses it; 1.6 MB
  where a server only offers gzip). Only when an icon is used: unused, it's trimmed away. A set is read the first
  time one of its icons is used (under 1 ms on a desktop; outlined takes about 3.3 MB of memory, filled about 2 MB).
- Regenerated from Tim's Avalonia `MaterialIconData.cs` by `dotnet fsi tools/GenerateMaterial.fsx [path]`.

**Cursors from icons:** any icon can be the pointer, shown by the OS (a hardware cursor: no lag, no extra frames,
past the window's edge while dragging):

```fsharp
let picker = Cursor.fromIcon Material.colorize [ Cursor.hotspot (3.0, 21.0) ]   // define once, as a value
Border.create [ Border.cursor picker; ... ]
```

- Default look: white with a 1-DIP black outline (shows on any background), 24 DIPs, pointing at its center.
  Settings: `Cursor.size`, `Cursor.hotspot (x, y)` (DIPs from the icon's top-left), `Cursor.fill`,
  `Cursor.outline` (transparent for none), `Cursor.outlineWidth`.
- Made for the window's scale, and again when the window moves to a screen with another scale. Equal settings make
  equal cursors, so platforms make each once and keep the 32 used last (SDL color cursors on the desktop; in the browser a PNG in CSS `cursor`,
  sharp through `image-set` where the browser takes it). Phones have no cursor.

**Cursors from images:** `Cursor.fromImage image [ ... ]` works the same way with any `Image` (a resource, bytes,
pixels, a file or a URL):

```fsharp
let potion = Cursor.fromImage potionImage [ Cursor.size 32.0; Cursor.scaling ImageScaling.Pixelated; Cursor.hotspot (16.0, 3.0) ]
```

- Its own size in DIPs (pixels ÷ density) unless `Cursor.size` gives the width; at most 128 DIPs a side (browsers
  ignore larger cursors; larger ones shrink, with a debug warning). Pointing at its centre unless `Cursor.hotspot`.
- Resampled on the CPU for each window scale, so every platform shows the same pixels; `Cursor.scaling` picks smooth
  (default) or pixelated enlarging.
- The element using it holds the image while it's in the tree and not hidden, so it's usually decoded before the pointer arrives.
  Until it's ready the arrow shows, then the cursor changes (no frame drawn); a picture that fails is reported once
  to `App.onError`. `Cursor.fill` and `outline` are for icons only.
- Not yet: following the Windows cursor-size setting; cursors from bitmaps (with Image, M8).

### Rendering to an image and saving it

Any view, or any element on screen, can become an `Image`: shown in an `ImageView` at once, or saved as a PNG.

```fsharp
// A view on its own, no window (in an app, or in a tool after `use _ = DesktopApp.headless []`).
let! image = ViewImage.render (chart model) [ ViewImage.size (640.0, 400.0); ViewImage.scale 2.0 ]

// An element as it is on screen: hover, scroll and all.
let capture = ctx.useCapture ()              // or Capture() kept with your model
Border.create [ Border.capture capture; ... ]
let! image = capture.ToImage [ Capture.margin 16.0 ]   // room for its shadow; transparent around it

let! png = Image.toPng image
let! saved = Files.save "chart.png" png      // a save dialog on the desktop, a download in the browser
```

- Every render returns a `Task` (the browser reads pixels back asynchronously); on the desktop it's already done.
- `ViewImage` waits for images still loading (up to `ViewImage.imageTimeout`, 10 s) and uses the app's theme, or
  `ViewImage.theme`. It sizes to its content when no size is given. Its background is the theme's; a capture's is
  transparent.
- A capture covers its element's whole box, even when partly scrolled away; it fails, saying why, if the element
  isn't showing. Pictures larger than 8192 pixels a side fail with a message.
- `Files.save` answers false when the person cancels (always true in the browser). The Gallery's Images page has
  both, under "Saving as PNG".

### Elements and layout

Widths and heights (and their min/max) take a number of DIPs, a `Length`, or a string:

| Setting | Means |
| --- | --- |
| `Border.width 36.0`, `Border.width (Px 36.0)`, `"36px"`, `"36"` | Fixed, in DIPs |
| `Border.width (Percent 50.0)`, `"50%"` | Half of the space the parent offers (margin included); automatic in unlimited space |
| `Border.height (Star 1.0)`, `"*"`, `"2*"` | A weighted share of leftover space in a StackPanel; elsewhere it stretches |
| `Auto`, `"auto"` (the default) | Size to content, or stretch per alignment |

Star is opt-in: a StackPanel's children are as long as their content unless they ask for a share, and the panel
only shares when its own length is known. A header, a content area that fills, and a footer:

```fsharp
StackPanel.create [
    StackPanel.children [
        header
        Border.create [ Border.height "*"; Border.content content ]
        footer
    ]
]
```

Panels: `StackPanel` (a row or column), `WrapPanel` (left to right, wrapping; `WrapPanel.breakAfter true` on a
child starts a new row), `DockPanel` (strips along the sides; a `Dock.Fill` child, else the last, fills), `Canvas` (positions) and
`Grid` (rows and columns). A child says where it goes with **attached settings**, which only its parent reads:

```fsharp
Grid.create [
    Grid.columns "auto, *, auto"                     // or [ GridColumn.create [ GridColumn.width "*"; GridColumn.minWidth 120.0 ] ]
    Grid.rows "auto, auto"
    Grid.columnSpacing 12.0
    Grid.rowSpacing 8.0
    Grid.children [
        TextBlock.create [ TextBlock.text "Name" ]   // (0, 0) when not placed
        nameField [ Grid.column 1 ]
        Button.create [ Button.text "Check"; Grid.cell (0, 2) ]
        notes [ Grid.row 1; Grid.column 1; Grid.columnSpan 2 ]
    ]
]
```

Fixed and Auto columns come first; stars share what's left by weight (within their min/max); in unlimited space
(a ScrollViewer's direction) stars act as Auto; a child that spans a star column doesn't widen the Auto columns it
also spans. Lines land on whole physical pixels, so cells tile with no gaps. `DockPanel.dock Dock.Top`,
`Canvas.left 40.0`, `Layoutable.zIndex 1` (drawn and hit above its siblings; Tab order stays the view's) and
`Separator.create [ Separator.orientation Vertical; Separator.lineStyle LineStyle.Dashed ]` work the same way.
Borders can be dashed or dotted: `Border.borderStyle LineStyle.Dashed`, `Border.borderDash (6.0, 3.0)`.

A DockPanel's children take their strips in the order written. The child that says `Dock.Fill` gets what's left,
wherever it is written, so a shell reads top to bottom, which is also the order Tab and a screen reader take:

```fsharp
DockPanel.create [
    DockPanel.children [
        toolbar [ DockPanel.dock Dock.Top ]
        content [ DockPanel.dock Dock.Fill ]         // no Fill child: the last child fills (`lastChildFill`)
        statusBar [ DockPanel.dock Dock.Bottom ]
    ]
]
```

Settings are checked at compile time: `TextBlock.margin` works because every element has a margin, while
`TextBlock.create [ Border.background … ]` is a type error. Every element has margin, width/height, min/max sizes,
alignment, visibility and clipping. Layout is measure-and-arrange, rounded to whole physical pixels, and changing a
setting only redoes what it affects.

A custom element is three parts (see `samples/Hello/Dashboard.fs` for four of them):

```fsharp
module ProgressBarProps =                                         // 1. properties, and what they affect
    let value = Prop.define "Value" 0.0 Affects.Render

[<AbstractClass>]
type ProgressBar =                                                // 2. its settings
    inherit Layoutable
    static member value<'k when 'k :> ProgressBar>(v: float) : Setting<'k> = Setting.create ProgressBarProps.value v

module ProgressBar =                                              // 3. the element: definition and create
    let Type =
        Element.define "ProgressBar" [
            Element.measure (fun _ _ -> Size.Create(0.0, 8.0))
            Element.render (fun ctx list ->
                list.FillRect(Rect.Create(0.0, 0.0, ctx.Size.Width * ctx.Get ProgressBarProps.value, 8.0), Color.White))
        ]
    let create (settings: Setting<ProgressBar> list) = View.Create(Type, settings)
```

A definition lists only what differs from a plain container (`Element.measure`, `Element.arrange`,
`Element.render`). `Element.extend Border.Type "Button" [ ... ]` starts from another element, and
`Element.defaults [ Border.background ...; Layoutable.cursor Cursor.Hand ]` sets values its elements have unless
a view says otherwise (that's how Button gets its look). `Type` and `create` go in a module rather than on the
settings type because static members are inherited: on the type, Button's would collide with Border's.

Element types are defined once, as static values: the differ recognizes a type by reference. `ElementTree` works
without a window (`tree.Update view`), which is how the layout and differ tests run; `App.start` runs an app on any
`IPlatform`, which is how the app tests run against a fake platform. Hosting Scribble yourself (a game engine, say)
uses the same settings style as everything else:

```fsharp
let device = WebGpuDevice.Create [ WebGpuDevice.backend BackendType.Vulkan; WebGpuDevice.powerPreference GpuPower.LowPower; WebGpuDevice.frameLatency 1 ]
let host = ElementHost.create window device text [ ElementHost.background (Color.Hex 0x181825); ElementHost.beforeFrame tick ]
```

`WebGpuDevice.frameLatency` (default 1) is how many finished frames may wait for the screen: 1 lags the mouse
least; 2 or more lets the CPU and GPU overlap for heavy frames. It doesn't change how many frames are drawn.
(`BrowserGpuDevice.CreateAsync [ BrowserGpuDevice.powerPreference ... ]` in the browser, where the browser decides
when frames reach the screen.) Typical numbers (Release, after warm-up): 10,000 elements lay out in about 1 ms, and changing one of them takes a
few microseconds; building, diffing and laying out a 1,000-element view with one change takes about 0.5 ms.

### Controls

`CheckBox`, `Switch` and `ToggleButton` are on or off; `Slider` picks a number; `TabControl` shows one page of
several. Each one either shows what the view gives it (it's **controlled**) or keeps its **own value**:

```fsharp
// Controlled: shows exactly model.Remember; a click only reports the new value.
CheckBox.create [ CheckBox.text "Remember me"; CheckBox.isChecked model.Remember
                  CheckBox.onCheckedChanged (fun on -> dispatch (SetRemember on)) ]
// Own value: no isChecked, so it keeps its own (rebuilding the view doesn't reset it); it still reports changes.
Switch.create [ Switch.text "Notifications"; Switch.onCheckedChanged (fun on -> dispatch (Notify on)) ]
```

- **CheckBox**: `isIndeterminate true` shows a dash ("some selected"; display only, a click reports `true`).
  CheckBox and Switch take `text` or `content` as their label; a click anywhere on either flips them. Space and
  Enter work when focused.
- **Slider**: `minimum` 0, `maximum` 100, `step` 1 (`0.0` for continuous); `onValueChanged` on every change,
  `onValueCommitted` when the user lets go (a drag ends, a key step lands). ←/↓ and →/↑ step (1% when
  continuous), PageDown/PageUp 10%, Home/End the ends; a press on the track jumps there and drags on.
  `orientation Vertical` has its minimum at the bottom.
- **TabControl**: `TabControl.tabs [ TabItem.create [ TabItem.header "General"; TabItem.content page ] ]`,
  `selected` (an index; leave it out to let the TabControl keep it), `onSelectedChanged`. A page is built the
  first time its tab is shown and then kept, hidden, with its state (`TabItem.keepAlive false` lets it go), so tabs
  never visited cost nothing. ←/→ on a focused header select (Home/End the ends); Tab moves into the page. Headers
  that don't fit scroll sideways, and the selected one is brought into view.
- Styles select on their values with states: `CheckBox.style [ States.isChecked ] [ ... ]`, `States.isIndeterminate`,
  `TabHeader.style [ States.isSelected ] [ ... ]` (F# reserves the word `checked`). The themes color them from the
  palette; nothing new to set in a theme.
- An element can be given keyboard focus from code: `element.Focus()`.
- **RadioGroup and RadioButton**: one option of several. The group holds the choice (`RadioGroup.selected v`,
  `onSelectedChanged (fun (v: 'T) -> ...)`, controlled or its own); each `RadioButton.value v` names its option.
  Radios may sit anywhere inside the group (a StackPanel: `orientation`, `spacing`). One Tab stop (the chosen
  option, else the first); the arrow keys choose the next or previous, wrapping and skipping disabled ones.

```fsharp
RadioGroup.create [
    RadioGroup.selected model.Size
    RadioGroup.onSelectedChanged (fun (s: Size) -> dispatch (SetSize s))
    RadioGroup.children [
        RadioButton.create [ RadioButton.value Small; RadioButton.text "Small" ]
        RadioButton.create [ RadioButton.value Large; RadioButton.text "Large" ]
    ]
]
```

- **ListBox**: a list the user selects from. It's an ItemsView (so 10,000 items cost about what 40 do: put it in
  a ScrollViewer) that wraps each item's view in a `ListBoxItem`. The selection is **keys** (`keySelector`; the
  index without one), so it survives inserts, sorting and filtering. `selectionMode`: `Single` (default),
  `Multiple` (Ctrl+click toggles, Shift+click selects a range, Ctrl+A all, Space toggles, Shift+arrows extend) or
  `None` (a list of actions: a click invokes). One Tab stop; ↑/↓, Home/End and PageUp/PageDown move through the
  items, realizing only the one reached. **Invoking** acts on an item: a double-click or Enter (a click in `None`).

```fsharp
ScrollViewer.create [
    ScrollViewer.content (
        ListBox.create [
            ListBox.source model.People
            ListBox.keySelector (fun (p: Person) -> p.Id)
            ListBox.viewSelector (fun (p: Person) -> p.Name)          // or a view
            ListBox.selected [ model.Current ]                         // left out: it keeps its own
            ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) -> dispatch (Pick e.Key))
            ListBox.onItemInvoked (fun (e: ItemInvokedEventArgs<Person, int>) -> dispatch (Open e.Key))
        ])
]
```

  The change event has `Keys`, `Items`, `Added`, `Removed`, and `Key`/`Item` (the one selected, for Single). A
  selected key that leaves the source is dropped (and reported) when the list keeps its own selection; a
  controlled list leaves the app's keys alone. Style the items with `ListBoxItem.style [ States.isSelected ] [ ... ]`
  (`ListBoxItem.indicator` is the bar at the left of a selected item).

Own values belong to the element: in an ItemsView, whose rows are recycled, give the value (controlled), or a row
scrolled in may show the own value of the item it was made for before.

### Semantics (for assistive technology)

Every element can say what it is to a screen reader: a **role** (button, checkbox, slider, tab, list item, ...),
a **name**, a value, states (checked, selected, disabled, focused) and the actions it supports. The built-in
elements fill this in themselves; a Button or CheckBox is named by the text inside it. No screen reader is
connected yet (that comes later); for now the semantic tree is something to check and test. It lives in its
own namespace, so it stays out of autocomplete until you open it:

```fsharp
open Scribble.Accessibility

Button.create [ Button.icon Material.close; Semantics.label "Close" ]   // an icon-only button needs a name
Slider.create [ Slider.value model.Volume; Semantics.label "Volume" ]   // a slider has no text of its own
IconView.create [ IconView.icon logo ]                                  // no label: decorative, left out
Border.create [ Semantics.hidden true; ... ]                            // leave this and its contents out
Border.create [ Semantics.role Role.Group; Semantics.label "Shipping address"; ... ]
Border.create [ Semantics.role Role.Switch; Semantics.label "Wi-Fi"; Semantics.isChecked model.WiFi
                Border.onClick (fun _ -> dispatch ToggleWiFi); Border.focusable true ]   // an app's own switch
```

- `host.SemanticTree()` builds the window's tree when asked (never per frame). Its `ToString()` prints one node per
  line (`CheckBox "Remember me" checked`), and `node.Perform SemanticAction.Click` (or `Increment`, `Focus`,
  `ScrollForward`, ...) acts the way the matching input would. Tests can drive an app this way.
- Layout panels and plain Borders aren't in the tree; what's inside them takes their place. A ScrollViewer is in
  it only when it can scroll, an ItemsView's realized items know their place among all of them ("3 of 10,000"),
  and a modal layer hides the layers below it.
- The inspector's SEMANTICS section shows the selected element's node, or why it has none ("decorative (no
  name)", "part of its control's name").
- Element types give theirs in `Element.defaults`: `Semantics.role Role.CheckBox`, and for what changes, the
  property to read (`Semantics.checkedFrom (ToggleButtonProps.isChecked, ToggleButtonProps.isIndeterminate)`).

## Project layout

| Project | Purpose |
| --- | --- |
| `src/Scribble.Core` | Views, the differ, elements and layout, styles and themes, input, components, layers, App settings and runtime. Folders follow compile order: `Foundation` (primitives, properties, dispatcher, app values, commands), `Framework` (Element, styles, the element tree, input), `Elements/Basic`, `Elements/Panels`, `Elements/Scrolling`, `Elements/Controls`, `Themes`, `Hosting` (components, layers, ElementHost, App) |
| `src/Scribble.Desktop` | `App.run` on the desktop: the platform, WebGPU renderer and HarfBuzz shaping in one reference |
| `src/Scribble.Inspector` | Opt-in inspector: `Inspector.enable [ ... ]` (element tree of every layer, pick mode, highlight, properties, frame stats) |
| `src/Scribble.Icons.Material` | Opt-in Material Symbols: `Material.home`, `Material.filled`, `Material.icon "name"` (© Google, Apache 2.0) |
| `src/Scribble.Browser` | `App.run` in the browser: the browser equivalents, plus a default page (`build/page`) |
| `src/Scribble.Platform` | Window, event, DPI and frame-clock abstractions |
| `src/Scribble.Platform.SDL3` | The desktop platform: windows, input and cursors through SDL3 (our own F# bindings) |
| `src/Scribble.Rendering` | Backend-neutral rendering interfaces |
| `src/Scribble.Rendering.WebGPU` | WebGPU backend over `webgpu.h` (wgpu-native) |
| `src/Scribble.Text` | Font parsing, glyph rasterizer, glyph cache, text drawing, measuring, wrapping and trimming; Inter |
| `src/Scribble.Text.Emoji` | Opt-in color emoji: the Twemoji font (COLR v0, ~1.4 MB) |
| `src/Scribble.Text.HarfBuzz` | Shaping with native HarfBuzz (desktop) |
| `src/Scribble.Text.HarfBuzz.Browser` | Shaping with HarfBuzz compiled to WebAssembly (browser) |
| `src/Scribble.Platform.Browser` | Browser backend: canvas, DOM events, `requestAnimationFrame` |
| `src/Scribble.Rendering.WebGPU.Browser` | Browser WebGPU (`navigator.gpu`) backend |
| `samples/Hello` | The Hello sample (library): `HelloApp.fs` (model, update, app settings) and `Dashboard.fs` (the dashboard as a view function, also used by the rendering tests) |
| `samples/Counter` | The Counter (library): the README's first example, one file |
| `samples/Gallery` | The Gallery (library): one file per page (`ComponentsPage.fs`, `LayoutPage.fs`, `ControlsPage.fs`, `ScrollingPage.fs`, `LayersPage.fs`, `StylesPage.fs`, `ImagesPage.fs`), `Common.fs` (page and section layouts, theme values, themes) and `GalleryApp.fs` (the tabs and the app) |
| `samples/Icons` | The Material Symbols gallery (library) |
| `samples/X.Desktop`, `samples/X.Browser` | Each sample's hosts: the same one-line Program.fs, a different SDK and Scribble package |
| `samples/Samples.Shared` | `Adaptive.fs`: the samples' own `SplitRow` |
| `samples/Directory.Build.props` | The hosts' defaults (NativeAOT for desktop, `-p:BrowserAot=true` for the browser) |
| `tests/*` | Unit and rendering tests |

## Interop rules

F# can't use the `LibraryImport` source generator (it's C#-only), so native calls use `DllImport` with
**blittable signatures only**: pointers instead of `ref`/`out`/`string`, structs made of primitive fields,
callbacks via `Marshal.GetFunctionPointerForDelegate` with the delegate kept alive. Calls like that need
no runtime marshalling, so they work under NativeAOT.

## License

Scribble is under the MIT License (`LICENSE`). What it bundles keeps its own license: Inter (SIL Open Font
License 1.1), HarfBuzz and harfbuzzjs (MIT-style), Twemoji in `Scribble.Text.Emoji` (CC-BY 4.0: apps that use it
must credit it) and Material Symbols in `Scribble.Icons.Material` (Apache License 2.0); see "Text" and "Icons"
above.
