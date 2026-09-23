using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaHashVestigio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HashSha256",
                table: "VESTIGIO",
                type: "char(64)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_VESTIGIO_HashSha256",
                table: "VESTIGIO",
                column: "HashSha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VESTIGIO_HashSha256",
                table: "VESTIGIO");

            migrationBuilder.DropColumn(
                name: "HashSha256",
                table: "VESTIGIO");
        }
    }
}
