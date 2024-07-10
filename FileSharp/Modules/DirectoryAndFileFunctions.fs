module DirectoryAndFileFunctions
open System

/// <summary>
/// Creates a directory in the specified parent directory
/// </summary>
let public DirectoryCreator (parentDirectory:string, directoryName:string) =
    let directoryPath = IO.Path.Combine(parentDirectory, directoryName)
    IO.Directory.CreateDirectory(directoryPath) |> ignore

/// <summary>
/// Moves a file from one location to another
/// </summary>
/// <param name="inputFilePath"></param>
/// <param name="outputFilePath"></param>
let public FileMover (inputFilePath) (outputFilePath) =
    match IO.File.Exists(outputFilePath) with
    | true ->
        let newOutputFilePath = IO.Path.Combine(IO.Path.GetDirectoryName(outputFilePath), $"Copy_{IO.Path.GetFileName(outputFilePath)}")
        IO.File.Move(inputFilePath, newOutputFilePath)
    | false ->
        IO.File.Move(inputFilePath, outputFilePath)

/// <summary>
/// Retrieves the file name from a file path 
/// </summary>
/// <param name="inputFilePath"></param>
let public GetFileName (inputFilePath:string) =
    IO.Path.GetFileName(inputFilePath)

/// <summary>
/// Retrieves a file extension from a file path 
/// </summary>
/// <param name="inputFilePath"></param>
let public GetFileExtension (inputFilePath:string) =
    IO.Path.GetExtension(inputFilePath)

/// <summary>
/// Retrieves a normalized file extension from a file path
/// NOTE: Normalized indicates that all dots are removed and
/// all characters are uppercased
/// </summary>
/// <param name="inputFilePath"></param>
let public GetNormalizedFileExtension (inputFilePath:string) =
    IO.Path.GetExtension(inputFilePath).TrimStart('.').ToUpper()

/// <summary>
/// Similar to System.IO.Path.Combine, but functional
/// </summary>
/// <param name="paths"></param>
let public BuildPath ([<ParamArray>]paths: string[]) =
    IO.Path.Combine(paths)

/// <summary>
/// Retrieve all files from a directory with a specific extension
/// </summary>
/// <param name="directoryPath"></param>
/// <param name="extension"></param>
let public GetAllFilesWithExtension (directoryPath:string, extension:string) =
    System.IO.Directory.GetFiles(directoryPath, $"*{extension}")

/// <summary>
/// Retrieve all files from a directory path 
/// </summary>
/// <param name="directoryPath"></param>
let public GetAllFiles (directoryPath:string) =
    System.IO.Directory.GetFiles(directoryPath)

/// <summary>
/// Retrieve all directories from a directory path 
/// </summary>
/// <param name="directoryPath"></param>
let public GetAllDirectories (directoryPath:string) =
    System.IO.Directory.GetDirectories(directoryPath)

/// <summary>
/// Retrieve a count of all files in a directory
/// </summary>
/// <param name="directoryPath"></param>
let public GetFileCount (directoryPath:string) =
    System.IO.Directory.GetFiles(directoryPath).Length

/// <summary>
/// Retrieve a count of all directories within another directory
/// </summary>
/// <param name="directoryPath"></param>
let public GetInternalDirectoryCount (directoryPath:string) =
    System.IO.Directory.GetDirectories(directoryPath).Length

/// <summary>
/// Delete a directory
/// </summary>
/// <param name="directoryPath"></param>
let public DeleteDirectory (directoryPath:string) =
    System.IO.Directory.Delete(directoryPath, false)
    printfn $"Deleted directory {directoryPath}"

/// <summary>
/// Deletes a directory and all of its contents, including subdirectories
/// </summary>
/// <param name="directoryPath"></param>
let public DeleteDirectoryRecursive (directoryPath:string) =
    System.IO.Directory.Delete(directoryPath, true)
    printfn $"Deleted directory {directoryPath}"

/// <summary>
/// Removes all empty directories from a root directory
/// </summary>
/// <param name="rootDirectoryPath"></param>
let rec public CleanDirectories (rootDirectoryPath:string) =
    let directories = System.IO.Directory.GetDirectories(rootDirectoryPath)
    for directory in directories do
        if GetInternalDirectoryCount directory > 0 then
            let internalDirectories = GetAllDirectories(directory)
            for internalDirectory in internalDirectories do
                CleanDirectories internalDirectory
        let fileCount = GetFileCount directory
        let newDirectoryCount = GetInternalDirectoryCount directory
        if newDirectoryCount = 0 then
            match fileCount with
            | 0 -> DeleteDirectory directory
            | _ -> printfn $"Directory {directory} is not empty. The directory actually contains {fileCount} files. Skipping deletion"
