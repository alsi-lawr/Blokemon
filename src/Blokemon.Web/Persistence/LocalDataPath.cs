namespace Blokemon.Web.Persistence;

public static class LocalDataPath
{
    public static string Resolve(IConfiguration configuration)
    {
        var configured = configuration["Blokemon:DataDirectory"];
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Blokemon"
            )
            : Path.GetFullPath(configured);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("A local Blokemon data directory is required.");
        }
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "blokemon.db");
    }
}
