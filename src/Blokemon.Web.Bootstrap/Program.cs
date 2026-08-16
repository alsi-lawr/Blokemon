using System.Text;
using Blokemon.Web.Content;

if (args is not [var contentRoot, var outputPath])
{
    Console.Error.WriteLine("Usage: Blokemon.Web.Bootstrap <content-root> <catalogue-output-path>");
    return 2;
}

var catalogue = BlokemonCatalogueBuilder.Load(Path.GetFullPath(contentRoot));
var output = Path.GetFullPath(outputPath);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, catalogue.ToBootstrapJson(), new UTF8Encoding(false));
Console.WriteLine($"Wrote {new FileInfo(output).Length:N0} bytes to {output}");
return 0;
