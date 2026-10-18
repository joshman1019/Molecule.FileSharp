module FileSharp.Tests

open System
open System.IO
open System.Collections.Generic
open System.IO.Compression
open ConfigurationRetrieval
open ExtensionData
open DirectoryAndFileFunctions

let check condition message = if not condition then failwith message
let rejects action =
    try action(); failwith "Expected invalid input to be rejected."
    with :? ArgumentException -> ()

let withDirectory test =
    // Stay beneath the checkout: macOS system temp paths can contain symlink ancestors.
    let root = Path.Combine(Directory.GetCurrentDirectory(), $".filesharp-tests-{Guid.NewGuid():N}")
    Directory.CreateDirectory root |> ignore
    try test root
    finally Directory.Delete(root, true)

let mappings () =
    let values = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    values.Add("TXT", "Text File")
    values

let runCli workingDirectory arguments input =
    let start = Diagnostics.ProcessStartInfo("dotnet")
    start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "FileSharp.dll"))
    for argument in arguments do start.ArgumentList.Add argument
    start.WorkingDirectory <- workingDirectory
    start.RedirectStandardInput <- true
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    use child = Diagnostics.Process.Start start
    child.StandardInput.Write(input: string)
    child.StandardInput.Close()
    let output = child.StandardOutput.ReadToEndAsync()
    let error = child.StandardError.ReadToEndAsync()
    check (child.WaitForExit(30000)) "CLI timed out"
    child.ExitCode, output.Result, error.Result

let tests = [
    "extension validation", fun () ->
        for value in [ null; ""; "."; ".."; ".*"; ".a/b"; ".a\\b"; "txt" ] do
            check (not (ExtensionIsValid value)) $"Accepted {value}"
        for value in [ "all"; "ALL"; ".c"; ".txt"; ".7z" ] do
            check (ExtensionIsValid value) $"Rejected {value}"
    "portable directory names", fun () ->
        for value in [ null; ""; ".."; "../escape"; "a\\b"; "CON"; "nul.txt"; "LPT1"; "a:"; "a."; " a" ] do
            check (not (IsSafeDirectoryName value)) $"Accepted {value}"
        check (IsSafeDirectoryName "Text File") "Rejected normal name"
    "configuration validation", fun () -> withDirectory (fun root ->
        let path = Path.Combine(root, "extensions.json")
        for json in [ "{}"; "null"; "{\"Extensions\":[{}]}"; "{\"Extensions\":[{\"Extension\":\"TXT\",\"ProductName\":\"../escape\"}]}"; "{\"Extensions\":[{\"Extension\":\"TXT\",\"ProductName\":\"Text\"},{\"Extension\":\"txt\",\"ProductName\":\"Other\"}]}" ] do
            File.WriteAllText(path, json)
            rejects (fun () -> LoadExtensions path |> ignore)
        let bundled = LoadExtensions (Path.Combine(AppContext.BaseDirectory, "extensions.json"))
        check (bundled["TXT"] = "Text File") "Bundled configuration invalid")
    "case-insensitive exact matching", fun () -> withDirectory (fun root ->
        File.WriteAllText(Path.Combine(root, "A.TXT"), "text")
        File.WriteAllText(Path.Combine(root, "b.atxt"), "other")
        check (Organize root ".txt" (mappings()) false ignore = 0) "Move failed"
        check (File.ReadAllText(Path.Combine(root, "Text File", "A.TXT")) = "text") "Uppercase extension missed"
        check (File.Exists(Path.Combine(root, "b.atxt"))) "Matched extension suffix")
    "multiple collisions preserve contents", fun () -> withDirectory (fun root ->
        let destination = Path.Combine(root, "Text File")
        Directory.CreateDirectory destination |> ignore
        for name in [ "a.txt"; "a (1).txt"; "a (2).txt" ] do File.WriteAllText(Path.Combine(destination, name), name)
        File.WriteAllText(Path.Combine(root, "a.txt"), "new")
        check (Organize root "ALL" (mappings()) false ignore = 0) "Collision move failed"
        check (File.ReadAllText(Path.Combine(destination, "a (3).txt")) = "new") "Collision suffix incorrect"
        check (File.ReadAllText(Path.Combine(destination, "a.txt")) = "a.txt") "Overwrote original")
    "dry run leaves filesystem unchanged", fun () -> withDirectory (fun root ->
        File.WriteAllText(Path.Combine(root, "a.txt"), "text")
        Directory.CreateDirectory(Path.Combine(root, "empty")) |> ignore
        check (Organize root "all" (mappings()) true ignore = 0) "Preview failed"
        check (CleanDirectories root true ignore = 0) "Cleanup preview failed"
        check (File.Exists(Path.Combine(root, "a.txt"))) "Preview moved source"
        check (Directory.Exists(Path.Combine(root, "empty"))) "Preview removed directory"
        check (not (Directory.Exists(Path.Combine(root, "Text File")))) "Preview created directory")
    "extensionless files and nonrecursive moves", fun () -> withDirectory (fun root ->
        File.WriteAllText(Path.Combine(root, "LICENSE"), "license")
        Directory.CreateDirectory(Path.Combine(root, "nested")) |> ignore
        File.WriteAllText(Path.Combine(root, "nested", "a.txt"), "nested")
        check (Organize root "all" (mappings()) false ignore = 0) "Move failed"
        check (File.Exists(Path.Combine(root, "No Extension", "LICENSE"))) "Extensionless file missed"
        check (File.Exists(Path.Combine(root, "nested", "a.txt"))) "Moved nested file")
    "cleanup removes every empty level and preserves files", fun () -> withDirectory (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "a", "b", "c")) |> ignore
        Directory.CreateDirectory(Path.Combine(root, "keep")) |> ignore
        File.WriteAllText(Path.Combine(root, "keep", "file"), "keep")
        check (CleanDirectories root false ignore = 0) "Cleanup failed"
        check (not (Directory.Exists(Path.Combine(root, "a")))) "Empty ancestors retained"
        check (File.Exists(Path.Combine(root, "keep", "file"))) "Deleted populated directory"
        check (Directory.Exists root) "Deleted root")
    "invalid root and arguments", fun () ->
        rejects (fun () -> NormalizeRoot "" |> ignore)
        rejects (fun () -> NormalizeRoot AppContext.BaseDirectory |> ignore)
        rejects (fun () -> FileSharp.Program.parseArgs [| "--dry-run" |] |> ignore)
        rejects (fun () -> FileSharp.Program.parseArgs [| "."; "--extension" |] |> ignore)
        rejects (fun () -> FileSharp.Program.parseArgs [| "."; "--unknown" |] |> ignore)
    "per-file failure continues safely", fun () -> withDirectory (fun root ->
        let values = mappings()
        values["TXT"] <- "z-obstruction"
        File.WriteAllText(Path.Combine(root, "z-obstruction"), "obstruction")
        File.WriteAllText(Path.Combine(root, "a.txt"), "text")
        File.WriteAllText(Path.Combine(root, "b.pdf"), "pdf")
        check (Organize root "all" values false ignore = 1) "Missing failure status"
        check (File.ReadAllText(Path.Combine(root, "a.txt")) = "text") "Lost source"
        check (File.ReadAllText(Path.Combine(root, "PDF", "b.pdf")) = "pdf") "Independent move did not continue")
    "CLI loads bundled configuration from another working directory", fun () -> withDirectory (fun root ->
        File.WriteAllText(Path.Combine(root, "a.txt"), "text")
        let exitCode, output, error = runCli root [ root; "--dry-run" ] ""
        check (exitCode = 0) $"CLI failed: {error}"
        check (output.Contains("Text File")) "CLI missed bundled mapping"
        check (File.Exists(Path.Combine(root, "a.txt"))) "CLI dry run moved file")
    "CLI supports redirected interactive input and EOF", fun () -> withDirectory (fun root ->
        File.WriteAllText(Path.Combine(root, "a.txt"), "text")
        let exitCode, _, error = runCli root [] $"{root}\nALL\nn\n"
        check (exitCode = 0) $"Redirected input failed: {error}"
        check (File.Exists(Path.Combine(root, "Text File", "a.txt"))) "Interactive move failed"
        let eofCode, _, eofError = runCli root [] ""
        check (eofCode = 2 && eofError.Contains("Input ended")) "EOF was not handled"
        let invalidCode, _, _ = runCli root [ root; "--extension"; ".*" ] ""
        check (invalidCode = 2) "CLI accepted wildcard extension")
    "maintenance parser validates command-specific options", fun () ->
        let options = FileSharp.Program.parseArgs [| "zip"; "."; "-e"; ".TXT,.log"; "-e"; ".csv"; "-r"; "-o"; "logs.zip"; "--level"; "smallest" |]
        check (options.Command = Cli.Zip && options.Extensions.Length = 3 && options.Recursive) "Multi-filter options lost"
        check (options.Level = CompressionLevel.SmallestSize) "Compression level lost"
        for args in [
            [| "delete"; "." |]; [| "zip"; "." |]; [| "delete"; "."; "-e"; ".tmp," |]
            [| "delete"; "."; "-e"; "all,.tmp" |]; [| "list"; "."; "--yes" |]
            [| "list"; "."; "--level"; "optimal" |]; [| "compress"; "."; "--level"; "huge" |]
            [| "organize"; "."; "-r" |]; [| "organize"; "."; "-e"; "all,.txt" |]
            [| "clean-empty"; "."; "-e"; ".tmp" |]; [| "list"; "."; "-o"; "out.zip" |]
        ] do rejects (fun () -> FileSharp.Program.parseArgs args |> ignore)
    "organize comma-separated extensions selects only exact requested types", fun () -> withDirectory (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "nested")) |> ignore
        for name in [ "a.TXT"; "b.pdf"; "keep.log"; "suffix.atxt"; "LICENSE"; "nested/c.txt" ] do
            File.WriteAllText(Path.Combine(root, name), name)
        let code, output, error = runCli root [ "organize"; root; "-e"; ".txt,.PDF" ] ""
        check (code = 0 && output.Contains("2 file(s) organized")) $"Multi-extension organize failed: {error}"
        check (File.Exists(Path.Combine(root, "Text File", "a.TXT"))) "TXT selection missed"
        check (File.Exists(Path.Combine(root, "PDF Documents", "b.pdf"))) "PDF selection missed"
        for name in [ "keep.log"; "suffix.atxt"; "LICENSE"; "nested/c.txt" ] do
            check (File.ReadAllText(Path.Combine(root, name)) = name) $"Changed unselected file: {name}")
    "organize repeated extensions previews once and supports legacy syntax", fun () -> withDirectory (fun root ->
        for name in [ "a.txt"; "b.pdf"; "keep.log" ] do File.WriteAllText(Path.Combine(root, name), name)
        let args = [ root; "-e"; ".txt"; "--extension"; ".PDF"; "--extensions"; ".TXT" ]
        let code, output, error = runCli root (args @ [ "--dry-run" ]) ""
        check (code = 0 && output.Contains("2 file(s) organized")) $"Repeated filter preview failed: {error}"
        check (Directory.GetFiles(root).Length = 3 && Directory.GetDirectories(root).Length = 0) "Preview changed filesystem"
        let code, output, error = runCli root args ""
        check (code = 0 && output.Contains("2 file(s) organized")) $"Legacy repeated filters failed: {error}"
        check (File.Exists(Path.Combine(root, "Text File", "a.txt"))) "Repeated TXT filter missed"
        check (File.Exists(Path.Combine(root, "PDF Documents", "b.pdf"))) "Repeated PDF filter missed"
        check (File.Exists(Path.Combine(root, "keep.log"))) "Repeated filters moved unrelated file")
    "organize unmatched filters leave files alone and default still selects all", fun () -> withDirectory (fun root ->
        File.WriteAllText(Path.Combine(root, "a.txt"), "text")
        let code, output, _ = runCli root [ "organize"; root; "-e"; ".pdf,.log" ] ""
        check (code = 0 && output.Contains("0 file(s) organized")) "Unmatched filters failed"
        check (File.Exists(Path.Combine(root, "a.txt"))) "Unmatched filter moved file"
        let code, _, error = runCli root [ "organize"; root ] ""
        check (code = 0 && File.Exists(Path.Combine(root, "Text File", "a.txt"))) $"Default selection regressed: {error}")
    "help system covers every command and stays plain when redirected", fun () -> withDirectory (fun root ->
        for name, _, _ in Cli.commands do
            let code, output, error = runCli root [ name; "--help" ] ""
            check (code = 0 && output.Contains("EXAMPLES") && output.Contains(name)) $"Missing help for {name}: {error}"
            check (not (output.Contains("\u001b"))) "Redirected help contains ANSI escapes"
        let code, output, _ = runCli root [ "help"; "zip" ] ""
        check (code = 0 && output.Contains("--output")) "Help topic missing"
        let code, _, _ = runCli root [ "help"; "unknown" ] ""
        check (code = 2) "Unknown help topic accepted"
        let code, output, _ = runCli root [ "--version" ] ""
        check (code = 0 && output.Contains("1.1.0")) "Version missing")
    "delete previews and requires script confirmation", fun () -> withDirectory (fun root ->
        let file = Path.Combine(root, "a.TMP")
        File.WriteAllText(file, "keep until confirmed")
        let code, output, _ = runCli root [ "delete"; root; "-e"; ".tmp"; "--dry-run" ] ""
        check (code = 0 && output.Contains("Would delete")) "Delete preview failed"
        check (File.Exists file) "Dry-run deleted file"
        let code, _, error = runCli root [ "delete"; root; "-e"; ".tmp" ] "DELETE\n"
        check (code = 2 && error.Contains("--yes")) "Piped confirmation bypassed --yes"
        check (File.Exists file) "Unconfirmed deletion changed files")
    "delete filters multiple types and only recurses when requested", fun () -> withDirectory (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "nested")) |> ignore
        for name in [ "a.TMP"; "b.bak"; "keep.txt"; "nested/c.tmp" ] do File.WriteAllText(Path.Combine(root, name), name)
        let code, _, error = runCli root [ "delete"; root; "-e"; ".tmp,.bak"; "--yes" ] ""
        check (code = 0) $"Delete failed: {error}"
        check (not (File.Exists(Path.Combine(root, "a.TMP"))) && not (File.Exists(Path.Combine(root, "b.bak")))) "Filter missed files"
        check (File.Exists(Path.Combine(root, "keep.txt")) && File.Exists(Path.Combine(root, "nested", "c.tmp"))) "Overbroad deletion"
        let code, _, error = runCli root [ "delete"; root; "-e"; ".tmp"; "-r"; "--yes" ] ""
        check (code = 0 && not (File.Exists(Path.Combine(root, "nested", "c.tmp")))) $"Recursive deletion failed: {error}"
        check (Directory.Exists(Path.Combine(root, "nested"))) "Delete removed directory")
    "ZIP selects multiple types and round-trips relative paths", fun () -> withDirectory (fun root ->
        Directory.CreateDirectory(Path.Combine(root, "nested")) |> ignore
        for name in [ "a.TXT"; "b.log"; "nested/a.TXT"; "skip.pdf" ] do File.WriteAllText(Path.Combine(root, name), name)
        let outputPath = Path.Combine(root, "selected.zip")
        let code, _, error = runCli root [ "zip"; root; "-e"; ".txt,.log"; "-r"; "-o"; outputPath ] ""
        check (code = 0) $"ZIP failed: {error}"
        use archive = ZipFile.OpenRead outputPath
        check (archive.Entries.Count = 3) "Wrong ZIP selection"
        for name in [ "a.TXT"; "b.log"; "nested/a.TXT" ] do
            use reader = new StreamReader(archive.GetEntry(name).Open())
            check (reader.ReadToEnd() = name) $"Wrong archive content: {name}"
            check (File.Exists(Path.Combine(root, name))) "ZIP removed original")
    "ZIP preview, empty selection, and overwrite refusal", fun () -> withDirectory (fun root ->
        File.WriteAllText(Path.Combine(root, "a.txt"), "text")
        let outputPath = Path.Combine(root, "preview.zip")
        let code, _, error = runCli root [ "zip"; root; "-e"; ".txt"; "-o"; outputPath; "-n" ] ""
        check (code = 0 && not (File.Exists outputPath)) $"ZIP preview wrote output: {error}"
        let code, _, _ = runCli root [ "zip"; root; "-e"; ".missing"; "-o"; outputPath ] ""
        check (code = 0 && not (File.Exists outputPath)) "Empty ZIP created"
        File.WriteAllText(outputPath, "existing")
        let code, _, _ = runCli root [ "zip"; root; "-e"; ".txt"; "-o"; outputPath ] ""
        check (code = 1 && File.ReadAllText(outputPath) = "existing") "ZIP overwrote destination")
    "ZIP failure removes temporary output and retains sources", fun () -> withDirectory (fun root ->
        let file = Path.Combine(root, "a.txt")
        File.WriteAllText(file, "old")
        let selection = Maintenance.select root [ ".txt" ] false ignore
        File.WriteAllText(file, "changed after preview")
        let output = Path.Combine(root, "fail.zip")
        let mutable failed = false
        try Maintenance.zip selection output CompressionLevel.Optimal false ignore |> ignore
        with :? IOException -> failed <- true
        check failed "ZIP accepted changed source"
        check (not (File.Exists output)) "Failed archive published"
        check (Directory.GetFiles(root, ".filesharp-*.tmp").Length = 0) "Temporary ZIP leaked"
        check (File.ReadAllText(file) = "changed after preview") "ZIP failure changed source"
        let selection = Maintenance.select root [ ".txt" ] false ignore
        let incomplete = { selection with Errors = 1 }
        let result = Maintenance.zip incomplete output CompressionLevel.Optimal false ignore
        check (result.Errors = 1 && not (File.Exists output)) "Partial scan produced incomplete ZIP")
    "ZIP rejects unsafe relative entry paths", fun () -> withDirectory (fun root ->
        File.WriteAllText(Path.Combine(root, "a.txt"), "text")
        let selection = Maintenance.select root [] false ignore
        let unsafe = { selection with Files = selection.Files |> Array.map (fun file -> { file with Relative = "../escape.txt" }) }
        rejects (fun () -> Maintenance.zip unsafe (Path.Combine(root, "out.zip")) CompressionLevel.Optimal false ignore |> ignore))
    "GZip round-trips several types and retains originals", fun () -> withDirectory (fun root ->
        for name in [ "a.txt"; "b.LOG"; "skip.pdf" ] do File.WriteAllText(Path.Combine(root, name), String.replicate 100 name)
        let code, _, error = runCli root [ "compress"; root; "-e"; ".txt,.log"; "--level"; "smallest" ] ""
        check (code = 0) $"GZip failed: {error}"
        for name in [ "a.txt"; "b.LOG" ] do
            use stream = File.OpenRead(Path.Combine(root, name + ".gz"))
            use decompressed = new GZipStream(stream, CompressionMode.Decompress)
            use reader = new StreamReader(decompressed)
            check (reader.ReadToEnd() = File.ReadAllText(Path.Combine(root, name))) "GZip round-trip changed data"
        check (not (File.Exists(Path.Combine(root, "skip.pdf.gz")))) "Compressed unselected type")
    "GZip preview and conflicts retain files and continue", fun () -> withDirectory (fun root ->
        for name in [ "a.txt"; "b.txt" ] do File.WriteAllText(Path.Combine(root, name), name)
        let code, _, _ = runCli root [ "compress"; root; "-e"; ".txt"; "-n" ] ""
        check (code = 0 && Directory.GetFiles(root, "*.gz").Length = 0) "GZip dry-run wrote files"
        File.WriteAllText(Path.Combine(root, "a.txt.gz"), "existing")
        let code, _, _ = runCli root [ "compress"; root ] ""
        check (code = 1 && File.ReadAllText(Path.Combine(root, "a.txt.gz")) = "existing") "GZip conflict overwrote file"
        check (File.Exists(Path.Combine(root, "b.txt.gz"))) "GZip failed to continue"
        check (not (File.Exists(Path.Combine(root, "a.txt.gz.gz")))) "Recompressed GZip output")
    "delete refuses changed snapshot files", fun () -> withDirectory (fun root ->
        let file = Path.Combine(root, "a.tmp")
        File.WriteAllText(file, "old")
        let selection = Maintenance.select root [ ".tmp" ] false ignore
        File.WriteAllText(file, "new contents")
        let result = Maintenance.delete selection false ignore
        check (result.Errors = 1 && File.Exists file) "Deleted a changed file")
    "inventory, stats, duplicates, and clean-empty CLI", fun () -> withDirectory (fun root ->
        File.WriteAllText(Path.Combine(root, "a.TXT"), "same")
        File.WriteAllText(Path.Combine(root, "b.txt"), "same")
        File.WriteAllText(Path.Combine(root, "c.log"), "diff")
        let code, output, _ = runCli root [ "list"; root; "-e"; ".txt"; "--no-color" ] ""
        check (code = 0 && output.Contains("a.TXT") && not (output.Contains("c.log"))) "List filter failed"
        check (not (output.Contains("\u001b"))) "Redirected output contains ANSI escapes"
        let code, output, _ = runCli root [ "stats"; root ] ""
        check (code = 0 && output.Contains("12 B") && output.Contains(".txt")) "Stats total incorrect"
        let code, output, _ = runCli root [ "duplicates"; root ] ""
        check (code = 0 && output.Contains("1 duplicate group") && output.Contains("4 B redundant")) "Duplicate hashing incorrect"
        check (not (output.Contains("    c.log"))) "Same-size different content counted as duplicate"
        Directory.CreateDirectory(Path.Combine(root, "empty", "child")) |> ignore
        let code, _, _ = runCli root [ "clean-empty"; root ] ""
        check (code = 0 && not (Directory.Exists(Path.Combine(root, "empty")))) "Standalone cleanup failed"
        check (Directory.GetFiles(root).Length = 3) "Read-only tools changed files")
    "links stay outside traversal and destinations", fun () -> withDirectory (fun root ->
        // Windows CI uses junctions for directory links (no developer mode required).
        let outside = Path.Combine(root, "outside")
        let target = Path.Combine(root, "target")
        Directory.CreateDirectory(Path.Combine(outside, "empty")) |> ignore
        Directory.CreateDirectory target |> ignore
        let link = Path.Combine(target, "Text File")
        if OperatingSystem.IsWindows() then
            use linkCommand = Diagnostics.Process.Start("cmd", $"/c mklink /J \"{link}\" \"{outside}\"")
            linkCommand.WaitForExit()
            check (linkCommand.ExitCode = 0) "Could not create test junction"
        else Directory.CreateSymbolicLink(link, outside) |> ignore
        try
            File.WriteAllText(Path.Combine(target, "a.txt"), "text")
            check (Organize target ".txt" (mappings()) false ignore = 1) "Followed destination link"
            check (CleanDirectories target false ignore = 0) "Cleanup failed"
            check (Directory.Exists(Path.Combine(outside, "empty"))) "Traversed directory link"
            let selection = Maintenance.select target [] true ignore
            check (selection.Errors = 0 && selection.Files.Length = 1) "Maintenance traversal followed a directory link"
            rejects (fun () -> Maintenance.zip selection (Path.Combine(link, "out.zip")) CompressionLevel.Optimal false ignore |> ignore)
            rejects (fun () -> NormalizeRoot link |> ignore)
            rejects (fun () -> NormalizeRoot (Path.Combine(link, "empty")) |> ignore)
            if not (OperatingSystem.IsWindows()) then
                let fileLink = Path.Combine(target, "linked.txt")
                File.CreateSymbolicLink(fileLink, Path.Combine(outside, "missing")) |> ignore
                check (Organize target ".txt" (mappings()) false ignore = 1) "Unexpected link handling"
                check (not (isNull (FileInfo(fileLink).LinkTarget))) "Moved dangling link"
        finally Directory.Delete link)
]

[<EntryPoint>]
let main _ =
    let mutable failures = 0
    for name, test in tests do
        try test(); printfn "PASS %s" name
        with ex -> failures <- failures + 1; eprintfn "FAIL %s: %s" name ex.Message
    printfn "%d tests, %d failures" tests.Length failures
    if failures = 0 then 0 else 1
