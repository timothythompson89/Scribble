namespace Scribble.Core

/// Which side of its anchor a flyout shows on (flipped to the other side when it doesn't fit).
[<RequireQualifiedAccess>]
type Placement =
    | Below
    | Above
    | Left
    | Right

/// Where a flyout lines up along its anchor's side: its start (left or top), centre or end.
[<RequireQualifiedAccess>]
type FlyoutAlignment =
    | Start
    | Center
    | End

/// The flyout's settings (on the Flyout: DESIGN.md, "M8 step 4", round 1b) and the anchor's (`flyout`, tooltips).
module FlyoutProps =
    /// On any element: its flyout (a `Flyout.create [ ... ]` view).
    let flyout = Prop<View voption>("Flyout", ValueNone, Affects.Nothing, PropKind.Value)
    // On the Flyout. Measure: a change places it again (its layer's root arranges it).
    let placement = Prop<Placement>("Placement", Placement.Below, Affects.Measure, PropKind.Value)
    let alignment = Prop<FlyoutAlignment>("Alignment", FlyoutAlignment.Start, Affects.Measure, PropKind.Value)
    let matchAnchorWidth = Prop<bool>("MatchAnchorWidth", false, Affects.Measure, PropKind.Value)
    /// Given: the flyout is controlled (shows exactly this). ValueNone: its anchor keeps its own.
    let isOpen = Prop<bool voption>("IsOpen", ValueNone, Affects.Nothing, PropKind.Value)
    let onOpenChanged = Prop<(bool -> unit) voption>("OnOpenChanged", ValueNone, Affects.Nothing, PropKind.Value)
    let lightDismiss = Prop<bool>("LightDismiss", true, Affects.Nothing, PropKind.Value)
    // On any element.
    let tooltip = Prop<string>("Tooltip", "", Affects.Nothing, PropKind.Value)
    let tooltipView = Prop<View voption>("TooltipView", ValueNone, Affects.Nothing, PropKind.Value)
    /// An uncontrolled flyout's open state, kept by its anchor.
    let internal ownOpen = Prop.local "FlyoutOwnOpen" false

    do
        // The host hears when the view gives an element another flyout (opened or closed, say).
        flyout.Watched <- true
        for p in [ flyout :> Prop; isOpen; onOpenChanged ] do
            p.NotStylable <- true

/// Opening and closing a flyout from its anchor (the host shows it).
module internal Flyouts =
    /// The value `view` gives `prop` itself (the last such setting), before any element is made from it.
    let valueIn (view: View) (prop: Prop<'T>) : 'T voption =
        let settings = view.Settings
        let mutable found = ValueNone
        let mutable i = settings.Length - 1
        while found.IsNone && i >= 0 do
            let struct (p, v) = settings[i]
            if obj.ReferenceEquals(p, prop) then found <- ValueSome(unbox<'T> v)
            i <- i - 1
        found

    let flyoutOf (e: Element) = e.Get FlyoutProps.flyout

    let hasFlyout (e: Element) = (flyoutOf e).IsSome

    /// Whether the anchor's flyout should show now: the Flyout's `isOpen` if it gives one, else the anchor's own.
    let isOpen (e: Element) =
        match flyoutOf e with
        | ValueSome view ->
            match valueIn view FlyoutProps.isOpen with
            | ValueSome(ValueSome v) -> v
            | _ -> e.Get FlyoutProps.ownOpen
        | ValueNone -> false

    /// Asks for the flyout open or closed (a click, a light dismiss): an uncontrolled one changes, and the Flyout's
    /// `onOpenChanged` hears it either way; a controlled one changes only when the view says so.
    let request (e: Element) (value: bool) =
        match flyoutOf e with
        | ValueSome view when isOpen e <> value ->
            match valueIn view FlyoutProps.isOpen with
            | ValueSome(ValueSome _) -> ()
            | _ ->
                e.SetLocal(FlyoutProps.ownOpen, value)
                e.NotifyWatch()
            match valueIn view FlyoutProps.onOpenChanged with
            | ValueSome(ValueSome handler) ->
                try
                    handler value
                with ex ->
                    e.ReportError ex
            | _ -> ()
        | _ -> ()
