/// The counter again, in the experimental builder syntax (see Scribble.Core/Builders.fs), to
/// compare with Counter.fs. Same model, messages and update; only the view differs.
module Scribble.Samples.CounterBuilders

open Scribble.Rendering
open Scribble.Core
open Scribble.Core.Builders
open type Scribble.Core.Builders.Ui
open Scribble.Samples.Counter

let view count dispatch : View =
    VStack(spacing = 16.0).margin(24.0) {
        Text($"Count: {count}").fontSize(28.0).foreground(Color.White)
        HStack(spacing = 8.0) {
            Button("−", fun () -> dispatch Decrement)
            Button("+", fun () -> dispatch Increment)
            if count <> 0 then Button("Reset", fun () -> dispatch Reset)
        }
    }

let app = [ App.title "Counter (builders)"; App.size (360.0, 180.0); App.program 0 update view ]
