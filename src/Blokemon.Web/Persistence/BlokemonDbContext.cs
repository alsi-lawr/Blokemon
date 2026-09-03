using Microsoft.EntityFrameworkCore;

namespace Blokemon.Web.Persistence;

public sealed class BlokemonDbContext(DbContextOptions<BlokemonDbContext> options)
    : DbContext(options)
{
    public DbSet<StateDocument> StateDocuments => Set<StateDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<StateDocument>();
        document.ToTable("StateDocuments");
        document.HasKey(static row => row.Key);
        document.Property(static row => row.Key).HasMaxLength(StateDocument.MaximumKeyLength);
        document.Property(static row => row.Revision).IsConcurrencyToken();
        document.Property(static row => row.Json);
    }
}

public sealed class StateDocument
{
    /// <summary>
    /// The longest key the store accepts. SQLite does not enforce the declared length, so the
    /// store refuses longer keys itself. The longest key the application composes from fixed
    /// literals and minted identities, an approval's, is 82 characters.
    /// </summary>
    public const int MaximumKeyLength = 160;

    public required string Key { get; init; }

    public long Revision { get; set; }

    public required string Json { get; set; }
}
