module FileSharp.Program

open System
open System.IO
open System.Text.Json
open ConfigurationRetrieval
open DirectoryAndFileFunctions

type Options = { Path: string; Extension: string; Clean: bool; DryRun: bool }

let usage = """FileSharp - organize files into extension-based folders
Usage: FileSharp <directory> [--extension .txt|all] [--dry-run] [--clean-empty]
       FileSharp --help
With no arguments, prompts for input. Only top-level files are moved.
--clean-empty recursively removes empty directories; links are skipped.
Exit codes: 0 success, 1 operational failure, 2 invalid input/configuration."""

let parseArgs (args: string array) =
    let rec parse options remaining =
        match remaining with
        | [] -> options
        | "--extension" :: value :: tail when not (value.StartsWith("--")) -> parse { options with Extension = value } tail
        | "--dry-run" :: tail -> parse { options with DryRun = true } tail
        | "--clean-empty" :: tail -> parse { options with Clean = true } tail
        | value :: tail when not (value.StartsWith("--")) && options.Path = "" -> parse { options with Path = value } tail
        | value :: _ -> invalidArg "arguments" $"Unexpected or incomplete argument: {value}"
    let options = parse { Path = ""; Extension = "all"; Clean = false; DryRun = false } (List.ofArray args)
    if String.IsNullOrWhiteSpace options.Path then invalidArg "arguments" "A directory path is required."
    options

let prompt message =
    printfn "%s" message
    match Console.ReadLine() with
    | null -> invalidArg "input" "Input ended before all questions were answered."
    | value -> value.Trim()

[<EntryPoint>]
let main args =
    try
        if args = [| "--help" |] || args = [| "-h" |] then
            printfn "%s" usage
            0
        else
            let options =
                if args.Length = 0 then
                    let path = prompt "Enter the directory to organize:"
                    let extension = prompt "Enter an extension (such as .txt) or all:"
                    let clean = prompt "Delete empty directories recursively? (y/N)"
                    { Path = path; Extension = extension; Clean = clean.Equals("y", StringComparison.OrdinalIgnoreCase); DryRun = false }
                else parseArgs args
            let root = NormalizeRoot options.Path
            if not (ExtensionData.ExtensionIsValid options.Extension) then
                invalidArg "extension" "Use 'all' or a dotted extension such as .txt."
            let mappings = LoadExtensions (Path.Combine(AppContext.BaseDirectory, "extensions.json"))
            let mutable errors = Organize root options.Extension mappings options.DryRun (printfn "%s")
            if options.Clean then errors <- errors + CleanDirectories root options.DryRun (printfn "%s")
            printfn "%s" (if options.DryRun then "Preview complete. No changes made." else "Organizing complete.")
            if errors = 0 then 0 else 1
    with
    | :? ArgumentException as ex -> eprintfn "Error: %s" ex.Message; 2
    | :? JsonException as ex -> eprintfn "Invalid configuration: %s" ex.Message; 2
    | :? IOException as ex -> eprintfn "File system error: %s" ex.Message; 1
    | :? UnauthorizedAccessException as ex -> eprintfn "Access denied: %s" ex.Message; 1
