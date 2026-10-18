module FileSharp.Tests

open System
open System.IO
open System.Collections.Generic
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
