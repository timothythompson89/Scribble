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

/// The flyout and tooltip settings every element has (DESIGN.md, "M8 step 4", round 1).
module FlyoutProps =
    let content = Prop<View voption>("FlyoutContent", ValueNone, Affects.Nothing, PropKind.Value)
    let placement = Prop<Placement>("FlyoutPlacement", Placement.Below, Affects.Nothing, PropKind.Value)
    let alignment = Prop<FlyoutAlignment>("FlyoutAlignment", FlyoutAlignment.Start, Affects.Nothing, PropKind.Value)
    /// Given: the flyout is controlled (shows exactly this). ValueNone: it keeps its own.
    let isOpen = Prop<bool voption>("IsFlyoutOpen", ValueNone, Affects.Nothing, PropKind.Value)
    let onOpenChanged = Prop<(bool -> unit) voption>("OnFlyoutOpenChanged", ValueNone, Affects.Nothing, PropKind.Value)
    let lightDismiss = Prop<bool>("FlyoutLightDismiss", true, Affects.Nothing, PropKind.Value)
    let matchAnchorWidth = Prop<bool>("FlyoutMatchAnchorWidth", false, Affects.Nothing, PropKind.Value)
    let tooltip = Prop<string>("Tooltip", "", Affects.Nothing, PropKind.Value)
    let tooltipView = Prop<View voption>("TooltipView", ValueNone, Affects.Nothing, PropKind.Value)
    /// An uncontrolled flyout's own open state.
    let internal ownOpen = Prop.local "FlyoutOwnOpen" false

    do
        // The host hears when the view opens or closes a controlled flyout.
        isOpen.Watched <- true
        for p in [ isOpen :> Prop; onOpenChanged ] do
            p.NotStylable <- true

/// Opening and closing a flyout from its anchor (the host shows it).
module internal Flyouts =
    /// Whether the anchor's flyout should show now: the view's value if it gives one, else its own.
    let isOpen (e: Element) =
        match e.Get FlyoutProps.isOpen with
        | ValueSome v -> v
        | ValueNone -> e.Get FlyoutProps.ownOpen

    let hasFlyout (e: Element) = (e.Get FlyoutProps.content).IsSome

    /// Asks for the flyout open or closed (a click, a light dismiss): an uncontrolled one changes, and
    /// `onFlyoutOpenChanged` hears it either way; a controlled one changes only when the view says so.
    let request (e: Element) (value: bool) =
        if isOpen e <> value then
            if (e.Get FlyoutProps.isOpen).IsNone then
                e.SetLocal(FlyoutProps.ownOpen, value)
                e.NotifyWatch()
            match e.Get FlyoutProps.onOpenChanged with
            | ValueSome handler ->
                try
                    handler value
                with ex ->
                    e.ReportError ex
            | ValueNone -> ()
