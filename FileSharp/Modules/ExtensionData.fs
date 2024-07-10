module ExtensionData

open ConfigurationRetrieval
open System

/// <summary>
/// Returns the product or service name for a given file extension, or the extension itself if not found.
/// This will be used to create a directory for the file to be stored in.
/// </summary>
/// <param name="normalizedFileExtension"></param>
let public ReturnProductDirectoryName (normalizedFileExtension: string) = 
    GetConfigurationExtensions
    |> List.tryFind (fun (item) -> item.Extension.Equals(normalizedFileExtension, StringComparison.OrdinalIgnoreCase))
    |> Option.map (fun (item) -> item.ProductName)
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