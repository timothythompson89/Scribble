/// A small, F#-friendly layer over .NET's JavaScript interop (System.Runtime.InteropServices.JavaScript).
///
/// C# normally reaches JavaScript through the [<JSImport>] source generator, which F# can't run.
/// The generator only emits calls to the public JSFunctionBinding API, so we call that API directly:
/// bind a function once, then invoke it with an argument buffer. Slot 0 of the buffer holds a thrown
/// exception, slot 1 the return value, and the arguments start at slot 2.
module Scribble.Platform.Browser.Js

open System
open System.IO
open System.Reflection
open System.Threading.Tasks
open System.Runtime.InteropServices.JavaScript

/// Imports an ES module embedded as a resource in `assembly`, under `moduleName`.
/// The module is loaded from a data: URL, so nothing needs to be deployed next to the app.
let importEmbeddedModule (moduleName: string) (assembly: Assembly) (resourceName: string) : Task =
    use stream =
        match assembly.GetManifestResourceStream resourceName with
        | null -> raise (InvalidOperationException $"Embedded JavaScript '{resourceName}' not found in {assembly.GetName().Name}.")
        | s -> s

    use reader = new StreamReader(stream)
    let source = reader.ReadToEnd()
    let url = "data:text/javascript;base64," + Convert.ToBase64String(Text.Encoding.UTF8.GetBytes source)
    JSHost.ImportAsync(moduleName, url) :> Task

/// A JavaScript function exported from an imported module, bound on first use.
[<Sealed>]
type JsFunction(moduleName: string, functionName: string, returns: JSMarshalerType, parameters: JSMarshalerType list) =
    let mutable binding: JSFunctionBinding = null

    let signature = Array.ofList (returns :: parameters)

    member _.ParameterCount = parameters.Length

    /// A fresh argument buffer with the exception and return slots initialized.
    member this.Args() =
        let args = Array.zeroCreate<JSMarshalerArgument> (this.ParameterCount + 2)
        args[0].Initialize()
        args[1].Initialize()
        args

    /// Calls the function. Throws a JSException if the JavaScript side threw.
    member _.Invoke(args: JSMarshalerArgument[]) =
        if isNull binding then
            binding <- JSFunctionBinding.BindJSFunction(functionName, moduleName, ReadOnlySpan signature)
        JSFunctionBinding.InvokeJS(binding, Span args)

/// Shorthands for marshaler types, so bindings read like signatures.
module T =
    /// For functions that return nothing (the generator uses Discard, not Void).
    let void' = JSMarshalerType.Discard
    let string = JSMarshalerType.String
    let double = JSMarshalerType.Double
    let bool = JSMarshalerType.Boolean
    let action = JSMarshalerType.Action()
    let task = JSMarshalerType.Task()
    let doubles = JSMarshalerType.Array JSMarshalerType.Double
    let strings = JSMarshalerType.Array JSMarshalerType.String
    /// A Span<byte>: JavaScript sees a MemoryView, valid only during the call.
    let bytes = JSMarshalerType.Span JSMarshalerType.Byte
    /// A byte[]: copied from a Uint8Array (a return value JavaScript keeps).
    let byteArray = JSMarshalerType.Array JSMarshalerType.Byte

/// Reads a Task returned by a JavaScript function (a Promise on the JS side).
let returnedTask (args: JSMarshalerArgument[]) : Task =
    let mutable task: Task = null
    args[1].ToManaged(&task)
    task

let returnedString (args: JSMarshalerArgument[]) : string =
    let mutable value: string = null
    args[1].ToManaged(&value)
    value

let returnedDouble (args: JSMarshalerArgument[]) : float =
    let mutable value = 0.0
    args[1].ToManaged(&value)
    value

let returnedDoubles (args: JSMarshalerArgument[]) : float[] =
    let mutable value: float[] = null
    args[1].ToManaged(&value)
    if isNull value then [||] else value

let returnedStrings (args: JSMarshalerArgument[]) : string[] =
    let mutable value: string[] = null
    args[1].ToManaged(&value)
    if isNull value then [||] else value

/// A byte[] returned by JavaScript (a Uint8Array), or null.
let returnedBytes (args: JSMarshalerArgument[]) : byte[] =
    let mutable value: byte[] = null
    args[1].ToManaged(&value)
    value
