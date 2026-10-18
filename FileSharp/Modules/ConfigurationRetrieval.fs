module ConfigurationRetrieval

open System
open System.IO
open System.Text.Json
open System.Collections.Generic

// Apply Windows naming restrictions on every OS so configuration stays portable.
let IsSafeDirectoryName (name: string) =
    if String.IsNullOrWhiteSpace name then false
    else
        let stem = (name.Split('.')[0]).ToUpperInvariant()
        let reserved =
            [ "CON"; "PRN"; "AUX"; "NUL" ]
            @ [ for i in 1..9 do yield $"COM{i}"; yield $"LPT{i}" ]
        name <> "." && name <> ".." && name = name.Trim()
        && not (name.EndsWith('.'))
        && not (name |> Seq.exists (fun c -> Char.IsControl c || "<>:\"/\\|?*".Contains c))
        && not (List.contains stem reserved)

let IsExtensionToken (value: string) =
    not (String.IsNullOrWhiteSpace value)
    && (value |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '_' || c = '-'))

let LoadExtensions (path: string) =
    use stream = File.OpenRead path
    use document = JsonDocument.Parse stream
    let mappings = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    let mutable entries = Unchecked.defaultof<JsonElement>
    if document.RootElement.ValueKind <> JsonValueKind.Object
       || not (document.RootElement.TryGetProperty("Extensions", &entries))
       || entries.ValueKind <> JsonValueKind.Array then
        invalidArg "configuration" "Expected an Extensions array."
    for entry in entries.EnumerateArray() do
        let readString (name: string) =
            let mutable value = Unchecked.defaultof<JsonElement>
            if entry.ValueKind <> JsonValueKind.Object
               || not (entry.TryGetProperty(name, &value))
               || value.ValueKind <> JsonValueKind.String then
                invalidArg "configuration" $"Each mapping requires a string {name}."
            value.GetString()
        let extension = readString "Extension"
        let product = readString "ProductName"
        if not (IsExtensionToken extension) || not (IsSafeDirectoryName product) then
            invalidArg "configuration" $"Invalid extension mapping: {extension} -> {product}."
        if not (mappings.TryAdd(extension, product)) then
            invalidArg "configuration" $"Duplicate extension mapping: {extension}."
    mappings
