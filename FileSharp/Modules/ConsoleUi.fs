module ConsoleUi

open System
open System.Globalization

// Escape control characters in filenames before rendering them in a terminal.
let safe (value: string) =
    value |> Seq.map (fun c -> if Char.IsControl c then $"\\u{int c:x4}" else string c) |> String.concat ""

let private colorEnabled noColor redirected =
    not noColor && not redirected
    && isNull (Environment.GetEnvironmentVariable "NO_COLOR")
    && Environment.GetEnvironmentVariable "TERM" <> "dumb"

let private paint enabled code text = if enabled then $"\u001b[{code}m{text}\u001b[0m" else text

let heading noColor title subtitle =
    let color = colorEnabled noColor Console.IsOutputRedirected
    printfn "\n  %s  /  %s" (paint color "1;36" "FILESHARP") (paint color "1" title)
    printfn "  %s\n" (paint color "2" (safe subtitle))

let showHelp noColor (content: string) =
    let color = colorEnabled noColor Console.IsOutputRedirected
    for line in content.Split('\n') do
        let styled =
            if line.StartsWith("FILESHARP") then paint color "1;36" line
            elif line <> "" && line = line.ToUpperInvariant() then paint color "1" line
            else line
        printfn "%s" styled

let report noColor (message: string) =
    let error = message.StartsWith("Error:", StringComparison.Ordinal)
    let enabled = colorEnabled noColor (if error then Console.IsErrorRedirected else Console.IsOutputRedirected)
    let prefix, code =
        if error then "!", "31"
        elif message.StartsWith("Would") then "~", "33"
        elif message.StartsWith("Skipped") then "-", "2"
        else "+", "32"
    let line = $"  {paint enabled code prefix} {safe message}"
    if error then eprintfn "%s" line else printfn "%s" line

let formatBytes (bytes: int64) =
    let units = [| "B"; "KiB"; "MiB"; "GiB"; "TiB"; "PiB" |]
    let mutable value = float bytes
    let mutable unit = 0
    while value >= 1024.0 && unit < units.Length - 1 do
        value <- value / 1024.0
        unit <- unit + 1
    value.ToString((if unit = 0 then "0" else "0.0"), CultureInfo.InvariantCulture) + " " + units[unit]

let table (headers: string list) (rows: string list list) =
    let rows = rows |> List.map (List.map safe)
    let widths = headers |> List.mapi (fun i header -> rows |> List.fold (fun width row -> max width row[i].Length) header.Length)
    let render row = row |> List.mapi (fun i (cell: string) -> cell.PadRight widths[i]) |> String.concat "  "
    printfn "  %s" (render headers)
    printfn "  %s" (widths |> List.map (fun width -> String('-', width)) |> String.concat "  ")
    for row in rows do printfn "  %s" (render row)

let summary noColor errors message =
    let color = colorEnabled noColor Console.IsOutputRedirected
    let status = if errors = 0 then paint color "32" "DONE" else paint color "31" $"{errors} ERROR(S)"
    printfn "\n  %s  %s\n" status message
