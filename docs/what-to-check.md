# What to check

What each sample should show and do when you run it (`dotnet run --project samples/Hello.Desktop`, `Gallery.Desktop`,
`Icons.Desktop`, `Counter.Desktop`; the README has the browser commands). Moved here from the README at the first
public push (DESIGN.md, project review, decision 18).

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
   - Toasts: "Show a toast" puts one at the bottom right for 4 seconds (it waits while the pointer is on it);
     "Show a warning" is amber and stays 8 seconds; "Show an error" is red and stays until its × is clicked; "With an action" has an Undo button. Three show at a time,
     newest lowest. The app keeps working everywhere except on a toast itself, and a click on a toast never
     moves keyboard focus.
   - Modal and dialogs: "Open a modal" dims the window and shows a box. Nothing below reacts: no hover, no clicks, no wheel,
     and Tab cycles only through the modal's buttons (the app's focus ring goes away and comes back after).
     "Toast" in the modal shows a toast above the dimming; Escape, a click outside or Close brings the app back
     exactly as it was. Confirm, Warning, Critical, Three choices and Inform are declared with hooks; the line
     under them shows the answer (Critical starts with focus on Keep).
   - Hover a button, then open the modal without moving the mouse: the button stops looking hovered.
9. The Styles page (M7 step 1, verified on Windows and in the browser 2026-11-16):
   - Primary is blue and Danger red (classes); the selected tab's blue comes from a style too.
   - Hover the card: its border and all three lines of text turn light blue (`Style.onState States.hover` on the card,
     and text matched by `Style.inside Border.Type [ Style.cls "card"; States.hover ]`); the line below the card stays
     grey. Leave it: they go back.
   - "Hover me" lights up under the pointer (an inline `Border.onState States.hover` block).
   - The chips at the bottom (Fast, Quiet, Cheap): Fast starts blue with white text. Clicking a chip toggles it
     (a state of the page's own, `States.define "chosen" []`, switched with `Border.state chosen true`; the text
     follows it through `Style.inside`); hovering a chip lightens it, chosen or not. In the inspector, pick a
     chosen chip: its states line ends with `chosen`, lit, and its `background` is "from Border.chip:chosen".
     Click it again and `chosen` dims. An element that isn't a chip doesn't list `chosen` at all.
   - The slider under the chips: drag it past 80 and its filled part turns red, back below and it's blue again
     (a look that follows its value: `Style.onWhere SliderProps.value (fun v -> v >= 80.0) [ ... ]`, no state).
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
   - Every page follows: the counters' bars, the Scrolling page's rows, scrollbar, toast and modal (open it after
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
     and the custom states that are the element's: `checked` on a CheckBox, `chosen` on the Styles page's chips).
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

1. Outlined/Filled switches every icon. The search field (a TextBox; Escape clears it) scrolls to the first icon
   whose name starts with (else contains) what you typed and outlines it. It doesn't open a phone's keyboard yet
   (M9 step 9d).
2. A click on a tile shows the F# to write for it above the grid: `Material.arrowUpward`, with `.filled` when
   filled, ``Material.``class`` `` for keywords, `Material._10k` for a leading digit.
3. Tiles light up under the pointer, and the pointer becomes that tile's icon (white with a black outline,
   pointing at its center): a quick way to see how any icon works as a cursor. Scrolling (wheel, a finger on a
   phone) is smooth.

