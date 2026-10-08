using CustodyChain.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations;

[DbContext(typeof(CustodyChainDbContext))]
[Migration("20261008000000_AdicionaAssetRefVestigio")]
public partial class AdicionaAssetRefVestigio : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AssetRef",
            table: "VESTIGIO",
            type: "varchar(45)",
            maxLength: 45,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_VESTIGIO_AssetRef",
            table: "VESTIGIO",
            column: "AssetRef",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_VESTIGIO_AssetRef", table: "VESTIGIO");
        migrationBuilder.DropColumn(name: "AssetRef", table: "VESTIGIO");
    }
}
