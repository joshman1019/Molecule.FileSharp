module ExtensionData
open System

/// <summary>
/// Table of common file extensions and their corresponding product or service names.
/// </summary>
let public ProductTable = [
    "EXE", "Executable File";
    "MSI", "Windows Installer Package";
    "DOC", "Microsoft Word Document";
    "DOCX", "Microsoft Word Open XML Document";
    "XLS", "Microsoft Excel Spreadsheet";
    "XLSX", "Microsoft Excel Open XML Spreadsheet";
    "PPT", "Microsoft PowerPoint Presentation";
    "PPTX", "Microsoft PowerPoint Open XML Presentation";
    "PUB", "Microsoft Publisher Document";
    "ACCDB", "Microsoft Access Database";
    "MDB", "Microsoft Access Database";
    "MD", "Markdown Document";
    "JPG", "Images";
    "PNG", "Images";
    "GIF", "Images";
    "WEBP", "Images";
    "BMP", "Images";
    "SVG", "Professional Images";
    "ICO", "Professional Images";
    "PDF", "PDF Documents";
    "MP3", "Audio Files";
    "MP4", "Video Files";
    "ZIP", "Compressed Files";
    "RAR", "Compressed Files";
    "TXT", "Text Files";
    "PS1", "PowerShell Script";
    "PSM1", "PowerShell Module";
    "BAT", "Batch File";
    "CMD", "Command Script";
    "TORRENT", "Torrent File";
]

/// <summary>
/// Returns the product or service name for a given file extension, or the extension itself if not found.
/// This will be used to create a directory for the file to be stored in.
/// </summary>
/// <param name="normalizedFileExtension"></param>
let public ReturnProductDirectoryName (normalizedFileExtension: string) = 
    ProductTable
    |> List.tryFind (fun (key, _) -> key = normalizedFileExtension)
    |> Option.map (fun (_, value) -> value)
    |> Option.defaultValue normalizedFileExtension

/// <summary>
/// Returns true if the extension is valid, false otherwise. 
/// Extensions must be at least 2 characters long and start with a period.
/// </summary>
/// <param name="extension"></param>
let public ExtensionIsValid (extension:string) =
    if extension.Equals("ALL", StringComparison.OrdinalIgnoreCase) then
        true
    else if extension[0] = '.' && extension.Length > 2 then
        true
    else
        false