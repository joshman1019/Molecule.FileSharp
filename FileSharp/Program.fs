module FileSharp.Program

open System
open System.IO
open System.Text.Json
open Cli
open ConsoleUi
open DirectoryAndFileFunctions

// Keep the parser accessible to existing callers and regression tests.
let parseArgs = Cli.parseArgs
let usage = Cli.help None

let prompt message =
    printfn "%s" message
    match Console.ReadLine() with
    | null -> invalidArg "input" "Input ended before all questions were answered."
    | value -> value.Trim()

let private interactive () =
    let path = prompt "Enter the directory to organize:"
    let extension = prompt "Enter extensions (such as .txt,.pdf) or all:"
    let clean = prompt "Delete empty directories recursively? (y/N)"
    parseArgs ([| path; "--extension"; extension |] |> fun args -> if clean.Equals("y", StringComparison.OrdinalIgnoreCase) then Array.append args [| "--clean-empty" |] else args)

let private selectionTable (selection: Maintenance.Selection) =
    table [ "SIZE"; "FILE" ]
        (selection.Files |> Array.map (fun file -> [ formatBytes file.Length; file.Relative ]) |> List.ofArray)

let private run options =
    let root = NormalizeRoot options.Path
    let name = commandName options.Command
    let title = if options.DryRun then name + " / preview" else name
    heading options.NoColor title root
    let report = ConsoleUi.report options.NoColor
    let timer = Diagnostics.Stopwatch.StartNew()
    let errors, message =
        match options.Command with
        | Organize ->
            let mappings = ConfigurationRetrieval.LoadExtensions (Path.Combine(AppContext.BaseDirectory, "extensions.json"))
            let mutable count = 0
            let progress (message: string) =
                if message.StartsWith("Moved:") || message.StartsWith("Would move:") then count <- count + 1
                report message
            let mutable errors = OrganizeExtensions root options.Extensions mappings options.DryRun progress
            if options.Clean then errors <- errors + CleanDirectories root options.DryRun report
            errors, $"{count} file(s) organized."
        | CleanEmpty ->
            let mutable count = 0
            let progress (message: string) =
                if message.StartsWith("Deleted empty") || message.StartsWith("Would delete") then count <- count + 1
                report message
            let errors = CleanDirectories root options.DryRun progress
            errors, $"{count} empty folder(s)."
        | command ->
            let selection = Maintenance.select root options.Extensions options.Recursive report
            let totalBytes = selection.Files |> Array.sumBy _.Length
            printfn "  Selected %d file(s) / %s / %s\n" selection.Files.Length (formatBytes totalBytes) (if options.Recursive then "recursive" else "top-level")
            match command with
            | ListFiles ->
                selectionTable selection
                selection.Errors, $"{selection.Files.Length} file(s), {formatBytes totalBytes}."
            | Stats ->
                let rows =
                    selection.Files
                    |> Array.groupBy (fun file -> let ext = Path.GetExtension file.Path in if ext = "" then "(none)" else ext.ToLowerInvariant())
                    |> Array.map (fun (extension, files) -> extension, files.Length, files |> Array.sumBy _.Length)
                    |> Array.sortBy (fun (extension, _, bytes) -> -bytes, extension)
                    |> Array.map (fun (extension, count, bytes) -> [ extension; string count; formatBytes bytes ])
                    |> List.ofArray
                table [ "TYPE"; "FILES"; "TOTAL SIZE" ] rows
                selection.Errors, $"{selection.Files.Length} file(s), {formatBytes totalBytes}."
            | Duplicates ->
                let groups, errors = Maintenance.duplicates selection report
                let mutable redundant = 0L
                for i in 0 .. groups.Length - 1 do
                    let files = groups[i]
                    redundant <- redundant + files[0].Length * int64 (files.Length - 1)
                    printfn "  Group %d / %d copies / %s each" (i + 1) files.Length (formatBytes files[0].Length)
                    for file in files do printfn "    %s" (safe file.Relative)
                    printfn ""
                if groups.Length = 0 then printfn "  No duplicates found."
                errors, $"{groups.Length} duplicate group(s), {formatBytes redundant} redundant content. No files changed."
            | Delete ->
                selectionTable selection
                if selection.Files.Length = 0 then selection.Errors, "No matching files."
                elif options.DryRun then
                    let result = Maintenance.delete selection true report
                    result.Errors, $"{result.Completed} file(s) would be deleted."
                else
                    let confirmed =
                        if options.Yes then true
                        elif Console.IsInputRedirected then
                            invalidArg "confirmation" "Deletion requires confirmation. Use --yes for scripts or --dry-run to preview."
                        else
                            printfn "\n  Permanently delete %d selected file(s)? Originals cannot be recovered by FileSharp." selection.Files.Length
                            (prompt "  Type DELETE to continue; anything else cancels:") = "DELETE"
                    if confirmed then
                        let result = Maintenance.delete selection false report
                        result.Errors, $"{result.Completed} file(s) deleted."
                    else selection.Errors, "Cancelled. No files deleted."
            | Zip ->
                let result = Maintenance.zip selection options.Output.Value options.Level options.DryRun report
                result.Errors, $"{result.Completed} file(s) archived. Originals retained."
            | Compress ->
                let result = Maintenance.compress selection options.Level options.DryRun report
                result.Errors, $"{result.Completed} file(s) compressed. Originals retained."
            | _ -> failwith "Unsupported command"
    let message = if options.DryRun then "Preview only. No changes made. " + message else message
    summary options.NoColor errors $"{message} ({timer.Elapsed.TotalSeconds:F2}s)"
    if errors = 0 then 0 else 1

[<EntryPoint>]
let main args =
    let noColor = Array.contains "--no-color" args
    try
        let helpArguments = args |> Array.filter ((<>) "--no-color") |> List.ofArray
        match helpArguments with
        | [ "--version" ] -> printfn "FileSharp %s" (typeof<Options>.Assembly.GetName().Version.ToString(3)); 0
        | [ "help" ] | [ "--help" ] | [ "-h" ] -> showHelp noColor (Cli.help None); 0
        | [ "help"; topic ] -> showHelp noColor (Cli.help (Some topic)); 0
        | _ ->
            let beforeSeparator = args |> Array.takeWhile ((<>) "--")
            if beforeSeparator |> Array.exists (fun arg -> arg = "--help" || arg = "-h") then
                let topic = helpArguments |> List.tryHead |> Option.bind (fun name -> tryCommand name |> Option.map (fun _ -> name))
                showHelp noColor (Cli.help topic)
                0
            else
                let options = if args.Length = 0 then interactive() else parseArgs args
                run options
    with
    | :? ArgumentException as ex -> report noColor $"Error: {ex.Message}"; eprintfn "  Run 'filesharp help' for commands and examples."; 2
    | :? JsonException as ex -> report noColor $"Error: Invalid configuration: {ex.Message}"; 2
    | :? IOException as ex -> report noColor $"Error: {ex.Message}"; 1
    | :? UnauthorizedAccessException as ex -> report noColor $"Error: Access denied: {ex.Message}"; 1
