module DirectoryAndFileFunctions

open System
open System.IO
open System.Collections.Generic
open ExtensionData

let IsLink (path: string) =
    File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
    || not (isNull (FileInfo(path).LinkTarget))

// Inspect every ancestor, including the root, rather than only the final component.
let EnsureNoLinks (path: string) =
    let rec inspect (directory: DirectoryInfo) =
        if not (isNull directory) then
            if IsLink directory.FullName then
                invalidArg "path" $"Linked directories are not supported: {directory.FullName}"
            inspect directory.Parent
    inspect (DirectoryInfo path)

let NormalizeRoot (path: string) =
    if String.IsNullOrWhiteSpace path then invalidArg "path" "A directory path is required."
    let root = Path.GetFullPath path |> Path.TrimEndingDirectorySeparator
    if not (Directory.Exists root) then invalidArg "path" "The directory does not exist."
    EnsureNoLinks root
    let app = Path.TrimEndingDirectorySeparator AppContext.BaseDirectory
    let comparison = if OperatingSystem.IsWindows() then StringComparison.OrdinalIgnoreCase else StringComparison.Ordinal
    if root.Equals(app, comparison) then invalidArg "path" "The application directory cannot be organized."
    root

let private occupied path =
    File.Exists path || Directory.Exists path || not (isNull (FileInfo(path).LinkTarget))

let private availablePath (reserved: HashSet<string>) (destination: string) =
    let directory = Path.GetDirectoryName destination
    let name = Path.GetFileNameWithoutExtension destination
    let extension = Path.GetExtension destination
    let mutable candidate = destination
    let mutable suffix = 0
    while occupied candidate || reserved.Contains candidate do
        suffix <- suffix + 1
        candidate <- Path.Combine(directory, $"{name} ({suffix}){extension}")
    reserved.Add candidate |> ignore
    candidate

let OrganizeExtensions root extensions (mappings: Dictionary<string, string>) dryRun report =
    let root = NormalizeRoot root
    if extensions |> List.exists (ExtensionIsValid >> not) then
        invalidArg "extensions" "Use 'all' or dotted extensions such as .txt and .log."
    if List.length extensions > 1 && (extensions |> List.exists (fun extension -> extension.Equals("all", StringComparison.OrdinalIgnoreCase))) then
        invalidArg "extensions" "Use all by itself, without other extensions."
    // Snapshot before creating directories or moving files.
    let files = Directory.GetFiles root |> Array.sort
    let reserved = HashSet<string>(StringComparer.OrdinalIgnoreCase)
    let mutable errors = 0
    for source in files do
        if List.isEmpty extensions
           || (extensions |> List.exists (fun extension ->
               extension.Equals("all", StringComparison.OrdinalIgnoreCase)
               || Path.GetExtension(source).Equals(extension, StringComparison.OrdinalIgnoreCase))) then
            try
                if IsLink source then report $"Skipped link: {source}"
                else
                    let product = ReturnProductDirectoryName mappings source
                    if not (ConfigurationRetrieval.IsSafeDirectoryName product) then
                        invalidArg "configuration" $"Unsafe destination: {product}"
                    let directory = Path.Combine(root, product)
                    if occupied directory && (IsLink directory || not (Directory.Exists directory)) then
                        invalidArg "destination" $"Destination must be an ordinary directory: {directory}"
                    let destination = availablePath reserved (Path.Combine(directory, Path.GetFileName source))
                    if dryRun then report $"Would move: {source} -> {destination}"
                    else
                        Directory.CreateDirectory directory |> ignore
                        EnsureNoLinks directory
                        // Never overwrite, including if another process creates a file after planning.
                        File.Move(source, destination, false)
                        report $"Moved: {source} -> {destination}"
            with
            | :? IOException as ex -> errors <- errors + 1; report $"Error: {source}: {ex.Message}"
            | :? UnauthorizedAccessException as ex -> errors <- errors + 1; report $"Error: {source}: {ex.Message}"
            | :? ArgumentException as ex -> errors <- errors + 1; report $"Error: {source}: {ex.Message}"
    errors

// Preserve the single-extension entry point for existing callers.
let Organize root extension mappings dryRun report =
    OrganizeExtensions root [ extension ] mappings dryRun report

let CleanDirectories root dryRun report =
    let root = NormalizeRoot root
    let mutable errors = 0
    // Iterative post-order traversal avoids stack overflow on deep trees.
    let pending = Stack<string * bool>()
    pending.Push(root, false)
    while pending.Count > 0 do
        let directory, visited = pending.Pop()
        try
            if not (IsLink directory) then
                if visited then
                    if directory <> root && (Directory.EnumerateFileSystemEntries(directory) |> Seq.isEmpty) then
                        if dryRun then report $"Would delete empty directory: {directory}"
                        else
                            Directory.Delete(directory, false)
                            report $"Deleted empty directory: {directory}"
                else
                    pending.Push(directory, true)
                    for child in Directory.GetDirectories directory do
                        pending.Push(child, false)
        with
        | :? IOException as ex -> errors <- errors + 1; report $"Error: {directory}: {ex.Message}"
        | :? UnauthorizedAccessException as ex -> errors <- errors + 1; report $"Error: {directory}: {ex.Message}"
    errors
