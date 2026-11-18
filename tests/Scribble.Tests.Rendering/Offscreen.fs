namespace Scribble.Rendering

/// The tests render on the desktop, where `RenderOffscreenAsync`'s task is already finished: this reads it at once
/// (opened with Scribble.Rendering, so tests call `device.RenderOffscreen(size, scale, draw)` as before).
[<AutoOpen>]
module OffscreenSync =
    type IRenderDevice with
        member device.RenderOffscreen(size, scale, draw) : Bitmap =
            let task = device.RenderOffscreenAsync(size, scale, draw)
            if not task.IsCompleted then invalidOp "RenderOffscreenAsync didn't finish at once (only the desktop's does)."
            task.GetAwaiter().GetResult()
