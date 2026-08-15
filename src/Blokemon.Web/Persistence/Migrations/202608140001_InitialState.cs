using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blokemon.Web.Persistence.Migrations;

[DbContext(typeof(BlokemonDbContext))]
[Migration("202608140001_InitialState")]
public partial class InitialState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StateDocuments",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                Json = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StateDocuments", x => x.Key);
            }
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StateDocuments");
    }
}
