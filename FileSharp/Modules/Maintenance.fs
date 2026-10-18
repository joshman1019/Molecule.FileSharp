module Maintenance

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Collections.Generic
open DirectoryAndFileFunctions

type SelectedFile = { Path: string; Relative: string; Length: int64; Modified: DateTime }
type Selection = { Root: string; Files: SelectedFile array; Errors: int }
type Result = { Completed: int; Errors: int }

let private isOperational (ex: exn) =
    ex :? IOException || ex :? UnauthorizedAccessException || ex :? ArgumentException

let private matches extensions (path: string) =
    List.isEmpty extensions
    || extensions |> List.exists (fun (extension: string) ->
        extension.Equals("all", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))

let select root extensions recursive report =
    let root = NormalizeRoot root
    if extensions |> List.exists (ExtensionData.ExtensionIsValid >> not) then
        invalidArg "extensions" "Invalid extension filter."
    let files = ResizeArray<SelectedFile>()
    let pending = Stack<string>()
    pending.Push root
    let mutable errors = 0
    while pending.Count > 0 do
        let directory = pending.Pop()
        try
            EnsureNoLinks directory
            for path in Directory.GetFileSystemEntries directory |> Array.sort do
                try
                    if IsLink path then report $"Skipped link: {path}"
                    elif Directory.Exists path then
                        if recursive then pending.Push path
                    elif matches extensions path then
                        let info = FileInfo path
                        files.Add { Path = path; Relative = Path.GetRelativePath(root, path); Length = info.Length; Modified = info.LastWriteTimeUtc }
                with ex when isOperational ex ->
                    errors <- errors + 1
                    report $"Error: {path}: {ex.Message}"
        with ex when isOperational ex ->
            errors <- errors + 1
            report $"Error: {directory}: {ex.Message}"
    { Root = root; Files = files.ToArray() |> Array.sortBy _.Relative; Errors = errors }

// Revalidate after a preview or user prompt. Portable checks cannot close all races.
let private validateCurrent file =
    EnsureNoLinks (Path.GetDirectoryName file.Path)
    if IsLink file.Path then invalidArg "file" $"File became a link: {file.Relative}"
    let info = FileInfo file.Path
    if info.Length <> file.Length || info.LastWriteTimeUtc <> file.Modified then
        raise (IOException $"File changed since selection: {file.Relative}")

let private perFile (selection: Selection) action report =
    let mutable completed = 0
    let mutable errors = selection.Errors
    for file in selection.Files do
        try
            action file
            completed <- completed + 1
        with ex when isOperational ex ->
            errors <- errors + 1
            report $"Error: {file.Relative}: {ex.Message}"
    { Completed = completed; Errors = errors }

let delete selection dryRun report =
    perFile selection (fun file ->
        validateCurrent file
        if dryRun then report $"Would delete: {file.Relative}"
        else
            File.Delete file.Path
            report $"Deleted: {file.Relative}") report

let private exists path =
    File.Exists path || Directory.Exists path || not (isNull (FileInfo(path).LinkTarget))

let validateOutput path =
    let full = Path.GetFullPath path
    let parent = Path.GetDirectoryName full
    if not (Directory.Exists parent) then invalidArg "output" "The output parent directory must already exist."
    EnsureNoLinks parent
    if exists full then raise (IOException $"Output already exists: {full}")
    full

// Write to a private sibling first. Failed archives never appear as completed output.
let private atomicOutput path write report =
    let path = validateOutput path
    let parent = Path.GetDirectoryName path
    let temporary = Path.Combine(parent, $".filesharp-{Guid.NewGuid():N}.tmp")
    let mutable created = false
    try
        do
            use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
            created <- true
            write stream
            stream.Flush(true)
        EnsureNoLinks parent
        File.Move(temporary, path, false)
    finally
        if created && File.Exists temporary then
            try File.Delete temporary
            with ex when isOperational ex -> report $"Error: Could not remove temporary file {temporary}: {ex.Message}"

let zip (selection: Selection) (output: string) (level: CompressionLevel) dryRun report =
    if not (Path.GetExtension(output).Equals(".zip", StringComparison.OrdinalIgnoreCase)) then
        invalidArg "output" "ZIP output must end in .zip."
    let output = validateOutput output
    let names = HashSet<string>(StringComparer.OrdinalIgnoreCase)
    let entries = selection.Files |> Array.map (fun file ->
        let components = file.Relative.Split(Path.DirectorySeparatorChar)
        if components |> Array.exists (ConfigurationRetrieval.IsSafeDirectoryName >> not) then
            invalidArg "file" $"ZIP entry is not a portable path: {file.Relative}"
        let name = String.Join("/", components)
        if not (names.Add name) then invalidArg "file" $"ZIP entries differ only in case: {file.Relative}"
        file, name)
    // A partial scan must never silently produce an incomplete backup.
    if selection.Errors > 0 then
        report "Error: ZIP cancelled because some directories or files could not be scanned."
        { Completed = 0; Errors = selection.Errors }
    elif entries.Length = 0 then
        report "No matching files. No archive created."
        { Completed = 0; Errors = 0 }
    else
        if dryRun then
            for file, name in entries do report $"Would archive: {file.Relative} -> {name}"
            report $"Would create ZIP: {output}"
        else
            atomicOutput output (fun stream ->
                use archive = new ZipArchive(stream, ZipArchiveMode.Create, true)
                for file, name in entries do
                    validateCurrent file
                    use source = File.OpenRead file.Path
                    let entry = archive.CreateEntry(name, level)
                    // ZIP timestamps support 1980 through 2107 only.
                    if file.Modified.Year >= 1980 && file.Modified.Year <= 2107 then
                        entry.LastWriteTime <- DateTimeOffset(file.Modified)
                    use destination = entry.Open()
                    source.CopyTo destination
                    validateCurrent file
                    report $"Archived: {file.Relative}") report
            report $"Created ZIP: {output}"
        { Completed = entries.Length; Errors = 0 }

let compress (selection: Selection) (level: CompressionLevel) dryRun report =
    let files = selection.Files |> Array.filter (fun file ->
        if Path.GetExtension(file.Path).Equals(".gz", StringComparison.OrdinalIgnoreCase) then
            report $"Skipped existing GZip file: {file.Relative}"
            false
        else true)
    perFile { selection with Files = files } (fun file ->
        validateCurrent file
        let output = validateOutput (file.Path + ".gz")
        if dryRun then report $"Would compress: {file.Relative} -> {file.Relative}.gz"
        else
            atomicOutput output (fun stream ->
                use source = File.OpenRead file.Path
                use compressed = new GZipStream(stream, level, true)
                source.CopyTo compressed
                validateCurrent file) report
            report $"Compressed: {file.Relative} -> {file.Relative}.gz") report

// Size grouping avoids hashing unique-size files. All hashing uses bounded buffers.
let duplicates (selection: Selection) report =
    let groups = ResizeArray<SelectedFile array>()
    let mutable errors = selection.Errors
    for _, candidates in selection.Files |> Array.groupBy _.Length do
        if candidates.Length > 1 then
            let hashes = ResizeArray<string * SelectedFile>()
            for file in candidates do
                try
                    validateCurrent file
                    use stream = File.OpenRead file.Path
                    let hash = SHA256.HashData stream |> Convert.ToHexString
                    validateCurrent file
                    hashes.Add(hash, file)
                with ex when isOperational ex ->
                    errors <- errors + 1
                    report $"Error: {file.Relative}: {ex.Message}"
            for _, matches in hashes |> Seq.groupBy fst do
                let files = matches |> Seq.map snd |> Seq.toArray
                if files.Length > 1 then groups.Add files
    groups.ToArray() |> Array.sortBy (fun files -> files[0].Relative), errors
