open System; 
open DirectoryAndFileFunctions
open ExtensionData

// Provide a welcome message to the user and prompt for the path to organize files
printfn "Welcome to FileSharp"
printfn "Enter the path where you would like to organize files"
let userPath = Console.ReadLine()

// If the path is the same as the applicaiton path, exit the application
if userPath = System.AppDomain.CurrentDomain.BaseDirectory then
    printfn "The path you entered is the same as the application path. Exiting."
    Environment.Exit(1)

// Verify the path is valid
match IO.Directory.Exists(userPath) with
| true -> printfn $"The path {userPath} is verified as valid."
| false ->
    printfn "Invalid Path"
    Environment.Exit(1)

// Prompt the user for the file extension to organize
printfn "Enter the extension of the files you would like to organize, otherwise enter 'all' to organize all files"
let extension = Console.ReadLine()

// Prompt the user if they would like to delete empty directories after organizing files
printfn "Would you like to delete empty directories after organizing files? (Y/N)"
let deleteEmpties = 
    match Console.ReadKey().KeyChar with
    | 'Y'
    | 'y' ->
        printfn "\nDeleting empty directories after organizing files"
        true
    | _ -> 
        printfn "\nNot deleting empty directories after organizing files"
        false

// Message to the user to indicate the files are being organized (with or without specific extension)
match extension with 
| "all" -> printfn "Organizing all files"
| _ -> printfn $"Organizing files with the extension: {extension}"

// Validate the extension
match ExtensionIsValid extension with
| true -> printfn "The extension was validated or ALL files were selected"
| false -> 
    printfn "Invalid extension"
    Environment.Exit(1)

// Get the files to organize
let files =
    match extension with
    | "all" -> GetAllFiles userPath
    | _ -> GetAllFilesWithExtension (userPath, extension)

// Organize the files
match files.Length with
| 0 -> 
    printfn "No files found. Nothing to organize"
| _ -> 
    for file in files do
        let filename = GetFileName file
        let fileExtension = GetNormalizedFileExtension file 
        let finalProductDirectoryName = ReturnProductDirectoryName fileExtension
        DirectoryCreator (userPath, finalProductDirectoryName) |> ignore
        let outputFilePath = BuildPath [|userPath; finalProductDirectoryName; filename|]
        FileMover file outputFilePath |> ignore
        printfn $"Moved {filename} to {ReturnProductDirectoryName}"

// Clean directories if the user selected to do so
if deleteEmpties then
    printfn "Cleaning directories"
    CleanDirectories userPath
    printfn "Cleaning complete"
    printfn "Organizing complete"
else
    printfn "Skipping directory cleaning"
    printfn "Organizing complete"