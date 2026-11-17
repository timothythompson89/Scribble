namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Rendering

/// Easing curves: progress 0..1 in, eased progress out. Any `float -> float` works.
module Easing =
    let linear (t: float) = t
    /// Starts slowly (cubic).
    let easeIn (t: float) = t * t * t
    /// Slows down at the end (cubic). The default: things arrive gently.
    let easeOut (t: float) = 1.0 - (1.0 - t) * (1.0 - t) * (1.0 - t)
    let easeInOut (t: float) =
        if t < 0.5 then 4.0 * t * t * t
        else 1.0 - (-2.0 * t + 2.0) ** 3.0 / 2.0

/// How values of a type blend: `lerp a b t` is `a` at 0 and `b` at 1.
module internal Lerp =
    let float (a: float) (b: float) (t: float) = a + (b - a) * t

    let color (a: Color) (b: Color) (t: float) =
        let t = float32 t
        Color.Rgba(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t)

/// A value moving towards a target over a duration. The first target is taken at once (nothing
/// animates into view); later targets start from wherever the value is at that moment.
[<Sealed>]
type internal Tween<'T>(lerp: 'T -> 'T -> float -> 'T, initial: 'T) =
    let mutable from = initial
    let mutable target = initial
    let mutable start = TimeSpan.Zero
    let mutable duration = TimeSpan.Zero
    let mutable easing: float -> float = Easing.linear

    member _.ValueAt(now: TimeSpan) : 'T =
        if duration <= TimeSpan.Zero || now >= start + duration then target
        else
            let t = max 0.0 ((now - start).TotalSeconds / duration.TotalSeconds)
            lerp from target (easing (min 1.0 t))

    member _.IsMoving(now: TimeSpan) = duration > TimeSpan.Zero && now < start + duration

    /// Heads for `next` from the current value (if it's a new target), then returns the value now.
    member this.Update(next: 'T, now: TimeSpan, length: TimeSpan, curve: float -> float) : 'T =
        if not (EqualityComparer<'T>.Default.Equals(next, target)) then
            from <- this.ValueAt now
            target <- next
            start <- now
            duration <- length
            easing <- curve
        this.ValueAt now
