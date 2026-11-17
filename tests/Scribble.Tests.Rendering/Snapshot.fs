/// Screenshot comparison against reference PNGs stored next to the tests.
///
/// The comparison is exact (project review 2026-11-17, decision 13: same pixels on every platform).
/// The references come from the default backend on Tim's machine (D3D12, NVIDIA RTX 4070 Ti;
/// regenerated 2026-11-17); BackendTests checks that the other backends draw the same pixels.
/// To accept new output as the reference, run the tests with SCRIBBLE_UPDATE_SNAPSHOTS=1 and look
/// at every changed picture before committing it.
module Scribble.Tests.Rendering.Snapshot

open System
open System.IO
open Scribble.Rendering

/// Per-channel difference (0–255) that still counts as equal: none.
let channelTolerance = 0
/// Share of pixels allowed to differ: none.
let pixelTolerance = 0.0

let private repoRoot =
    lazy
        (let rec up (dir: DirectoryInfo) =
            if isNull dir then failwith "Can't find Scribble.slnx above the test output folder."
            elif File.Exists(Path.Combine(dir.FullName, "Scribble.slnx")) then dir.FullName
            else up dir.Parent
         match Environment.GetEnvironmentVariable "SCRIBBLE_REPO_ROOT" with
         | null | "" -> up (DirectoryInfo AppContext.BaseDirectory)
         | root -> root)

let referenceDir () = Path.Combine(repoRoot.Value, "tests", "Scribble.Tests.Rendering", "References")
let outputDir () = Path.Combine(repoRoot.Value, "tests", "Scribble.Tests.Rendering", "bin", "snapshots")

type Comparison =
    { DifferentPixels: int
      TotalPixels: int
      MaxDifference: int }

    member c.Passes = float c.DifferentPixels <= pixelTolerance * float c.TotalPixels

let compare (expected: Bitmap) (actual: Bitmap) =
    if expected.Size <> actual.Size then
        { DifferentPixels = Int32.MaxValue; TotalPixels = 1; MaxDifference = 255 }
    else
        let mutable different = 0
        let mutable maxDiff = 0
        for p in 0 .. expected.Pixels.Length / 4 - 1 do
            let mutable worst = 0
            for c in 0..3 do
                worst <- max worst (abs (int expected.Pixels[p * 4 + c] - int actual.Pixels[p * 4 + c]))
            maxDiff <- max maxDiff worst
            if worst > channelTolerance then different <- different + 1
        { DifferentPixels = different; TotalPixels = expected.Pixels.Length / 4; MaxDifference = maxDiff }

/// Compares `actual` with References/<name>.png. Returns an error message, or None when it matches.
let check (name: string) (actual: Bitmap) : string option =
    let reference = Path.Combine(referenceDir (), name + ".png")
    let update = Environment.GetEnvironmentVariable "SCRIBBLE_UPDATE_SNAPSHOTS" = "1"
    if update || not (File.Exists reference) then
        Png.save reference actual
        if update then None
        else Some $"No reference existed for '{name}'; wrote one to {reference}. Check it by eye, then rerun."
    else
        let result = compare (Png.load reference) actual
        if result.Passes then
            None
        else
            let actualPath = Path.Combine(outputDir (), name + ".actual.png")
            Png.save actualPath actual
            Some(
                $"'{name}' differs from its reference: {result.DifferentPixels} of {result.TotalPixels} pixels "
                + $"(max channel difference {result.MaxDifference}). Actual output: {actualPath}. "
                + "If the change is intended, rerun with SCRIBBLE_UPDATE_SNAPSHOTS=1."
            )
