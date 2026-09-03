using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blokemon.Web.Persistence.Migrations;

/// <summary>
/// Widens the key column for account-scoped keys and deletes the single-tenant documents that
/// lived under the literal keys. Nothing is migrated (BLOKEMON-D-038): production is
/// browser-only, so no server data exists to carry over. This runs exactly once per database
/// through the start-up <c>MigrateAsync()</c>; no runtime code path deletes these keys.
/// </summary>
[DbContext(typeof(BlokemonDbContext))]
[Migration("202609030001_AccountScopedKeys")]
public partial class AccountScopedKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "StateDocuments",
            type: "TEXT",
            maxLength: StateDocument.MaximumKeyLength,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 64
        );
        migrationBuilder.Sql(
            "DELETE FROM StateDocuments WHERE Key IN ('profile', 'match', 'match-history')"
        );
    }

    // SQLite carries no declared text length, so there is no narrower column to return to, and
    // the deleted legacy rows are gone by decision. Nothing to undo.
    protected override void Down(MigrationBuilder migrationBuilder) { }

    // SQLite applies a column change by rebuilding the table, which needs the model this
    // migration arrives at; the model snapshot states the same shape.
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.11");
        modelBuilder.Entity(
            "Blokemon.Web.Persistence.StateDocument",
            entity =>
            {
                entity
                    .Property<string>("Key")
                    .HasMaxLength(StateDocument.MaximumKeyLength)
                    .HasColumnType("TEXT");
                entity.Property<string>("Json").IsRequired().HasColumnType("TEXT");
                entity.Property<long>("Revision").IsConcurrencyToken().HasColumnType("INTEGER");
                entity.HasKey("Key");
                entity.ToTable("StateDocuments");
            }
        );
#pragma warning restore 612, 618
    }
}
