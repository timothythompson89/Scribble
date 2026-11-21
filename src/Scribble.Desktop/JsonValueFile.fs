namespace Scribble.Core

open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json

/// Stored values as one JSON object, a member per value (`{ "Compact": true, "Volume": 0.5, "Theme": "Dark" }`),
/// read from and written to a file the caller opens (DESIGN.md, "Stored values"). `read` gives the file's stream,
/// or null when there is none; `write` is given what fills a new file.
///
/// The low-level reader and writer only (no reflection: NativeAOT). Members it doesn't know (another version's,
/// or a hand-written object or list) are kept as they are, in the file's order. A file that is missing or damaged
/// gives no values, so the defaults are used.
[<Sealed>]
type internal JsonValueFile(read: unit -> Stream, write: (Stream -> unit) -> unit) =
    // Each member's JSON as written in the file ("true", "0.5", "\"Dark\"", "[1,2]"), in the file's order.
    let members = Dictionary<string, string>()
    let order = List<string>()
    let mutable loaded = false
    let mutable changed = false

    let load () =
        if not loaded then
            loaded <- true
            try
                use stream = read ()
                if not (isNull stream) then
                    use document = JsonDocument.Parse stream
                    if document.RootElement.ValueKind = JsonValueKind.Object then
                        for m in document.RootElement.EnumerateObject() do
                            if not (members.ContainsKey m.Name) then order.Add m.Name
                            members[m.Name] <- m.Value.GetRawText()
            with _ ->
                members.Clear()
                order.Clear()

    /// A number as JSON has it (no NaN or infinity, which a float may be: those are stored as text).
    let isJsonNumber (text: string) =
        try
            use document = JsonDocument.Parse text
            document.RootElement.ValueKind = JsonValueKind.Number
        with _ ->
            false

    /// `text` as a JSON string, escaped by the writer.
    let quoted (text: string) =
        use buffer = new MemoryStream()
        // Quotes and letters as they are ("Ada \"A\"", é), not as " escapes: the file is read by people, never by a page.
        (use writer = new Utf8JsonWriter(buffer, JsonWriterOptions(Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping))
         writer.WriteStringValue text)
        Encoding.UTF8.GetString(buffer.ToArray())

    interface IValueStore with
        member _.TryRead name =
            load ()
            match members.TryGetValue name with
            | true, raw ->
                try
                    use document = JsonDocument.Parse raw
                    let e = document.RootElement
                    match e.ValueKind with
                    | JsonValueKind.String -> ValueSome(e.GetString())
                    | JsonValueKind.Number -> ValueSome raw
                    | JsonValueKind.True -> ValueSome "true"
                    | JsonValueKind.False -> ValueSome "false"
                    | _ -> ValueNone
                with _ ->
                    ValueNone
            | _ -> ValueNone

        member _.Write(name, kind, text) =
            load ()
            let raw =
                match kind with
                | StoredKind.Bool when text = "true" || text = "false" -> text
                | StoredKind.Number when isJsonNumber text -> text
                | _ -> quoted text
            match members.TryGetValue name with
            | true, old when old = raw -> ()
            | found, _ ->
                if not found then order.Add name
                members[name] <- raw
                changed <- true

        member _.Flush() =
            if changed then
                write (fun stream ->
                    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
                    writer.WriteStartObject()
                    for name in order do
                        writer.WritePropertyName name
                        writer.WriteRawValue(members[name], true)
                    writer.WriteEndObject()
                    writer.Flush())
                changed <- false
