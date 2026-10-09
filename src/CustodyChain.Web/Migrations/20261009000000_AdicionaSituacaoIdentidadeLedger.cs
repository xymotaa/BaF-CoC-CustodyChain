using CustodyChain.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations;

[DbContext(typeof(CustodyChainDbContext))]
[Migration("20261009000000_AdicionaSituacaoIdentidadeLedger")]
public partial class AdicionaSituacaoIdentidadeLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "SituacaoIdentidadeLedger",
            table: "INTERVENIENTE",
            type: "varchar(24)",
            maxLength: 24,
            nullable: false,
            defaultValue: "DESCONHECIDA");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "SituacaoIdentidadeLedger", table: "INTERVENIENTE");
}
