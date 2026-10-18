module Cli

open System
open System.IO.Compression
open ExtensionData

type Command = Organize | Delete | Zip | Compress | ListFiles | Stats | Duplicates | CleanEmpty

type Options = {
    Command: Command
    Path: string
    Extensions: string list
    Recursive: bool
    DryRun: bool
    Clean: bool
    Yes: bool
    Output: string option
    Level: CompressionLevel
    NoColor: bool
}

let defaults = {
    Command = Organize; Path = ""; Extensions = []; Recursive = false
    DryRun = false; Clean = false; Yes = false; Output = None
    Level = CompressionLevel.Optimal; NoColor = false
}

let commands = [
    "organize", Organize, "Group files into folders using extension mappings"
    "delete", Delete, "Delete selected file types (preview + confirmation)"
    "zip", Zip, "Collect selected file types into one ZIP archive"
    "compress", Compress, "Create a .gz copy of each selected file"
    "list", ListFiles, "List selected files and their sizes"
    "stats", Stats, "Summarize file counts and disk usage by extension"
    "duplicates", Duplicates, "Find identical content using SHA-256 (read-only)"
    "clean-empty", CleanEmpty, "Remove empty directories, working from children up"
]

let commandName command = commands |> List.find (fun (_, value, _) -> value = command) |> fun (name, _, _) -> name

let tryCommand name = commands |> List.tryFind (fun (value, _, _) -> value = name) |> Option.map (fun (_, value, _) -> value)

let private extensions (value: string) =
    let values = value.Split(',') |> Array.map _.Trim() |> List.ofArray
    if values |> List.exists (ExtensionIsValid >> not) then
        invalidArg "extensions" "Use dotted extensions separated by commas (such as .txt,.log), or all."
    values

let parseArgs (args: string array) =
    let initial, arguments =
        match List.ofArray args with
        | head :: tail when (tryCommand head).IsSome -> { defaults with Command = (tryCommand head).Value }, tail
        | values -> defaults, values
    let rec parse options remaining =
        match remaining with
        | [] -> options
        | ("--extension" | "--extensions" | "-e") :: value :: tail when not (value.StartsWith("-")) ->
            parse { options with Extensions = options.Extensions @ extensions value } tail
        | ("--output" | "-o") :: value :: tail when not (value.StartsWith("-")) ->
            if options.Output.IsSome then invalidArg "output" "Specify --output only once."
            parse { options with Output = Some value } tail
        | "--level" :: value :: tail ->
            let level =
                match value with
                | "fast" -> CompressionLevel.Fastest
                | "optimal" -> CompressionLevel.Optimal
                | "smallest" -> CompressionLevel.SmallestSize
                | _ -> invalidArg "level" "Choose fast, optimal, or smallest."
            parse { options with Level = level } tail
        | ("--recursive" | "-r") :: tail -> parse { options with Recursive = true } tail
        | ("--dry-run" | "-n") :: tail -> parse { options with DryRun = true } tail
        | "--clean-empty" :: tail -> parse { options with Clean = true } tail
        | ("--yes" | "-y") :: tail -> parse { options with Yes = true } tail
        | "--no-color" :: tail -> parse { options with NoColor = true } tail
        | "--" :: [ path ] when options.Path = "" -> { options with Path = path }
        | value :: tail when not (value.StartsWith("-")) && options.Path = "" -> parse { options with Path = value } tail
        | value :: _ -> invalidArg "arguments" $"Unexpected or incomplete argument: {value}"
    let options = parse initial arguments
    if String.IsNullOrWhiteSpace options.Path then invalidArg "path" "A directory path is required."
    if options.Command = Delete && options.Extensions.IsEmpty then
        invalidArg "extensions" "Deletion requires an explicit --extensions selection (or all)."
    if options.Extensions.Length > 1 && (options.Extensions |> List.exists (fun e -> e.Equals("all", StringComparison.OrdinalIgnoreCase))) then
        invalidArg "extensions" "Use all by itself, without other extensions."
    if options.Command = Organize && (options.Recursive || options.Extensions.Length > 1) then
        invalidArg "arguments" "Organize supports one extension and top-level files only."
    if options.Clean && options.Command <> Organize then invalidArg "arguments" "Use the clean-empty command for standalone cleanup."
    if options.Yes && options.Command <> Delete then invalidArg "arguments" "--yes applies only to delete."
    if options.Output.IsSome && options.Command <> Zip then invalidArg "arguments" "--output applies only to zip."
    if options.Command = Zip && options.Output.IsNone then invalidArg "output" "ZIP requires --output <archive.zip>."
    if (args |> Array.takeWhile ((<>) "--") |> Array.contains "--level") && options.Command <> Zip && options.Command <> Compress then
        invalidArg "level" "--level applies only to zip and compress."
    if options.Command = CleanEmpty && (options.Recursive || not options.Extensions.IsEmpty) then
        invalidArg "arguments" "clean-empty always traverses directories and does not accept file filters."
    if List.contains options.Command [ ListFiles; Stats; Duplicates ] && options.DryRun then
        invalidArg "arguments" "This command is already read-only; --dry-run is unnecessary."
    options

let help topic =
    let globalOptions = """  -e, --extensions LIST   Case-insensitive types: .txt,.log (repeatable)
  -r, --recursive         Include subdirectories; skip linked directories
  -n, --dry-run           Preview changes without writing or deleting
      --no-color         Disable terminal colors (also honors NO_COLOR)
  -h, --help             Show help for the current command
      --version          Show application version"""
    match topic with
    | None ->
        let rows = commands |> List.map (fun (name, _, description) -> $"  {name.PadRight(14)}{description}") |> String.concat "\n"
        $"""FILESHARP  /  directory maintenance

USAGE
  filesharp <command> <directory> [options]
  filesharp help [command]
  filesharp <directory> [options]       Legacy organize shortcut

COMMANDS
{rows}

COMMON OPTIONS
{globalOptions}

EXAMPLES
  filesharp list ./downloads --extensions .zip,.pdf
  filesharp delete ./downloads -e .tmp,.bak --dry-run
  filesharp zip ./logs -e .txt,.log -r -o ./logs.zip
  filesharp compress ./logs -e .log --level smallest
  filesharp help delete

Top-level files only unless --recursive is supplied. Originals are kept by
zip and compress. Existing outputs are never overwritten. No arguments starts
interactive organization. Exit codes: 0 success, 1 I/O failure, 2 invalid input.
"""
    | Some name ->
        let command = tryCommand name |> Option.defaultWith (fun () -> invalidArg "command" $"Unknown help topic: {name}")
        let _, _, description = commands |> List.find (fun (_, value, _) -> value = command)
        let details, examples =
            match command with
            | Organize ->
                "Top-level files only. Select one extension or all.\n  --clean-empty          Also remove empty directories recursively", "filesharp organize ./downloads -e .pdf --dry-run"
            | Delete ->
                "An explicit --extensions filter is required; use all to select every type.\nDeletion is permanent. A preview is shown before confirmation.\n  -y, --yes              Skip confirmation for scripts (not needed for dry-run)\nWithout --yes, redirected input is rejected. Cancellation makes no changes.", "filesharp delete ./cache -e .tmp,.bak -r --dry-run\n  filesharp delete ./cache -e .tmp,.bak -r --yes"
            | Zip ->
                "One ZIP archive containing selected files, with relative directory paths.\nOriginals stay in place; an existing output is an error. The output parent\ndirectory must exist. Relative output paths use the current working directory.\n  -o, --output PATH      Required destination ending in .zip\n      --level LEVEL      fast | optimal (default) | smallest", "filesharp zip ./logs -e .txt,.log -r -o ./logs.zip"
            | Compress ->
                "Create file.ext.gz beside each matching file. Originals stay in place.\nExisting .gz output is an error; files ending in .gz are skipped.\n      --level LEVEL      fast | optimal (default) | smallest", "filesharp compress ./logs -e .txt,.log -r --level smallest"
            | ListFiles -> "Read-only listing with sizes and paths relative to the selected directory.", "filesharp list ./downloads -e .pdf,.zip -r"
            | Stats -> "Read-only counts and total bytes grouped by final extension, largest first.", "filesharp stats ./downloads -r"
            | Duplicates -> "Read-only duplicate groups: size prefilter followed by SHA-256 content hashes.\nNo files are deleted. Hashing can take time for large directories.", "filesharp duplicates ./photos -e .jpg,.png -r"
            | CleanEmpty -> "Always recursive. Remove empty folders from children to parents; preserve\nthe root, all files, populated folders, and links. No extension filters.", "filesharp clean-empty ./downloads --dry-run"
        let commandOptions =
            [
                if command <> CleanEmpty then yield "  -e, --extensions LIST   Types such as .txt,.log (repeatable); see rules above"
                if not (List.contains command [ Organize; CleanEmpty ]) then yield "  -r, --recursive         Include subdirectories; skip links"
                if List.contains command [ Organize; Delete; Zip; Compress; CleanEmpty ] then yield "  -n, --dry-run           Preview without making changes"
                yield "      --no-color         Disable terminal colors (also honors NO_COLOR)"
                yield "  -h, --help             Show this command's help"
            ] |> String.concat "\n"
        $"""FILESHARP  /  {name}
{description}

USAGE
  filesharp {name} <directory> [options]

{details}

OPTIONS
{commandOptions}

EXAMPLES
  {examples}
"""
