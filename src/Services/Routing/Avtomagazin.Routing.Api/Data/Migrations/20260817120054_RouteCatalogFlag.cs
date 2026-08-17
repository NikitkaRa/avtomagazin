using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avtomagazin.Routing.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RouteCatalogFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCatalog",
                table: "Routes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE "Routes"
                SET "IsCatalog" = TRUE
                WHERE "Id" = 'f1000000-0000-4000-8000-000000000001';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCatalog",
                table: "Routes");
        }
    }
}
