using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using Blokemon.App;
using Blokemon.App.Catalogue;

namespace Blokemon.Web.Client.Application;

/// <summary>
/// What the computer's own runtime exports to the worker that hosts it. Nothing here runs on
/// the page's thread: computerWorker.js boots a second runtime from the page's own framework
/// files and calls these from there, so the class holds its own catalogue and its own computer.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class ComputerWorker
{
    private static ComputerTurn? _computer;

    /// <summary>
    /// Builds the computer from the catalogue the worker fetched, and answers with the rules it
    /// plays by so the page can check them against its own.
    /// </summary>
    [JSExport]
    public static string Prepare(string catalogueJson)
    {
        var computer = new ComputerTurn(BlokemonCatalogue.FromBootstrapJson(catalogueJson));
        _computer = computer;
        return JsonSerializer.Serialize(
            new ComputerVersions(computer.AuthorityVersion, computer.PolicyVersion),
            ComputerVersionsJson.Default.ComputerVersions
        );
    }

    /// <summary>The id of the candidate the policy selects for the saved battle, or null.</summary>
    [JSExport]
    public static string? Decide(string documentJson) =>
        (_computer ?? throw new InvalidOperationException("The computer is not prepared.")).Decide(
            documentJson
        );
}
