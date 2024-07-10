module ConfigurationRetrieval

open Microsoft.Extensions.Configuration

type public ExtensionData = {
    Extension: string
    ProductName: string
}

let public GetConfigurationExtensions:ExtensionData list = 
    let config = ConfigurationBuilder().AddJsonFile("extensions.json").Build()
    config.GetSection("Extensions").GetChildren()
    |> Seq.map (fun (item) -> 
        {
            Extension = item.Item("Extension")
            ProductName = item.Item("ProductName")
        }
    ) |> List.ofSeq


