namespace Scribble.Accessibility.AccessKit

#nowarn "9"

open System
open System.Collections.Generic
open System.Runtime.InteropServices
open System.Text
open Microsoft.FSharp.NativeInterop
open Scribble.Accessibility
open Scribble.Core
open Scribble.Platform

/// Writes a tree out to AccessKit, call by call.
module internal Writer =
    let private utf8 (s: string) (set: nativeptr<byte> -> unativeint -> unit) =
        if not (String.IsNullOrEmpty s) then
            let bytes = Encoding.UTF8.GetBytes s
            use p = fixed bytes
            set p (unativeint bytes.Length)

    let private flag (value: bool) = if value then 1uy else 0uy

    let node (n: AkNode) : nativeint =
        let node = Native.accesskit_node_new (byte n.Role)
        utf8 n.Label (fun p length -> Native.accesskit_node_set_label_with_length (node, p, length))
        match n.Value with
        // (An empty text box still has a value: an empty one.)
        | ValueSome "" ->
            let mutable zero = 0uy
            Native.accesskit_node_set_value_with_length (node, &&zero, 0un)
        | ValueSome v -> utf8 v (fun p length -> Native.accesskit_node_set_value_with_length (node, p, length))
        | ValueNone -> ()
        let mutable r = Native.Rect()
        r.X0 <- n.X0
        r.Y0 <- n.Y0
        r.X1 <- n.X1
        r.Y1 <- n.Y1
        Native.accesskit_node_set_bounds (node, r)
        if n.Children.Length > 0 then
            use p = fixed n.Children
            Native.accesskit_node_set_children (node, unativeint n.Children.Length, p)
        for a in n.Actions do
            Native.accesskit_node_add_action (node, byte a)
        n.Toggled |> ValueOption.iter (fun v -> Native.accesskit_node_set_toggled (node, byte v))
        n.Selected |> ValueOption.iter (fun v -> Native.accesskit_node_set_selected (node, flag v))
        n.Expanded |> ValueOption.iter (fun v -> Native.accesskit_node_set_expanded (node, flag v))
        if n.Disabled then Native.accesskit_node_set_disabled node
        if n.ReadOnly then Native.accesskit_node_set_read_only node
        if n.MultiSelectable then Native.accesskit_node_set_multiselectable node
        if n.Modal then Native.accesskit_node_set_modal node
        if n.ClipsChildren then Native.accesskit_node_set_clips_children node
        match n.Range with
        | ValueSome range ->
            Native.accesskit_node_set_numeric_value (node, range.Value)
            Native.accesskit_node_set_min_numeric_value (node, range.Minimum)
            Native.accesskit_node_set_max_numeric_value (node, range.Maximum)
            if range.Step > 0.0 then Native.accesskit_node_set_numeric_value_step (node, range.Step)
        | ValueNone -> ()
        n.Orientation |> ValueOption.iter (fun v -> Native.accesskit_node_set_orientation (node, byte v))
        match n.Position with
        | ValueSome p ->
            Native.accesskit_node_set_position_in_set (node, unativeint (max 0 p.Index))
            Native.accesskit_node_set_size_of_set (node, unativeint (max 0 p.Count))
        | ValueNone -> ()
        match n.Scroll with
        | ValueSome s ->
            Native.accesskit_node_set_scroll_x (node, s.OffsetX)
            Native.accesskit_node_set_scroll_x_min (node, 0.0)
            Native.accesskit_node_set_scroll_x_max (node, max 0.0 (s.ExtentWidth - s.ViewportWidth))
            Native.accesskit_node_set_scroll_y (node, s.OffsetY)
            Native.accesskit_node_set_scroll_y_min (node, 0.0)
            Native.accesskit_node_set_scroll_y_max (node, max 0.0 (s.ExtentHeight - s.ViewportHeight))
        | ValueNone -> ()
        node

    /// The `accesskit_tree_update` for a whole tree (AccessKit owns what is returned).
    let update (u: AkUpdate) : nativeint =
        let update = Native.accesskit_tree_update_with_capacity_and_focus (unativeint u.Nodes.Length, u.Focus)
        let info = Native.accesskit_tree_info_new u.Root
        utf8 "Scribble" (fun p length -> Native.accesskit_tree_info_set_toolkit_name_with_length (info, p, length))
        Native.accesskit_tree_update_set_tree_info (update, info)
        for n in u.Nodes do
            Native.accesskit_tree_update_push_node (update, n.Id, node n)
        update

/// A window's screen-reader bridge through AccessKit (DESIGN.md, "AccessKit: the screen-reader bridge"): Windows
/// for now (UI Automation, so Narrator, NVDA and anything else that reads windows). `App.run` on the desktop
/// attaches one to every window; a host of your own does it itself:
///
///     let bridge = AccessKitBridge.attach host      // before the window is first shown
///     ...
///     bridge.Dispose()                              // when the window goes
///
/// It does nothing until something asks the window what is in it: no tree is built and no timer runs. From then
/// on it gives AccessKit the window's whole semantic tree after a frame in which something changed, at most ten
/// times a second (a change of focus at once), and performs the actions AccessKit asks for on the UI thread.
/// `SCRIBBLE_LOG_ACCESSIBILITY=1` writes to the console when something first asks, and each tree it builds.
[<Sealed>]
type AccessKitBridge private (host: ElementHost, hwnd: nativeint) =
    /// Decision 10: at most one update in this long while changes keep coming.
    static let interval = TimeSpan.FromMilliseconds 100.0
    let log = Environment.GetEnvironmentVariable "SCRIBBLE_LOG_ACCESSIBILITY" = "1"
    let pacer = UpdatePacer(interval)
    /// Something has asked for the tree (it stays so: Windows never says that nothing listens any more).
    let mutable active = false
    let mutable dirty = false
    let mutable lastFocus: Element voption = ValueNone
    let mutable treesBuilt = 0
    let mutable disposed = false
    /// The nodes of the tree AccessKit has, by id: what an action is about.
    let nodes = Dictionary<uint64, SemanticNode>()

    let sameFocus (a: Element voption) (b: Element voption) =
        match a, b with
        | ValueSome x, ValueSome y -> obj.ReferenceEquals(x, y)
        | ValueNone, ValueNone -> true
        | _ -> false

    /// The window's tree now, written out for AccessKit.
    let build () : nativeint =
        let tree = host.SemanticTree()
        nodes.Clear()
        for n in tree.Nodes do
            nodes[n.Id] <- n
        let window = host.Window
        let size = window.ClientSize
        let focus = host.Focused
        lastFocus <- focus
        dirty <- false
        treesBuilt <- treesBuilt + 1
        if log then Console.WriteLine("Scribble accessibility: tree " + string treesBuilt + ", " + string nodes.Count + " nodes")
        Writer.update (Translation.update window.Title (float size.Width) (float size.Height) window.Scale tree focus)

    // Called by AccessKit on the window's thread: the first time something asks for the tree, and from
    // `update_if_active` once something has. A failure gives an empty window, never an exception across the call.
    let onTree =
        Native.TreeUpdateCallback(fun _ ->
            if not active then
                active <- true
                if log then Console.WriteLine "Scribble accessibility: something asked for the window's tree; the bridge is active"
            try
                build ()
            with e ->
                // To App.onError, as any error from a frame; rethrown without one, which must not cross the call.
                (try
                    (host.Tree :> IElementRoot).ReportError e
                 with _ ->
                     ())
                Writer.update { Root = Translation.rootId; Focus = Translation.rootId; Nodes = [||] })

    /// An action AccessKit asked for, on the UI thread: the node it was about does it, by the path the matching
    /// input takes.
    let perform (id: uint64) (action: int) =
        if not disposed then
            match nodes.TryGetValue id with
            | true, node ->
                match Translation.semanticAction node action with
                | ValueSome a -> node.Perform a |> ignore
                | ValueNone -> ()
            | _ -> ()

    // Called by AccessKit on any thread (decision 10 of the platform round: through the dispatcher).
    let onAction =
        Native.ActionCallback(fun request _ ->
            let action = int (Marshal.ReadByte(request, Native.ActionRequestActionOffset))
            let id = uint64 (Marshal.ReadInt64(request, Native.ActionRequestTargetNodeOffset))
            Native.accesskit_action_request_free request
            match host.Dispatcher with
            | ValueSome d -> d.Post(fun () -> perform id action)
            | ValueNone -> ())

    let onTreePointer = Marshal.GetFunctionPointerForDelegate<Native.TreeUpdateCallback> onTree
    let onActionPointer = Marshal.GetFunctionPointerForDelegate<Native.ActionCallback> onAction
    let adapter = Native.accesskit_windows_subclassing_adapter_new (hwnd, onTreePointer, 0n, onActionPointer, 0n)

    let send () =
        if not disposed then
            let events = Native.accesskit_windows_subclassing_adapter_update_if_active (adapter, onTreePointer, 0n)
            if events <> 0n then Native.accesskit_windows_queued_events_raise events

    /// After a frame: a tree that changed goes out, paced. Nothing at all until something has asked.
    let afterFrame () =
        if active && not disposed then
            let focusMoved = not (sameFocus host.Focused lastFocus)
            if dirty || focusMoved then
                match pacer.Changed(host.Elapsed, focusMoved) with
                | PacerStep.Send -> send ()
                | PacerStep.WaitFor wait ->
                    match host.Dispatcher with
                    | ValueSome d -> d.PostAfter(wait, fun () -> if pacer.Due host.Elapsed then send ())
                    // No timer to wait on: sent with the next change.
                    | ValueNone -> ()
                | PacerStep.Waiting -> ()

    let subscriptions: IDisposable list =
        [ host.LayerChanged.Subscribe(fun _ -> dirty <- true)
          host.FrameDrawn.Subscribe(fun _ -> afterFrame ()) ]

    /// Something has asked the window what is in it (a screen reader, or another tool that reads windows).
    member _.IsActive = active
    /// How many trees it has built for AccessKit: none while nothing asks.
    member _.TreesBuilt = treesBuilt

    /// Makes `host`'s window readable by screen readers. Call it before the window is first shown (AccessKit's
    /// rule), on the UI thread. On a platform or a window it can't serve it gives a bridge that does nothing;
    /// without the AccessKit library beside the app it throws (`ScreenReader.attach` turns that into a warning).
    static member attach(host: ElementHost) : IDisposable =
        match host.Window.Surface with
        | Win32Handle(_, hwnd) when OperatingSystem.IsWindows() -> new AccessKitBridge(host, hwnd) :> IDisposable
        | _ ->
            { new IDisposable with
                member _.Dispose() = () }

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                for s in subscriptions do
                    s.Dispose()
                Native.accesskit_windows_subclassing_adapter_free adapter
                // The callbacks stay alive until here: AccessKit holds pointers to them.
                GC.KeepAlive onTree
                GC.KeepAlive onAction
