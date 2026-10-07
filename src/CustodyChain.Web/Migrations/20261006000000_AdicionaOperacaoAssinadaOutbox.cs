using CustodyChain.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations;

[DbContext(typeof(CustodyChainDbContext))]
[Migration("20261006000000_AdicionaOperacaoAssinadaOutbox")]
public partial class AdicionaOperacaoAssinadaOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OperacaoAssinadaHashSha256",
            table: "REGISTRO_LEDGER",
            type: "char(64)",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "OperacaoAssinadaId",
            table: "REGISTRO_LEDGER",
            type: "varchar(120)",
            maxLength: 120,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "OperacaoAssinadaJson",
            table: "REGISTRO_LEDGER",
            type: "longtext",
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<byte>(
            name: "VersaoOperacaoAssinada",
            table: "REGISTRO_LEDGER",
            type: "tinyint unsigned",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_REGISTRO_LEDGER_OperacaoAssinadaId",
            table: "REGISTRO_LEDGER",
            column: "OperacaoAssinadaId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_REGISTRO_LEDGER_OperacaoAssinadaId", table: "REGISTRO_LEDGER");
        migrationBuilder.DropColumn(name: "OperacaoAssinadaHashSha256", table: "REGISTRO_LEDGER");
        migrationBuilder.DropColumn(name: "OperacaoAssinadaId", table: "REGISTRO_LEDGER");
        migrationBuilder.DropColumn(name: "OperacaoAssinadaJson", table: "REGISTRO_LEDGER");
        migrationBuilder.DropColumn(name: "VersaoOperacaoAssinada", table: "REGISTRO_LEDGER");
    }
}
