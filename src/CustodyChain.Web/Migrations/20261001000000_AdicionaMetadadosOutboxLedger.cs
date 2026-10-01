using CustodyChain.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations;

[DbContext(typeof(CustodyChainDbContext))]
[Migration("20261001000000_AdicionaMetadadosOutboxLedger")]
public partial class AdicionaMetadadosOutboxLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ChaveIdempotencia",
            table: "REGISTRO_LEDGER",
            type: "varchar(120)",
            maxLength: 120,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "CredencialId",
            table: "REGISTRO_LEDGER",
            type: "varchar(120)",
            maxLength: 120,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "DidResponsavel",
            table: "REGISTRO_LEDGER",
            type: "varchar(200)",
            maxLength: 200,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "PayloadHashSha256",
            table: "REGISTRO_LEDGER",
            type: "char(64)",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<DateTime>(
            name: "ProcessandoEm",
            table: "REGISTRO_LEDGER",
            type: "datetime(6)",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ProximaTentativaEm",
            table: "REGISTRO_LEDGER",
            type: "datetime(6)",
            nullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Estado",
            table: "REGISTRO_LEDGER",
            type: "varchar(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "varchar(15)",
            oldMaxLength: 15)
            .Annotation("MySql:CharSet", "utf8mb4")
            .OldAnnotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_REGISTRO_LEDGER_ChaveIdempotencia",
            table: "REGISTRO_LEDGER",
            column: "ChaveIdempotencia",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_REGISTRO_LEDGER_Estado_ProximaTentativaEm",
            table: "REGISTRO_LEDGER",
            columns: new[] { "Estado", "ProximaTentativaEm" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_REGISTRO_LEDGER_ChaveIdempotencia", table: "REGISTRO_LEDGER");
        migrationBuilder.DropIndex(name: "IX_REGISTRO_LEDGER_Estado_ProximaTentativaEm", table: "REGISTRO_LEDGER");

        migrationBuilder.DropColumn(name: "ChaveIdempotencia", table: "REGISTRO_LEDGER");
        migrationBuilder.DropColumn(name: "CredencialId", table: "REGISTRO_LEDGER");
        migrationBuilder.DropColumn(name: "DidResponsavel", table: "REGISTRO_LEDGER");
        migrationBuilder.DropColumn(name: "PayloadHashSha256", table: "REGISTRO_LEDGER");
        migrationBuilder.DropColumn(name: "ProcessandoEm", table: "REGISTRO_LEDGER");
        migrationBuilder.DropColumn(name: "ProximaTentativaEm", table: "REGISTRO_LEDGER");

        migrationBuilder.AlterColumn<string>(
            name: "Estado",
            table: "REGISTRO_LEDGER",
            type: "varchar(15)",
            maxLength: 15,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "varchar(20)",
            oldMaxLength: 20)
            .Annotation("MySql:CharSet", "utf8mb4")
            .OldAnnotation("MySql:CharSet", "utf8mb4");
    }
}
