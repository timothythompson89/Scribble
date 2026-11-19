namespace Scribble.Core

open System.Collections.Generic
open System.Diagnostics
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text

/// Timing of the last frame the host drew, for the title bar, the inspector and perf tests.
[<Struct>]
type FrameStats =
    { LayoutMilliseconds: float
      RenderMilliseconds: float
      Shapes: int }

/// What the ElementHost settings configure.
type internal ElementHostOptions =
    { Background: Color voption
      Dispatcher: Dispatcher voption
      BeforeFrame: unit -> unit
      Styles: Style list
      Theme: Theme voption
      Toasts: ToastPlacement voption
      AppValues: AppValues voption
      /// Time since some fixed start (the platform's `Elapsed`); a stopwatch when not set.
      Clock: (unit -> System.TimeSpan) voption }

/// One setting for `ElementHost.create`: `ElementHost.background (Color.Hex 0x181825)`.
[<Struct>]
type ElementHostSetting = internal ElementHostSetting of (ElementHostOptions -> ElementHostOptions)

/// Which layer one pointer belongs to.
type internal LayerPointer(kind: PointerKind) =
    member _.Kind = kind
    member val Position: Point voption = ValueNone with get, set
    member val Modifiers = Modifiers.None with get, set
    /// The layer the pointer is over (the one that last got its events).
    member val Over: Layer voption = ValueNone with get, set
    /// The layer that got the press: it keeps the pointer until every button is up.
    member val Capture: Layer voption = ValueNone with get, set
    member val Buttons = 0 with get, set

/// A flyout the host shows: its anchor, the layer the anchor is in, and its own layer.
type internal OpenFlyout(anchor: Element, anchorLayer: Layer, layer: Layer, returnFocus: Element voption) =
    member _.Anchor = anchor
    member _.AnchorLayer = anchorLayer
    member _.Layer = layer
    /// What had focus in the anchor's layer when it opened (focus goes back there when it closes).
    member _.ReturnFocus = returnFocus
    /// What the layer shows was made from these (a change rebuilds the view).
    member val Content: View voption = ValueNone with get, set
    member val Rect = Rect.Create(0.0, 0.0, 0.0, 0.0) with get, set
    member val Shown = false with get, set
    /// Focus moves into it after its first layout.
    member val FocusPending = true with get, set
    member val Ring = false with get, set
    /// A modal: its layer is modal and fills the window (a scrim with the box in the middle), not placed by an anchor.
    member val IsModal = false with get, set
    /// A modal's scrim starts clear and fades in once it has been laid out.
    member val Entered = false with get, set
    /// Where the keyboard and the focus go when it closes, when that isn't its anchor's layer (a context menu).
    member val ReturnTo: struct (Layer * Element voption) voption = ValueNone with get, set

/// A layer an element asked for (`IElementRoot.ElementLayer`): the element, the layer it is in, its own layer, and
/// what gives the view to show there.
type internal ElementLayer(owner: Element, ownerLayer: Layer, layer: Layer, content: unit -> obj) =
    member _.Owner = owner
    member _.OwnerLayer = ownerLayer
    member _.Layer = layer
    member val Content = content with get, set
    /// The view the layer shows now (the same one again changes nothing).
    member val Shown: View voption = ValueNone with get, set

/// Connects element trees to a window: lays them out at the window's size and scale, draws them
/// on every Frame, and asks for a frame whenever something in them changes. `App.start` makes
/// one; a custom host (a game engine, a test) makes its own with `ElementHost.create`.
///
/// The app's tree is one layer (`App`, order 0); more layers can go above it (`AddLayer`). The
/// host decides which layer gets the pointer (the topmost with something under it, or a modal
/// one) and the keyboard (the layer last clicked, unless a modal layer is above it).
///
/// Forward the window's events to `HandleEvent`; the host handles Frame, Resized and ScaleChanged,
/// pointer input (routed events, pointer capture, clicks, element states, the cursor) and the
/// keyboard (focus, Tab order, routed key events, Enter/Space activation).
[<Sealed>]
type ElementHost internal (window: IWindow, device: IRenderDevice, text: TextSystem, options: ElementHostOptions) =
    let surface = device.CreateSurface(window.Surface, window.ClientSize)
    // ValueNone: the theme's background (see `ClearColor`).
    let mutable currentBackground = options.Background
    /// The clear colour the window was last told of (`IWindow.SetBackground`).
    let mutable toldBackground: Color voption = ValueNone
    let sharedValues = match options.AppValues with ValueSome v -> v | ValueNone -> AppValues()
    do options.Theme |> ValueOption.iter (fun t -> sharedValues.Set(Theme.current, t))
    do options.Toasts |> ValueOption.iter (fun p -> sharedValues.Set(Toasts.placement, p))
    /// What this host offers its trees' components and its program's commands (its toasts and dialogs).
    let services = HostServices()
    do sharedValues.Services <- box services
    let mutable stats = { LayoutMilliseconds = 0.0; RenderMilliseconds = 0.0; Shapes = 0 }
    let runBeforeFrame = options.BeforeFrame
    let now =
        match options.Clock with
        | ValueSome f -> f
        | ValueNone ->
            let stopwatch = Stopwatch.StartNew()
            fun () -> stopwatch.Elapsed
    let started = now ()

    /// True while a frame is being prepared and drawn: changes made then (an update patched in
    /// before layout) are drawn by this frame, so they don't need to ask for another.
    let mutable inFrame = false

    /// Bottom to top.
    let layers = List<Layer>()
    let pointers = Dictionary<int, LayerPointer>()
    /// The layer last pressed in: it gets the keyboard (unless a modal layer is above it).
    let mutable pressedLayer: Layer voption = ValueNone
    /// The layer that had the keyboard at the last check (its focus looks active).
    let mutable keyboardOwner: Layer voption = ValueNone
    let mutable windowActive = true
    /// Keys looked at before any layer (tools such as the inspector): true stops the key there.
    let keyPreviews = List<Key -> Modifiers -> bool>()
    let frameDrawn = Event<FrameStats>()
    let layerChanged = Event<Layer>()
    let closed = Event<unit>()
    let mutable isClosed = false

    let dips (p: PixelPoint) = Point(p.X / window.Scale, p.Y / window.Scale)
    let same (a: Layer) (b: Layer) = obj.ReferenceEquals(a, b)
    let isIn (layer: Layer) = layers.Contains layer

    /// Anchors whose flyout may have opened or closed, and the layer each is in (`IElementRoot.Watch`).
    let watched = List<struct (Element * Layer)>()
    /// Shown flyouts, in opening order (their layers stack the same way).
    let openFlyouts = List<OpenFlyout>()
    /// Pointers whose press closed a flyout: their release goes nowhere either.
    let swallowed = HashSet<int>()

    /// A flyout is styled as if inside its element: when that element (or something around it) restyles, so does the
    /// flyout (a class added, the pointer over it, `styles` or `theme` changed above it).
    let restyleOwned (e: Element) =
        for f in openFlyouts do
            if obj.ReferenceEquals(e, f.Anchor) || e.Contains f.Anchor then
                match f.Layer.Tree.Root with
                | ValueSome r -> (f.Layer.Tree :> IElementRoot).RequestRestyle(box r, true, false)
                | ValueNone -> ()

    /// What the window was last told of text input (`IWindow.SetTextInput`), and the check that tells it again
    /// when that has changed (set below, once the keyboard's layer can be found).
    let mutable sentTextInput: TextInputRequest voption = ValueNone
    let mutable syncTextInput: unit -> unit = ignore
    /// An element in a layer asks for a layer of its own (set below, once layers can be added).
    let mutable elementLayerAsked: Layer -> Element -> (unit -> obj) -> unit = fun _ _ _ -> ()

    let add (layer: Layer) =
        let tree = layer.Tree
        tree.ClearCompositionHandler <- fun () -> window.ClearComposition()
        tree.ElementLayerHandler <- fun e content -> elementLayerAsked layer e content
        layer.Input.FocusMoved <- fun () -> syncTextInput ()
        tree.WatchHandler <- fun e -> watched.Add(struct (e, layer))
        tree.RestyleHandler <- restyleOwned
        tree.Scale <- window.Scale
        tree.Dispatcher <- options.Dispatcher
        tree.AppValues <- sharedValues
        tree.Services <- box services
        if layer.UsesAppStyles then
            tree.Styles <- options.Styles
            tree.Theme <- ValueSome(sharedValues.Get Theme.current)
        tree.OnInvalidated <-
            fun () ->
                layerChanged.Trigger layer
                if not inFrame then window.RequestFrame()
        // After everything with the same or a lower order.
        layers.Insert(layers.FindLastIndex(fun l -> l.Order <= layer.Order) + 1, layer)
        layer.InitialView |> ValueOption.iter layer.Update
        window.RequestFrame()

    let app = Layer.Create(text, [ Layer.name "App"; Layer.order 0 ], window.SetCursor)

    // The window's theme follows the app value: layers that take app styles get the new one (their
    // trees re-style and ask for a frame; the background follows in that frame).
    let themeSubscription =
        sharedValues.Subscribe(
            Theme.current.Id,
            fun () ->
                let theme = ValueSome(sharedValues.Get Theme.current)
                for l in layers do
                    if l.UsesAppStyles then l.Tree.Theme <- theme
                window.RequestFrame()
        )

    /// A modal layer blocks what's below it while it shows something.
    let isBlocking (l: Layer) = l.IsModal && l.Tree.HasVisibleContent

    /// The layer that gets the pointer at `p`: the topmost modal layer, or the topmost one with an
    /// element there, whichever is higher; the app if none.
    let layerAt (p: Point) =
        let mutable i = layers.Count - 1
        let mutable found = ValueNone
        while found.IsNone && i >= 0 do
            let l = layers[i]
            if isBlocking l || l.Tree.HitTest(p.X, p.Y).IsSome then found <- ValueSome l
            i <- i - 1
        found |> ValueOption.defaultValue app

    /// The layer that gets the wheel at `p`: as `layerAt`, passing over layers that let it through.
    let wheelLayerAt (p: Point) =
        let mutable i = layers.Count - 1
        let mutable found = ValueNone
        while found.IsNone && i >= 0 do
            let l = layers[i]
            if isBlocking l || (not l.LetsWheelThrough && l.Tree.HitTest(p.X, p.Y).IsSome) then found <- ValueSome l
            i <- i - 1
        found |> ValueOption.defaultValue app

    let addLayer (layer: Layer) =
        // A layer holding a press sees nothing under it where another layer has the pointer.
        layer.Input.IsCovered <- fun p -> not (same (layerAt p) layer)
        add layer

    do addLayer app

    /// The layer keys go to: the one last pressed in (while it shows something; else the app),
    /// unless a modal layer is above it.
    let keyboardLayer () =
        let pressed =
            match pressedLayer with
            | ValueSome l when isIn l && l.Tree.HasVisibleContent -> l
            | _ -> app
        let topModal = layers.FindLastIndex(fun l -> isBlocking l)
        if topModal > layers.IndexOf pressed then layers[topModal] else pressed

    // The window is told when the focused element takes typed text, and where its caret is: when the focus moves
    // (at once: a phone shows its keyboard only from inside the touch that asked for it) and after every frame
    // (the caret moved). An element that hasn't said where its caret is counts as its whole box (DESIGN.md, "9d",
    // decisions 50 and 51).
    do
        syncTextInput <-
            fun () ->
                let layer = keyboardLayer ()
                let request =
                    match layer.Focused with
                    | ValueSome e when layer.Input.WantsText ->
                        let scale = window.Scale
                        let origin = e.PositionInTree
                        let struct (rect, keyboard, multiline) =
                            match layer.Tree.TextArea with
                            | ValueSome(struct (element, rect, keyboard, multiline)) when obj.ReferenceEquals(element, e) ->
                                struct (rect, keyboard, multiline)
                            // No caret of its own (a list that jumps to what is typed, an element with
                            // `onTextInput`): typed letters, but no on-screen keyboard (DESIGN.md, "9e", decision 67).
                            | _ -> struct (Rect.Create(0.0, 0.0, e.Bounds.Width, e.Bounds.Height), KeyboardKind.None, false)
                        ValueSome
                            { CaretX = (origin.X + rect.X) * scale
                              CaretY = (origin.Y + rect.Y) * scale
                              CaretWidth = rect.Width * scale
                              CaretHeight = rect.Height * scale
                              Keyboard = keyboard
                              Multiline = multiline }
                    | _ -> ValueNone
                if request <> sentTextInput then
                    sentTextInput <- request
                    window.SetTextInput request

    /// Only the keyboard's layer shows its focus as active (ring, held Space); the others look
    /// like an inactive window.
    let updateKeyboardOwner () =
        let current = keyboardLayer ()
        match keyboardOwner with
        | ValueSome l when same l current -> ()
        | previous ->
            previous |> ValueOption.iter (fun l -> if isIn l then l.Input.WindowFocusChanged false)
            keyboardOwner <- ValueSome current
            current.Input.WindowFocusChanged windowActive

    let removeLayer (layer: Layer) =
        if layers.Remove layer then
            for s in pointers.Values do
                match s.Over with
                | ValueSome l when same l layer -> s.Over <- ValueNone
                | _ -> ()
                match s.Capture with
                | ValueSome l when same l layer ->
                    s.Capture <- ValueNone
                    s.Buttons <- 0
                | _ -> ()
            layer.Tree.Root <- ValueNone
            window.RequestFrame()

    // --- Flyouts (DESIGN.md, "M8 step 4", round 1) -------------------------------------------------

    /// Above the app's own layers: modals (and the dialogs shown in them), then flyouts (one opened in a modal shows over it), then toasts (an error
    /// stays in sight over a modal); all below the inspector.
    let modalOrder = System.Int32.MaxValue - 6
    let flyoutOrder = System.Int32.MaxValue - 5
    /// Layers elements ask for (a TextBox's selection handles): a press in one closes no flyout.
    let elementLayerOrder = System.Int32.MaxValue - 4
    let toastOrder = System.Int32.MaxValue - 3
    let flyoutGap = 4.0

    let flyoutOf (anchor: Element) = openFlyouts |> Seq.tryFind (fun f -> obj.ReferenceEquals(f.Anchor, anchor))
    let flyoutIn (layer: Layer) = openFlyouts |> Seq.tryFind (fun f -> same f.Layer layer)

    /// Whether presses elsewhere and Escape close it: the Flyout's own setting (styles included).
    let lightDismissOf (f: OpenFlyout) =
        match f.Layer.Tree.Root with
        // A modal: whether Escape asks to close it (the box is the scrim's child).
        | ValueSome root when f.IsModal -> root.Children.Count = 0 || root.Children[0].Get ModalProps.closeOnEscape
        | ValueSome root -> root.Get FlyoutProps.lightDismiss
        | _ -> true

    let flyoutView (f: OpenFlyout) (content: View) =
        // A modal: the box on its scrim, clear until it has been laid out once (then it fades in).
        if f.IsModal then Scrim.create [ Scrim.opacity (if f.Entered then 1.0 else 0.0); Scrim.content content ]
        // Hidden while its anchor doesn't show (it closes after the frame).
        elif f.Shown then content
        else content.With(LayoutProps.isVisible, box false)

    /// Asks the anchor to close its flyout (an uncontrolled one closes; a controlled one's view decides). During a
    /// frame, after it, so what the handler changes asks for the next frame.
    let dismiss (f: OpenFlyout) =
        let a = f.Anchor
        if inFrame then
            match a.Root with
            | ValueSome r -> r.AfterFrame(fun () -> Flyouts.request a false)
            | ValueNone -> ()
        else Flyouts.request a false

    /// A context menu's anchor stands in a layer of its own: the layer that asked for the menu, and what had the
    /// focus there, which get the keyboard and the focus back when it closes.
    let mutable contextReturn: struct (Layer * Element voption) voption = ValueNone
    /// The layer context menus' anchors stand in (made with the first one; see "Context menus" below).
    let mutable contextLayer: Layer voption = ValueNone

    let closeFlyout (f: OpenFlyout) =
        let hadKeys = same (keyboardLayer ()) f.Layer
        openFlyouts.Remove f |> ignore
        removeLayer f.Layer
        let struct (returnLayer, returnFocus) =
            match f.ReturnTo with
            | ValueSome back -> back
            | ValueNone -> struct (f.AnchorLayer, f.ReturnFocus)
        if hadKeys && isIn returnLayer then
            pressedLayer <- ValueSome returnLayer
            match returnFocus with
            | ValueSome e when e.Root.IsSome -> returnLayer.Input.Restore(e, f.Layer.Input.KeyboardMode)
            | _ -> ()

    /// Opens and closes the flyouts whose anchors changed.
    let syncFlyouts () =
        while watched.Count > 0 do
            let batch = watched.ToArray()
            watched.Clear()
            for struct (anchor, anchorLayer) in batch do
                let want = anchor.Root.IsSome && isIn anchorLayer && Flyouts.hasFlyout anchor && Flyouts.isOpen anchor
                match flyoutOf anchor, want with
                | None, true ->
                    // A Modal in the view stands where a flyout's anchor would: its layer is modal and fills the window.
                    let isModal = obj.ReferenceEquals(anchor.Type, ModalAnchor.Type)
                    let layer =
                        Layer.Create(
                            text,
                            [ if isModal then
                                  Layer.name "Modal"
                                  Layer.order modalOrder
                                  Layer.modal true
                              else
                                  Layer.name ("Flyout of " + (if anchor.Name <> "" then anchor.Name else anchor.Type.Name))
                                  Layer.order flyoutOrder
                              Layer.appStyles anchorLayer.UsesAppStyles ],
                            window.SetCursor)
                    layer.Tree.Owner <- ValueSome anchor
                    let f = OpenFlyout(anchor, anchorLayer, layer, anchorLayer.Focused)
                    f.IsModal <- isModal
                    // A context menu: its anchor's layer is only where it stands.
                    match contextLayer with
                    | ValueSome c when same c anchorLayer -> f.ReturnTo <- contextReturn
                    | _ -> ()
                    // The Flyout is the layer's root, placed beside the anchor where it is now (the tree lays out its root every
                    // frame, and the anchor moving brings one).
                    if not isModal then
                        layer.Tree.Place <- ValueSome(fun root window -> FlyoutPlacement.flyout f.Rect flyoutGap root window)
                    // The ring shows inside if the keyboard was in use where the keys were (usually the anchor's layer; a
                    // dialog's anchor stands in a layer of its own, which never has the keyboard).
                    f.Ring <- (keyboardLayer ()).Input.KeyboardMode
                    openFlyouts.Add f
                    addLayer layer
                    if not anchorLayer.UsesAppStyles then layer.Tree.Theme <- anchorLayer.Tree.Theme
                | Some f, false -> closeFlyout f
                | _ -> ()

    /// Before a flyout's layer is laid out (its anchor's layer is below it, so laid out already): follows the anchor,
    /// and asks to close when the anchor no longer shows (scrolled out, hidden); closes when it went away.
    let refreshFlyout (f: OpenFlyout) =
        let a = f.Anchor
        match (if a.Root.IsSome then a.Get FlyoutProps.flyout else ValueNone) with
        | ValueNone -> closeFlyout f
        | ValueSome content ->
            let p = a.PositionInTree
            let b = a.Bounds
            let rect = Rect.Create(p.X, p.Y, b.Width, b.Height)
            let visible = a.VisibleBounds
            // A modal shows wherever it stands in the view (which has no size).
            let shown = f.IsModal || (visible.Width > 0.0 && visible.Height > 0.0)
            let changed =
                f.Content.IsNone || not (obj.ReferenceEquals(f.Content.Value, content)) || f.Rect <> rect || f.Shown <> shown
            if changed then
                f.Content <- ValueSome content
                f.Rect <- rect
                f.Shown <- shown
                f.Layer.Update(flyoutView f content)
                if not shown then dismiss f

    /// A press at `p` closes the flyouts it's outside of (and outside their anchors), from the top down to the first
    /// it's in; pinned ones stay. True if one closed: the press goes no further.
    let lightDismissAt (p: Point) =
        let mutable dismissed = false
        if openFlyouts.Count > 0 && (layerAt p).Order <= flyoutOrder then
            let mutable stop = false
            let mutable i = openFlyouts.Count - 1
            while not stop && i >= 0 do
                let f = openFlyouts[i]
                let v = f.Anchor.VisibleBounds
                let onAnchor = p.X >= v.X && p.X < v.X + v.Width && p.Y >= v.Y && p.Y < v.Y + v.Height
                if f.IsModal then
                    // Nothing below a modal is reached. A press on its scrim (not the box) asks to close it, if it says so.
                    stop <- true
                    match f.Layer.Tree.Root, f.Layer.Tree.HitTest(p.X, p.Y) with
                    | ValueSome root, ValueSome hit when obj.ReferenceEquals(root, hit) && root.Children.Count > 0 && root.Children[0].Get ModalProps.lightDismiss ->
                        dismiss f
                        dismissed <- true
                    | _ -> ()
                elif onAnchor || f.Layer.Tree.HitTest(p.X, p.Y).IsSome then stop <- true
                elif lightDismissOf f then
                    dismiss f
                    dismissed <- true
                i <- i - 1
        dismissed

    /// Escape that nothing handled closes the top flyout light dismiss can close (unless keys are in a layer above).
    let dismissTopFlyout () =
        if (keyboardLayer ()).Order > flyoutOrder then false
        else
            match openFlyouts |> Seq.tryFindBack (fun f -> lightDismissOf f) with
            | Some f ->
                dismiss f
                true
            | None -> false

    // --- Layers elements ask for (DESIGN.md, "9g": a TextBox's selection handles and row) ----------

    let elementLayers = List<ElementLayer>()

    let elementLayerIn (layer: Layer) = elementLayers |> Seq.tryFind (fun x -> same x.Layer layer)

    let closeElementLayer (x: ElementLayer) =
        elementLayers.Remove x |> ignore
        removeLayer x.Layer

    do
        elementLayerAsked <-
            fun ownerLayer owner content ->
                match elementLayers |> Seq.tryFind (fun x -> obj.ReferenceEquals(x.Owner, owner)) with
                | Some x ->
                    x.Content <- content
                    window.RequestFrame()
                | None ->
                    let layer =
                        Layer.Create(
                            text,
                            [ Layer.name ("Above " + (if owner.Name <> "" then owner.Name else owner.Type.Name))
                              Layer.order elementLayerOrder
                              Layer.keyboard false
                              Layer.appStyles ownerLayer.UsesAppStyles ],
                            window.SetCursor)
                    // Styled as if inside its element, as a flyout is.
                    layer.Tree.Owner <- ValueSome owner
                    elementLayers.Add(ElementLayer(owner, ownerLayer, layer, content))
                    addLayer layer
                    if not ownerLayer.UsesAppStyles then layer.Tree.Theme <- ownerLayer.Tree.Theme

    /// Before an element's layer is laid out (its element's layer is below it, so laid out already): it shows what
    /// the element gives now, and goes when the element gives nothing, left its tree, or is under a modal.
    let refreshElementLayer (x: ElementLayer) =
        let owner = x.Owner
        let ownerIndex = layers.IndexOf x.OwnerLayer
        let attached = ownerIndex >= 0 && (match owner.Root with ValueSome r -> obj.ReferenceEquals(r, x.OwnerLayer.Tree) | ValueNone -> false)
        let covered = attached && layers.FindLastIndex(fun l -> isBlocking l) > ownerIndex
        let view =
            if not attached || covered then null
            else
                try
                    x.Content ()
                with ex ->
                    owner.ReportError ex
                    null
        match view with
        | :? View as view ->
            match x.Shown with
            | ValueSome shown when obj.ReferenceEquals(shown, view) -> ()
            | _ ->
                x.Shown <- ValueSome view
                x.Layer.Update view
        | _ -> closeElementLayer x

    // --- Context menus (DESIGN.md, "9f", decisions 71 and 73) --------------------------------------

    /// Where a context menu's anchor stands: a layer of its own that shows nothing and never takes the keyboard,
    /// holding a one-DIP element at the pointer whose flyout is the menu (as a dialog's Modal stands in the
    /// Dialogs layer). So a context menu is placed, kept in the window, closed and styled as any flyout is.
    let emptyContext = Canvas.create [ Canvas.isHitTestVisible false ]

    let closeContext () =
        match contextLayer with
        | ValueSome layer -> layer.Update emptyContext
        | ValueNone -> ()

    /// Shows `menu` at `p` (window DIPs). The keyboard and the focus go back to `from` when it closes.
    let openContext (from: Layer) (menu: View) (p: Point) =
        let layer =
            match contextLayer with
            | ValueSome layer -> layer
            | ValueNone ->
                let layer = Layer.Create(text, [ Layer.name "Context menu"; Layer.keyboard false ], window.SetCursor)
                addLayer layer
                contextLayer <- ValueSome layer
                layer
        contextReturn <- ValueSome(struct (from, from.Focused))
        layer.Update(
            Canvas.create [
                Canvas.isHitTestVisible false
                Canvas.children [
                    Border.create [
                        Canvas.left p.X
                        Canvas.top p.Y
                        Border.width 1.0
                        Border.height 1.0
                        Border.isHitTestVisible false
                        Border.flyout [
                            Flyout.isOpen true
                            Flyout.onOpenChanged (fun isOpen -> if not isOpen then closeContext ())
                            Flyout.content menu
                        ]
                    ]
                ]
            ]
        )
        window.RequestFrame()

    /// The context menu of the nearest element that has one, from `start` outward: its `contextMenu` setting, else
    /// its type's own (a TextBox's).
    let contextMenuOf (start: Element) : View voption =
        let rec find (e: Element) =
            let own =
                if not e.IsEnabled then ValueNone
                else
                    match e.Get FlyoutProps.contextMenu with
                    | ValueSome menu -> ValueSome menu
                    | ValueNone ->
                        match e.Type.ContextMenu with
                        | ValueSome f ->
                            try
                                f (InputContext e)
                            with ex ->
                                e.ReportError ex
                                ValueNone
                        | ValueNone -> ValueNone
            match own, e.Parent with
            | ValueSome menu, _ -> ValueSome menu
            | ValueNone, ValueSome parent -> find parent
            | ValueNone, ValueNone -> ValueNone
        find start

    /// A right-click or a long press at `p` in `layer`: the menu of what is there, if it has one. True if it opened.
    let contextAt (layer: Layer) (p: Point) =
        match layer.Tree.HitTest(p.X, p.Y) |> ValueOption.bind contextMenuOf with
        | ValueSome menu ->
            openContext layer menu p
            true
        | ValueNone -> false

    /// The Menu key or Shift+F10: the menu of the focused element, at its caret if it has said where that is (a
    /// TextBox), else just inside its bottom left corner.
    let contextForFocus () =
        let layer = keyboardLayer ()
        match layer.Focused with
        | ValueSome e ->
            match contextMenuOf e with
            | ValueSome menu ->
                let origin = e.PositionInTree
                let p =
                    match layer.Tree.TextArea with
                    | ValueSome(struct (element, rect, _, _)) when obj.ReferenceEquals(element, e) ->
                        Point(origin.X + rect.X, origin.Y + rect.Y + rect.Height)
                    | _ -> Point(origin.X + min 8.0 e.Bounds.Width, origin.Y + max 0.0 (e.Bounds.Height - 4.0))
                openContext layer menu p
                true
            | ValueNone -> false
        | ValueNone -> false

    /// A finger held still this long asks for the context menu of what it is on.
    let longPressTime = System.TimeSpan.FromMilliseconds 500.0
    /// It may move this far (DIPs) and still be a long press.
    let longPressSlop = 8.0
    /// The finger being timed: its pointer, where it went down, and a number that a later press or a move changes.
    let mutable longPress: struct (int * Point * int) voption = ValueNone
    let mutable longPressGeneration = 0

    // --- Toasts and dialogs (DESIGN.md, "4d", round 3): this window's own -------------------

    /// Their state, made (with its layer) the first time something asks for it.
    let mutable toastStack: ToastStack voption = ValueNone
    let mutable dialogQueue: DialogQueue voption = ValueNone

    let uiDispatcher (what: string) =
        match options.Dispatcher with
        | ValueSome d -> d
        | ValueNone -> invalidOp (what + " need the host's dispatcher (ElementHost.dispatcher).")

    let toastsOf () =
        match toastStack with
        | ValueSome s -> s
        | ValueNone ->
            let dispatcher = uiDispatcher "Toasts"
            let layer = Layer.Create(text, [ Layer.name "Toasts"; Layer.order toastOrder; Layer.keyboard false ], window.SetCursor)
            addLayer layer
            let s = ToastStack(dispatcher, layer.Update, fun () -> sharedValues.Get Toasts.placement)
            // The placement is an app value: when it changes, the toasts move.
            sharedValues.Subscribe(Toasts.placement.Id, fun () -> s.Refresh()) |> ignore
            toastStack <- ValueSome s
            s

    let dialogsOf () =
        match dialogQueue with
        | ValueSome q -> q
        | ValueNone ->
            let dispatcher = uiDispatcher "Dialogs"
            // A place for the dialog's Modal to stand: this layer shows nothing itself and never takes the keyboard;
            // each dialog opens in its own modal layer, like any Modal.
            let layer = Layer.Create(text, [ Layer.name "Dialogs"; Layer.keyboard false ], window.SetCursor)
            addLayer layer
            let q = DialogQueue(dispatcher, layer.Update)
            layer.Update DialogQueue.Nothing
            dialogQueue <- ValueSome q
            closed.Publish.Add(fun () -> q.CancelAll())
            q

    let toaster = Toaster((fun () -> options.Dispatcher), fun () -> ValueSome(toastsOf ()))
    let dialogs = Dialogs((fun () -> options.Dispatcher), fun () -> ValueSome(dialogsOf ()))

    // What components in this host's trees and its program's commands reach them through.
    do
        services.FindToasts <- fun () -> ValueSome(toastsOf ())
        services.FindDialogs <- fun () -> ValueSome(dialogsOf ())

    // --- Tooltips (DESIGN.md, "M8 step 4", round 1, 8) ---------------------------------------------

    /// The element whose tooltip shows (or is waiting to), and the layer showing it.
    let mutable tipOwner: Element voption = ValueNone
    let mutable tipLayer: Layer voption = ValueNone
    /// Bumped whenever the pointer moves or the tooltip goes: a wait that sees another number is stale.
    let mutable tipGeneration = 0
    /// When the last tooltip went (the next shows at once within `tipWarm`).
    let mutable tipHiddenAt: System.TimeSpan voption = ValueNone
    /// Pressed while its tooltip showed or waited: no tooltip again until the pointer leaves it.
    let mutable tipSuppressed: Element voption = ValueNone
    let tipDelay = System.TimeSpan.FromMilliseconds 500.0
    let tipWarm = System.TimeSpan.FromMilliseconds 300.0
    /// Below the pointer: about a cursor's height down.
    let tipCursorHeight = 16.0

    let hasTip (e: Element) = e.Get FlyoutProps.tooltip <> "" || (e.Get FlyoutProps.tooltipView).IsSome

    /// The nearest element under `p` (or around what's there) with a tooltip.
    let tipAt (p: Point) =
        let mutable e = (layerAt p).Tree.HitTest(p.X, p.Y)
        while e.IsSome && not (hasTip e.Value) do
            e <- e.Value.Parent
        e

    let hideTip () =
        tipGeneration <- tipGeneration + 1
        match tipLayer with
        | ValueSome l ->
            removeLayer l
            tipLayer <- ValueNone
            tipHiddenAt <- ValueSome(now ())
        | ValueNone -> ()
        tipOwner <- ValueNone

    let showTip (owner: Element) (p: Point) =
        let content =
            match owner.Get FlyoutProps.tooltipView with
            | ValueSome v -> v
            | ValueNone -> TextBlock.create [ TextBlock.text (owner.Get FlyoutProps.tooltip) ]
        let mutable top = owner
        while top.Parent.IsSome do
            top <- top.Parent.Value
        let ownerLayer = layers |> Seq.tryFind (fun l -> l.Tree.Root.IsSome && obj.ReferenceEquals(l.Tree.Root.Value, top))
        let usesAppStyles = ownerLayer |> Option.map _.UsesAppStyles |> Option.defaultValue true
        let layer = Layer.Create(text, [ Layer.name "Tooltip"; Layer.order System.Int32.MaxValue; Layer.appStyles usesAppStyles ], window.SetCursor)
        layer.Tree.Owner <- ValueSome owner
        addLayer layer
        match ownerLayer with
        | Some l when not usesAppStyles -> layer.Tree.Theme <- l.Tree.Theme
        | _ -> ()
        // Below the pointer, kept inside the window.
        let below = Rect.Create(p.X, p.Y, 0.0, tipCursorHeight)
        layer.Tree.Place <-
            ValueSome(fun root window ->
                let w, h = min root.DesiredSize.Width window.Width, min root.DesiredSize.Height window.Height
                FlyoutPlacement.place below Placement.Below FlyoutAlignment.Start 0.0 w h window.Width window.Height)
        layer.Update(TooltipPresenter.create [ TooltipPresenter.content content ])
        tipLayer <- ValueSome layer

    /// The mouse moved to `p` (no button down): the tooltip under it waits, shows, stays or goes.
    let tipMoved (p: Point) =
        let under = tipAt p
        match tipSuppressed with
        | ValueSome s when not (under.IsSome && obj.ReferenceEquals(under.Value, s)) -> tipSuppressed <- ValueNone
        | _ -> ()
        match under, tipOwner with
        | ValueSome u, ValueSome o when obj.ReferenceEquals(u, o) && tipLayer.IsSome -> ()
        | _ ->
            let wasShown = tipLayer.IsSome
            hideTip ()
            match under with
            | ValueSome u when tipSuppressed.IsNone ->
                tipOwner <- ValueSome u
                if wasShown || (tipHiddenAt.IsSome && now () - tipHiddenAt.Value <= tipWarm) then showTip u p
                else
                    match options.Dispatcher with
                    | ValueSome d ->
                        let generation = tipGeneration
                        d.PostAfter(
                            tipDelay,
                            fun () ->
                                if generation = tipGeneration && tipLayer.IsNone && u.Root.IsSome && hasTip u then showTip u p
                        )
                    | ValueNone -> ()
            | _ -> ()

    /// A press, the wheel or a key: the tooltip goes, and stays away from that element until the pointer leaves it.
    let tipInterrupted () =
        if tipOwner.IsSome then
            tipSuppressed <- tipOwner
            hideTip ()

    let pointerFor (kind: PointerKind) (id: int) =
        match pointers.TryGetValue id with
        | true, s -> s
        | _ ->
            let s = LayerPointer(kind)
            pointers[id] <- s
            s

    /// Moves a pointer that isn't pressed onto `layer`: the layer it leaves sees it go.
    let moveTo (s: LayerPointer) (id: int) (layer: Layer) =
        match s.Over with
        | ValueSome old when same old layer -> false
        | old ->
            old |> ValueOption.iter (fun o -> if isIn o then o.Input.PointerExited(s.Kind, id))
            s.Over <- ValueSome layer
            true

    /// The layer a pointer's events go to: the one holding its press, else the one under it.
    let targetFor (s: LayerPointer) (id: int) (p: Point) =
        match s.Capture with
        | ValueSome c when isIn c -> c
        | _ ->
            let l = layerAt p
            moveTo s id l |> ignore
            l

    /// After layout, or when the last button comes up: a pointer that stood still may now be
    /// over another layer (one appeared, went away or changed).
    let reassign (id: int) (s: LayerPointer) =
        match s.Position with
        | ValueSome p when s.Buttons = 0 ->
            let l = layerAt p
            if moveTo s id l then l.Input.PointerAt(s.Kind, id, p, s.Modifiers)
        | _ -> ()

    /// Cleared behind the layers every frame, instead of the theme's background (the default).
    static member background(value: Color) = ElementHostSetting(fun o -> { o with Background = ValueSome value })

    /// The UI thread's dispatcher: components use it for state set from other threads and to
    /// report errors from async work. Without one, state must be set on the UI thread.
    static member dispatcher(value: Dispatcher) = ElementHostSetting(fun o -> { o with Dispatcher = ValueSome value })

    /// Runs at the start of every frame, before layout: the place to advance animations (which
    /// change properties and so ask for the next frame themselves).
    static member beforeFrame(handler: unit -> unit) = ElementHostSetting(fun o -> { o with BeforeFrame = handler })

    /// Where animation time comes from: time since some fixed start, from a monotonic clock
    /// (App.start passes the platform's `Elapsed`). Without it, a stopwatch.
    static member clock(now: unit -> System.TimeSpan) = ElementHostSetting(fun o -> { o with Clock = ValueSome now })

    /// Styles for every layer (except those with `Layer.appStyles false`); `App.styles` sets them.
    static member styles(value: Style list) = ElementHostSetting(fun o -> { o with Styles = value })

    /// The starting window theme (`Theme.current` in its app values), for every layer except those
    /// with `Layer.appStyles false`; `App.theme` sets it. Default: `Theme.dark []`.
    static member theme(value: Theme) = ElementHostSetting(fun o -> { o with Theme = ValueSome value })

    /// Where the toasts stack at the start: `ElementHost.toasts [ Toasts.verticalAlignment VerticalAlignment.Top ]`
    /// (it is the starting value of `Toasts.placement`, an app value).
    static member toasts(settings: ToastsSetting list) = ElementHostSetting(fun o -> { o with Toasts = ValueSome(Toasts.From settings) })

    /// The app values its layers share (`App.run` gives the app's, shared by its windows). Default: its own.
    static member appValues(value: AppValues) = ElementHostSetting(fun o -> { o with AppValues = ValueSome value })

    /// The app values every layer shares (`ctx.useAppValue`).
    member _.AppValues = sharedValues

    member _.Window = window

    /// The GPU device it draws with, and its text system (for tools that open a window of their own).
    member _.Device = device
    member _.Text = text

    /// Raises `Closed`, then closes the window (once). App.start calls it when the window is asked to
    /// close; call it instead of `Window.Close()` so everything attached hears about it.
    member _.Close() =
        if not isClosed then
            isClosed <- true
            // First, so windows owned by this one (an inspector pop-out) close while it still exists.
            closed.Trigger()
            window.Close()

    /// When the host closes through `Close()`, just before its window goes.
    member _.Closed: IEvent<unit> = closed.Publish

    /// The app's layer (order 0).
    member _.App = app
    /// The app's tree.
    member _.Tree = app.Tree

    member _.Root
        with get () = app.Tree.Root
        and set (v: Element voption) = app.Tree.Root <- v

    /// All layers, bottom to top (the app's included).
    member _.Layers: IReadOnlyList<Layer> = layers

    /// Adds a layer above the app (or wherever its order puts it) and returns it:
    /// `host.AddLayer [ Layer.order 200; Layer.modal true; Layer.view (dialog ()) ]`.
    member _.AddLayer(settings: LayerSetting list) : Layer =
        let layer = Layer.Create(text, settings, window.SetCursor)
        addLayer layer
        layer

    /// This window's toasts (their layer is added the first time they're asked for: above dialogs and flyouts, below
    /// the inspector; a press in it never takes the keyboard). Components get it with `ctx.useToaster ()`. Needs a
    /// dispatcher (`ElementHost.dispatcher`).
    member _.Toaster = toaster

    /// This window's dialogs (`Inform`, `Confirm`, `Ask`). Components get it with `ctx.useDialogs ()`.
    /// What's open or waiting is cancelled when the host closes. Needs a dispatcher (`ElementHost.dispatcher`).
    member _.Dialogs = dialogs

    /// Takes a layer away (its elements leave the tree, so components stop). Not the app's.
    member _.RemoveLayer(layer: Layer) =
        if same layer app then invalidOp "The app's layer can't be removed."
        removeLayer layer

    /// Cleared behind the layers every frame instead of the theme's background (starts as
    /// `ElementHost.background`; ValueNone follows the theme; may change while running).
    member _.Background
        with get () = currentBackground
        and set v =
            currentBackground <- v
            window.RequestFrame()

    /// The window's theme (`Theme.current`, an app value; `App.theme`/`ElementHost.theme` start it).
    /// It applies to every layer that takes app styles (toasts, dialogs; not the inspector) and
    /// gives the window's background.
    member _.WindowTheme: Theme = sharedValues.Get Theme.current

    /// What the window is cleared to: `Background` if set, else the window theme's background
    /// (`Palette.background`).
    member this.ClearColor =
        match currentBackground with
        | ValueSome c -> c
        | ValueNone -> this.WindowTheme.Get Palette.background

    member _.LastFrame = stats

    /// Time on the host's clock (`ElementHost.clock`) since the host was made: what animations see.
    member _.Elapsed: System.TimeSpan = now () - started

    /// The UI thread's dispatcher (`ElementHost.dispatcher`; App.start sets one).
    member _.Dispatcher = app.Tree.Dispatcher

    /// Shows a view in the app's layer (builds the elements the first time, patches them after that).
    member _.Update(view: View) = app.Update view

    member private this.DrawFrame() =
        let time = now () - started
        for l in layers do
            l.Tree.Time <- time
        runBeforeFrame ()
        let size = window.ClientSize
        let scale = window.Scale
        let started = Stopwatch.GetTimestamp()
        let available: Size = { Width = float size.Width / scale; Height = float size.Height / scale }
        // A tooltip whose element went goes too.
        match tipOwner with
        | ValueSome o when o.Root.IsNone -> hideTip ()
        | _ -> ()
        // Bottom to top, opening and closing flyouts as anchors change; a flyout's layer follows its anchor, which is
        // laid out before it.
        syncFlyouts ()
        let mutable i = 0
        while i < layers.Count do
            let l = layers[i]
            flyoutIn l |> Option.iter refreshFlyout
            elementLayerIn l |> Option.iter refreshElementLayer
            if isIn l then
                l.Tree.Scale <- scale
                l.Tree.UpdateLayout available
                match flyoutIn l with
                | Some f when f.FocusPending && f.Shown ->
                    f.FocusPending <- false
                    pressedLayer <- ValueSome l
                    // The first Tab stop, or in a modal the element that asked for it (`Modal.initialFocus`).
                    let rec asked (e: Element) : Element voption =
                        if e.Get ModalProps.initialFocus && e.Get LayoutProps.focusable && e.IsEnabled then ValueSome e
                        else
                            let mutable found = ValueNone
                            for c in e.Children do
                                if found.IsNone then found <- asked c
                            found
                    match (if f.IsModal then l.Tree.Root |> ValueOption.bind asked else ValueNone) with
                    | ValueSome e -> l.Input.Restore(e, f.Ring)
                    | ValueNone -> l.Input.FocusFirst f.Ring
                    if f.IsModal then
                        // Laid out once, clear: now it fades in (the theme's transition on the scrim's opacity).
                        f.Entered <- true
                        f.Content |> ValueOption.iter (fun content -> l.Update(flyoutView f content))
                        window.RequestFrame()
                | _ -> ()
            syncFlyouts ()
            // A layer that went away starts the walk again (laying out what's laid out is cheap).
            i <- layers.IndexOf l + 1
        // Layout may have moved things under a pointer that stood still (hover, cursor), or put a
        // layer under it, or taken one away.
        for KeyValue(id, s) in List.ofSeq pointers do
            reassign id s
        for l in layers do
            l.Input.Refresh()
        updateKeyboardOwner ()
        let laidOut = Stopwatch.GetTimestamp()
        // A layout that threw (reported already) left the tree half laid out: drop this frame, keep
        // the last one on screen.
        let layoutFailed = layers |> Seq.exists (fun l -> l.Tree.LayoutFailed)
        match (if layoutFailed then None else surface.TryBeginFrame scale) with
        | Some frame ->
            use frame = frame
            let background = this.ClearColor
            frame.Clear background
            // The platform shows the same colour wherever it shows anything of its own around the drawing.
            if ValueSome background <> toldBackground then
                toldBackground <- ValueSome background
                let channel (v: float32) = byte (max 0.0 (min 255.0 (System.Math.Round(float v * 255.0))))
                window.SetBackground(channel background.R, channel background.G, channel background.B)
            for l in layers do
                l.Tree.Render frame.DrawList
            stats <-
                { LayoutMilliseconds = Stopwatch.GetElapsedTime(started, laidOut).TotalMilliseconds
                  RenderMilliseconds = Stopwatch.GetElapsedTime(laidOut).TotalMilliseconds
                  Shapes = frame.DrawList.Count }
        | None -> ()

    /// The layer that gets keys now.
    member _.KeyboardLayer = keyboardLayer ()

    /// Sees every key going down before any layer does, whatever has focus (repeats included);
    /// returning true stops the key there. For tools that need a shortcut that always works (the
    /// inspector's F12). Dispose the result to stop.
    member _.AddKeyPreview(handler: Key -> Modifiers -> bool) : System.IDisposable =
        keyPreviews.Add handler
        { new System.IDisposable with
            member _.Dispose() = keyPreviews.Remove handler |> ignore }

    /// After each frame is drawn (and its effects have run), with the frame's timing.
    member _.FrameDrawn: IEvent<FrameStats> = frameDrawn.Publish

    /// When something in a layer changed (layout, looks or state: anything that asks for a frame,
    /// including changes made while a frame is drawn). Fires often; keep handlers cheap.
    member _.LayerChanged: IEvent<Layer> = layerChanged.Publish

    /// The element with keyboard focus (in the layer that gets keys), if any.
    member _.Focused: Element voption = (keyboardLayer ()).Focused

    /// Moves keyboard focus as Tab (Next) or Shift+Tab (Previous) would, in the layer that gets keys.
    member _.MoveFocus(direction: FocusDirection) = (keyboardLayer ()).MoveFocus direction

    /// Handles a window event. Returns true for a key that an element (or built-in behavior such
    /// as Tab or a focused button's Enter) handled; the app's own key handling skips those.
    member this.HandleEvent(event: PlatformEvent) : bool =
        // What input handlers read as the time is the time of the input, not of the last frame (which may be long
        // ago in an idle window): a double-click is two presses close together on this clock.
        match event with
        | PointerPressed _
        | KeyDown _
        | TextInput _
        | TextComposition _ ->
            let time = now () - started
            for l in layers do
                l.Tree.Time <- time
        | _ -> ()
        match event with
        | Frame ->
            inFrame <- true
            try
                this.DrawFrame()
#if DEBUG
                // What a frame asks for is ignored (it's handled by that frame), so styles queued
                // during it wait until something else asks for a frame. Nothing should do that
                // (DESIGN.md, "Recycling measured; ...", decision 8); say who did.
                for l in layers do
                    match l.Tree.FirstQueuedRestyle with
                    | ValueSome e ->
                        Debug.WriteLine(
                            "Scribble: " + e.Type.Name + (if e.Name <> "" then " #" + e.Name else "") + " in layer '" + l.Name
                            + "' still has styles to match after its frame; they apply only when something else asks for a frame.")
                    | ValueNone -> ()
#endif
            finally
                inFrame <- false
            // Effects and "keep animating" requests: outside the frame, so what they change asks
            // for the next one.
            for l in layers.ToArray() do
                l.Tree.RunAfterFrame()
            options.Dispatcher |> ValueOption.iter (fun d -> d.FrameEnded())
            // The caret may have moved: the window hears where it is now.
            syncTextInput ()
            frameDrawn.Trigger stats
            false
        | Resized size ->
            surface.Resize size
            // A phone's keyboard came up or went (the window is what it leaves): what is being typed in stays in
            // view (DESIGN.md, "9d", decision 58).
            if sentTextInput.IsSome then (keyboardLayer ()).Focused |> ValueOption.iter (fun e -> e.BringIntoView())
            false
        | ScaleChanged scale ->
            for l in layers do
                l.Tree.Scale <- scale
            false
        | PointerMoved(kind, id, p, mods) ->
            let p = dips p
            // A finger that moves is no long press.
            match longPress with
            | ValueSome(struct (pressed, at, _)) when pressed = id && (abs (p.X - at.X) > longPressSlop || abs (p.Y - at.Y) > longPressSlop) ->
                longPress <- ValueNone
            | _ -> ()
            let s = pointerFor kind id
            s.Position <- ValueSome p
            s.Modifiers <- mods
            (targetFor s id p).Input.PointerMoved(kind, id, p, mods)
            // Tooltips: the mouse only (none on touch or pen), and not while a button is down.
            if kind = PointerKind.Mouse && s.Buttons = 0 then tipMoved p
            false
        | PointerPressed(kind, id, p, button, mods) ->
            let p = dips p
            let s = pointerFor kind id
            s.Position <- ValueSome p
            s.Modifiers <- mods
            tipInterrupted ()
            if s.Buttons = 0 && lightDismissAt p then swallowed.Add id |> ignore
            else
                let layer = targetFor s id p
                if s.Buttons = 0 then
                    s.Capture <- ValueSome layer
                    if layer.TakesKeyboard then pressedLayer <- ValueSome layer
                    updateKeyboardOwner ()
                s.Buttons <- s.Buttons + 1
                layer.Input.PointerPressed(kind, id, p, button, mods)
                // A finger or a pen that stays where it went down asks for a context menu (DESIGN.md, "9f", decision 73).
                if kind <> PointerKind.Mouse then
                    longPressGeneration <- longPressGeneration + 1
                    let generation = longPressGeneration
                    longPress <- ValueSome(struct (id, p, generation))
                    match options.Dispatcher with
                    | ValueSome d ->
                        d.PostAfter(
                            longPressTime,
                            fun () ->
                                match longPress with
                                | ValueSome(struct (pressed, at, g)) when g = generation && pressed = id ->
                                    longPress <- ValueNone
                                    let time = now () - started
                                    for l in layers do
                                        l.Tree.Time <- time
                                    // An element type that uses a long press itself keeps the finger (a TextBox
                                    // selects the word under it: DESIGN.md, "9g", decision 100).
                                    if isIn layer && layer.Input.LongPress(kind, id, at) then ()
                                    elif isIn layer && contextAt layer at then
                                        // The menu took the press: no click when the finger lifts.
                                        layer.Input.PointerCanceled(kind, id)
                                        s.Buttons <- 0
                                        s.Capture <- ValueNone
                                        swallowed.Add id |> ignore
                                | _ -> ()
                        )
                    | ValueNone -> ()
            false
        | PointerReleased(_, id, p, _, mods) when swallowed.Contains id && pointers[id].Buttons = 0 ->
            swallowed.Remove id |> ignore
            let s = pointers[id]
            s.Position <- ValueSome(dips p)
            s.Modifiers <- mods
            false
        | PointerReleased(kind, id, p, button, mods) ->
            let p = dips p
            let s = pointerFor kind id
            s.Position <- ValueSome p
            s.Modifiers <- mods
            let layer = targetFor s id p
            s.Buttons <- max 0 (s.Buttons - 1)
            if s.Buttons = 0 then s.Capture <- ValueNone
            longPress <- ValueNone
            layer.Input.PointerReleased(kind, id, p, button, mods)
            // Released over another layer: that one has the pointer now.
            reassign id s
            // A right-click: the context menu of what is under it.
            if kind = PointerKind.Mouse && button = MouseButton.Right && isIn layer then contextAt layer p |> ignore
            false
        | PointerExited(kind, id) ->
            if kind = PointerKind.Mouse then hideTip ()
            let s = pointerFor kind id
            match s.Capture, s.Over with
            | ValueSome l, _
            | ValueNone, ValueSome l -> if isIn l then l.Input.PointerExited(kind, id)
            | _ -> ()
            if s.Buttons = 0 then
                s.Position <- ValueNone
                s.Over <- ValueNone
                if kind <> PointerKind.Mouse then pointers.Remove id |> ignore
            false
        | PointerCanceled(kind, id) ->
            longPress <- ValueNone
            let s = pointerFor kind id
            match s.Capture, s.Over with
            | ValueSome l, _
            | ValueNone, ValueSome l -> if isIn l then l.Input.PointerCanceled(kind, id)
            | _ -> ()
            s.Buttons <- 0
            s.Capture <- ValueNone
            false
        | PointerWheel(p, dx, dy, mods) ->
            let p = dips p
            tipInterrupted ()
            (wheelLayerAt p).Input.Wheel(p, dx, dy, mods)
            false
        | KeyDown(key, mods, isRepeat) ->
            tipInterrupted ()
            let mutable taken = false
            for preview in keyPreviews.ToArray() do
                if not taken then taken <- preview key mods
            taken
            || (keyboardLayer ()).Input.KeyDown(key, mods, isRepeat)
            || (key = Key.Escape && dismissTopFlyout ())
            // The Menu key or Shift+F10 that nothing used: the focused element's context menu.
            || ((key = Key.Menu || (key = Key.F 10 && mods = Modifiers.Shift)) && not isRepeat && contextForFocus ())
        | KeyUp(key, mods) -> (keyboardLayer ()).Input.KeyUp(key, mods)
        | TextInput text -> (keyboardLayer ()).Input.TextInput text
        | TextComposition(text, caret) -> (keyboardLayer ()).Input.TextComposition(text, caret)
        | FocusChanged active ->
            windowActive <- active
            (keyboardLayer ()).Input.WindowFocusChanged active
            false
        | _ -> false

    interface System.IDisposable with
        member _.Dispose() =
            themeSubscription.Dispose()
            surface.Dispose()

module ElementHost =
    /// A host for `window`, drawing with `device` and `text`:
    /// `ElementHost.create window device text [ ElementHost.background (Color.Hex 0x181825) ]`.
    let create (window: IWindow) (device: IRenderDevice) (text: TextSystem) (settings: ElementHostSetting list) =
        let defaults = { Background = ValueNone; Dispatcher = ValueNone; BeforeFrame = ignore; Styles = []; Theme = ValueNone; Toasts = ValueNone; AppValues = ValueNone; Clock = ValueNone }
        let options = settings |> List.fold (fun o (ElementHostSetting apply) -> apply o) defaults
        new ElementHost(window, device, text, options)
