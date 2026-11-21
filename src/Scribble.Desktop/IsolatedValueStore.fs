namespace Scribble.Core

open System
open System.IO
open System.IO.IsolatedStorage

/// The desktop's store for stored values (DESIGN.md, "Stored values"): a JSON file in the user's isolated
/// storage, named after the store (`Gallery.json`). `App.run` gives every app one.
///
/// Isolated storage finds an app's files by the path of its exe: a moved or reinstalled exe, and each of a
/// project's Debug, Release and published builds, has a store of its own and starts from the defaults.
///
/// The file is written under another name and then renamed, so a crash while writing leaves the old file or the
/// new one, never half of one.
[<Sealed>]
type IsolatedValueStore(storeName: string) =
    // The file's name: the store's, without what a file name can't hold.
    let fileName =
        let invalid = Path.GetInvalidFileNameChars()
        let cleaned = String(storeName.ToCharArray() |> Array.map (fun c -> if Array.contains c invalid then '_' else c)).Trim()
        (if cleaned = "" then "Scribble" else cleaned) + ".json"
    let tempName = fileName + ".tmp"

    let openStore () = IsolatedStorageFile.GetUserStoreForAssembly()

    /// The file, read whole (the store is closed again before it is parsed); the temporary one when a rename the
    /// last run didn't finish left only that.
    let read () : Stream =
        use store = openStore ()
        let name = if store.FileExists fileName then fileName elif store.FileExists tempName then tempName else ""
        if name = "" then null
        else
            use file = store.OpenFile(name, FileMode.Open, FileAccess.Read, FileShare.Read)
            let memory = new MemoryStream()
            file.CopyTo memory
            memory.Position <- 0L
            memory

    let write (fill: Stream -> unit) =
        use store = openStore ()
        (use file = store.OpenFile(tempName, FileMode.Create, FileAccess.Write, FileShare.None)
         fill file)
        if store.FileExists fileName then store.DeleteFile fileName
        store.MoveFile(tempName, fileName)

    let file = JsonValueFile(read, write) :> IValueStore

    /// The name of the file in the app's isolated storage.
    member _.FileName = fileName

    interface IValueStore with
        member _.TryRead name = file.TryRead name
        member _.Write(name, kind, text) = file.Write(name, kind, text)
        member _.Flush() = file.Flush()
