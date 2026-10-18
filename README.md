# FileSharp

FileSharp is an F# command-line file organizer for Windows, macOS, and Linux.
It groups files from one directory into folders using the mappings in
`extensions.json`. File selection is case-insensitive on every platform.

Requires the .NET 10 SDK to build, or the .NET 10 runtime to run a published,
framework-dependent build. The application uses only the runtime and FSharp.Core;
no configuration packages are needed.

```sh
dotnet build FileSharp.sln --configuration Release
dotnet run --project FileSharp -- "/path/to/files" --dry-run
dotnet run --project FileSharp -- "/path/to/files" --extension .txt
dotnet run --project FileSharp -- "/path/to/files" --clean-empty
```

On Windows, pass a path such as `"C:\Users\you\Downloads"`. Quote paths that
contain spaces. Run without arguments for interactive prompts, or use `--help`
for usage. Both piped input and terminals are supported.

## Behavior and safeguards

- Only files directly inside the selected directory are moved. Subdirectory
  contents are left in place.
- `--extension` accepts `all` (the default) or a dotted extension such as `.c`
  or `.txt`. Matching ignores case and matches the entire final extension.
- Existing destination files and directories are preserved. Conflicting files
  receive numbered names such as `report (1).txt`, `report (2).txt`, and so on.
  A move never overwrites an existing file, even if a conflict appears mid-run.
- Unmapped extensions use an uppercase extension folder. Extensionless files go
  into `No Extension`; extensions that cannot form portable folder names go into
  `Other Files`.
- `--dry-run` prints proposed moves and currently empty directories without
  creating, moving, or deleting anything. Empty-parent predictions may differ
  from a real cleanup because the preview does not mutate the tree.
- `--clean-empty` opts into recursive empty-directory removal. Cleanup proceeds
  from children to parents and never recursively deletes contents or removes the
  selected root. Files and populated directories are retained.
- File links are skipped. Directory symlinks, junctions, and reparse points are
  never intentionally traversed. A selected root with a linked ancestor is
  rejected; use its physical path (including on macOS, where `/tmp` is a link).
  Linked destination directories are rejected.
- The executable's own directory cannot be selected. Choose a separate data
  directory. File-system failures are reported; independent moves continue.
- Exit codes: `0` success (including a dry run), `1` file-system or per-file
  failure, `2` invalid arguments, input, or configuration. Skipped links are
  informational.

Use this on directories you control. Link checks are best-effort checks using
portable .NET APIs, not a security boundary against concurrent replacement of
files or directories by another process. There is no transaction or undo;
preview first and keep backups of valuable files.

## Configuration

`extensions.json` is loaded beside the executable, independently of the current
working directory, and is included in build and publish output. Edit that copy
to customize a deployed application. Every mapping must have a unique extension
(case-insensitive, without a dot) and a single portable folder name:

```json
{
  "Extensions": [
    { "Extension": "TXT", "ProductName": "Text File" }
  ]
}
```

Extensions accept ASCII letters, digits, `_`, and `-`. Folder names cannot be
empty, relative paths, Windows device names, or contain separators, control
characters, or Windows-invalid characters. Leading/trailing whitespace and
trailing dots are rejected. Invalid configuration is rejected before any moves.

## Validation and publishing

The regression suite creates isolated disposable directories and exercises input,
configuration, collisions, dry runs, recursive cleanup, and link handling:

```sh
dotnet run --project FileSharp.Tests --configuration Release
dotnet publish FileSharp/FileSharp.fsproj --configuration Release --output artifacts/publish
dotnet artifacts/publish/FileSharp.dll --help
```

The tests are a dependency-light executable runner; `dotnet test` does not run
this suite. CI runs the commands above on Windows, macOS, and Linux. Windows
link tests use junctions so developer mode is not required.

For a standalone executable, publish on a machine with the SDK using the target
runtime identifier, for example:

```sh
dotnet publish FileSharp/FileSharp.fsproj --configuration Release --runtime linux-x64 --self-contained true --output artifacts/linux-x64
```

Use the appropriate RID for the destination, such as `win-x64`, `osx-arm64`, or
`linux-arm64`. Keep `extensions.json` alongside the published executable.
