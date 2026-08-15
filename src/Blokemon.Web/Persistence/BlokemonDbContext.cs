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
        document.Property(static row => row.Key).HasMaxLength(64);
        document.Property(static row => row.Revision).IsConcurrencyToken();
        document.Property(static row => row.Json);
    }
}

public sealed class StateDocument
{
    public required string Key { get; init; }

    public long Revision { get; set; }

    public required string Json { get; set; }
}
