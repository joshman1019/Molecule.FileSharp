# FileSharp

A cross-platform F# CLI for organizing folders, cleaning up file types, archiving
files, and finding wasted space. Runs on Windows, macOS, and Linux with .NET 10.

## Get started

Build and run with the .NET 10 SDK:

```sh
dotnet build FileSharp.sln --configuration Release
dotnet run --project FileSharp -- help
dotnet run --project FileSharp -- stats "/path/to/downloads" --recursive
```

Build a local .NET tool package and install it into this checkout:

```sh
dotnet pack FileSharp/FileSharp.fsproj --configuration Release --output artifacts/packages
dotnet tool install Molecule.FileSharp --version 1.1.0 --tool-path artifacts/tools --add-source artifacts/packages
./artifacts/tools/filesharp help
```

On Windows, use `artifacts\tools\filesharp.exe`. The examples below use
`filesharp` for brevity; substitute the installed path or
`dotnet run --project FileSharp --`. Quote paths containing spaces.

## Commands

| Command | Purpose | Example |
| --- | --- | --- |
| `organize` | Group files into mapped folders | `filesharp organize ./downloads -e .pdf -n` |
| `delete` | Delete matching file types | `filesharp delete ./cache -e .tmp,.bak -r -n` |
| `zip` | Collect selected files into one ZIP | `filesharp zip ./logs -e .txt,.log -r -o ./logs.zip` |
| `compress` | Create an individual GZip copy per file | `filesharp compress ./logs -e .log --level smallest` |
| `list` | List paths and sizes | `filesharp list ./downloads -e .zip,.pdf -r` |
| `stats` | Show file counts and total size by extension | `filesharp stats ./downloads -r` |
| `duplicates` | Find identical content without deleting it | `filesharp duplicates ./photos -r` |
| `clean-empty` | Remove empty folders from children to parents | `filesharp clean-empty ./downloads -n` |

Get command-specific instructions and examples with `filesharp help zip` or
`filesharp zip --help`. Use `--version` for the version. With no arguments,
FileSharp prompts for interactive organization. The original
`filesharp <directory> [options]` organization syntax still works.

### Select files

Use `-e` / `--extensions` (or the original `--extension`) with a comma-separated
list, repeated options, or `all`. Matching is case-insensitive and uses the exact
final extension: `.txt` matches `NOTES.TXT`, but not `notes.atxt`. One-character
extensions such as `.c` work. A `.tar.gz` file has final extension `.gz`.

```sh
filesharp list ./downloads -e .jpg,.png -e .webp
filesharp zip ./downloads -e all -o ./backup.zip
```

File tools default to top-level files; `-r` / `--recursive` includes descendants.
`organize` accepts one extension (or `all`) and stays top-level. `clean-empty`
always traverses directories and does not accept file filters. Other commands
default to all files; `delete` requires an explicit selection, including `-e all`
if that is intended. Invalid or inapplicable options produce an error.

### Delete file types

Preview first:

```sh
filesharp delete ./cache -e .tmp,.bak --recursive --dry-run
filesharp delete ./cache -e .tmp,.bak --recursive
```

A real deletion lists the selected paths and asks you to type `DELETE`.
Cancellation makes no changes. For scripts, pass `--yes`; redirected input
cannot answer the deletion prompt. Deletion is permanent and only removes
selected files, leaving directories in place. Files changed since the selection
snapshot are reported as failures rather than deleted.

### ZIP and GZip

`zip` creates one archive with paths relative to the selected directory. This
keeps equally named files in different subfolders distinct. `--output` is
required, must end in `.zip`, and is relative to the current working directory.
Its parent must already exist. File names must be portable across platforms;
unsafe entry paths and entry names differing only in case are rejected.

`compress` writes `file.ext.gz` beside each selected file. Files already ending
in `.gz` are skipped. Both commands keep originals and reject existing output,
including linked output paths. Use `--level fast`, `optimal` (the default), or
`smallest` to trade speed for size. Already compressed data may grow.

Archives are streamed with bounded buffers and written to temporary sibling
files, then published only after success. ZIP aborts if the selection scan is
incomplete or a source changes; an ordinary failure removes its temporary file.
A process or machine crash can leave a `.filesharp-<id>.tmp` file. FileSharp
never deletes originals after compression.

### Inventory and duplicates

`list` prints relative paths and sizes. `stats` groups case-insensitively by
extension, largest total first. Sizes use binary units (KiB, MiB, GiB).
`duplicates` first groups by byte length, then hashes candidates with SHA-256,
showing identical-content groups and redundant bytes. These tools are read-only;
duplicate detection never chooses files to delete.

### Terminal output

Output includes clear command headings, tables, per-file status, selected file
counts and sizes, elapsed time, and a completion/error summary. Supported
terminals use restrained colors. Redirected output is plain text; `NO_COLOR`,
`TERM=dumb`, and `--no-color` also disable color. Errors go to standard error.
Control characters in paths are escaped before display to prevent terminal
escape sequences from filenames.

## Safeguards and compatibility

- `--dry-run` / `-n` previews changes without writing, moving, or deleting.
  Cleanup shows currently empty folders; empty-parent predictions can differ
  because a preview does not mutate the tree.
- File links are skipped; linked directories, junctions, and reparse points are
  never intentionally traversed. Roots with linked ancestors are rejected;
  use the physical path (macOS `/tmp`, for example, is a link).
- The executable's own directory cannot be selected. Use a separate data folder.
- Organization preserves existing destinations using numbered names such as
  `report (1).txt`. Unknown extensions use an uppercase folder name,
  extensionless files use `No Extension`, and nonportable extensions use
  `Other Files`. `organize --clean-empty` adds recursive empty-folder cleanup.
- Empty-folder cleanup never removes the selected root, files, linked folders,
  or populated folders. It only calls nonrecursive directory deletion.
- Independent per-file operations continue after reported failures. ZIP requires
  a complete scan. Exit codes: `0` success or cancellation, `1` operational
  failure, `2` invalid arguments, input, or configuration. Skipped links are
  informational. An empty selection succeeds without creating an archive.

Use FileSharp on directories you control. Portable link and snapshot checks are
best-effort checks, not a security boundary against concurrent replacement of
files or directories. There is no transaction or undo. Preview changes and keep
backups of valuable files. An error can leave earlier successful operations in
place.

## Organization configuration

Only `organize` loads `extensions.json`. The configuration lives beside the
executable and is included in build, publish, and tool-package output. Other
maintenance commands do not depend on this file.

```json
{
  "Extensions": [
    { "Extension": "TXT", "ProductName": "Text File" }
  ]
}
```

Extensions must be unique ignoring case, without a dot, and contain only ASCII
letters, digits, `_`, or `-`. Product names must be single portable folder names:
no relative paths, separators, control characters, Windows-invalid characters,
Windows device names, leading/trailing whitespace, or trailing dots. Invalid
configuration is rejected before organization begins.

## Validate and publish

The dependency-light regression runner exercises the filesystem and real CLI,
including compression round trips and deletion safeguards. `dotnet test` does
not execute this runner. CI builds and runs it on Windows, macOS, and Linux.
Windows link tests use junctions, without requiring developer mode.

```sh
dotnet run --project FileSharp.Tests --configuration Release
dotnet publish FileSharp/FileSharp.fsproj --configuration Release --output artifacts/publish
dotnet artifacts/publish/FileSharp.dll help
```

A framework-dependent publish requires the .NET 10 runtime on the destination.
For standalone distribution, publish for the destination RID:

```sh
dotnet publish FileSharp/FileSharp.fsproj --configuration Release --runtime linux-x64 --self-contained true --output artifacts/linux-x64
```

Other examples include `win-x64`, `osx-arm64`, and `linux-arm64`. Keep
`extensions.json` beside the published executable.
