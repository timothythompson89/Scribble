namespace Scribble.Core

/// What controls share about their value (DESIGN.md, M8 round 5, decision 2): when the view gives
/// the value setting, the control is *controlled* and shows exactly that; when it doesn't, the
/// control keeps its *own value* (set by code, so a patch from a view that never had the setting
/// leaves it). Either way the change event gets the new value.
module internal ControlValue =
    /// The view gives this value (the control is controlled).
    let isControlled (e: Element) (prop: Prop) =
        match e.CurrentView with
        | ValueSome v -> v.Has prop
        | ValueNone -> false

    /// The user changed the value to `value`: the own value follows unless the view gives it, then
    /// the handler runs.
    let change (e: Element) (prop: Prop<'T>) (value: 'T) (handler: ('T -> unit) voption) =
        if not (isControlled e prop) then e.Set(prop, value)
        match handler with
        | ValueSome h -> h value
        | ValueNone -> ()
