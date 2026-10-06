using Microsoft.EntityFrameworkCore.Migrations;

namespace UrlShortener.Infrastructure.Persistence.Migrations;

public sealed partial class InitialLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Links",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Code = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false, collation: "BINARY"),
                OriginalUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false, collation: "BINARY"),
                ClickCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
            },
            constraints: table => table.PrimaryKey("PK_Links", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_Links_Code", table: "Links", column: "Code", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Links_OriginalUrl", table: "Links", column: "OriginalUrl", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "Links");
}