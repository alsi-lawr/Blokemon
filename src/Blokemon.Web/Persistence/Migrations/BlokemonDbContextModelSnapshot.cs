using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Blokemon.Web.Persistence.Migrations;

[DbContext(typeof(BlokemonDbContext))]
public partial class BlokemonDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.11");
        modelBuilder.Entity(
            "Blokemon.Web.Persistence.StateDocument",
            entity =>
            {
                entity.Property<string>("Key").HasMaxLength(160).HasColumnType("TEXT");
                entity.Property<string>("Json").IsRequired().HasColumnType("TEXT");
                entity.Property<long>("Revision").IsConcurrencyToken().HasColumnType("INTEGER");
                entity.HasKey("Key");
                entity.ToTable("StateDocuments");
            }
        );
#pragma warning restore 612, 618
    }
}
