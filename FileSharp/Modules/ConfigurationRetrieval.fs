module ConfigurationRetrieval

open Microsoft.Extensions.Configuration

type public ExtensionData = {
    Extension: string
    ProductName: string
}

let public GetConfigurationExtensions() =
    let mutable extensions: ExtensionData list = []
    ConfigurationBuilder().AddJsonFile("extensions.json", false).Build()
    |> fun config -> config.GetSection("extensions").Bind(extensions)
    extensions

