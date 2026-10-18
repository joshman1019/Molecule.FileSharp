module ExtensionData

open System
open System.IO
open System.Collections.Generic
open ConfigurationRetrieval

let ExtensionIsValid (extension: string) =
    not (String.IsNullOrWhiteSpace extension)
    && (extension.Equals("all", StringComparison.OrdinalIgnoreCase)
        || (extension.StartsWith('.') && IsExtensionToken (extension.Substring 1)))

let ReturnProductDirectoryName (mappings: Dictionary<string, string>) (file: string) =
    let extension = Path.GetExtension(file).TrimStart('.').ToUpperInvariant()
    match mappings.TryGetValue extension with
    | true, product -> product
    | _ when extension = "" -> "No Extension"
    | _ when IsSafeDirectoryName extension -> extension
    | _ -> "Other Files"
