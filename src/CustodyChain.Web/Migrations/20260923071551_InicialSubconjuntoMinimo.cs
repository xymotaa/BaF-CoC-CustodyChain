using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations
{
    /// <inheritdoc />
    public partial class InicialSubconjuntoMinimo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PERFIL",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Codigo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nome = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetodoDid = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PERFIL", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PROCESSO",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Numero = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NomeOperacao = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrgaoOrigem = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DataAbertura = table.Column<DateOnly>(type: "date", nullable: true),
                    Situacao = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSO", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TIPO_VESTIGIO",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Categoria = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Descricao = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AreaPericial = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExigeHash = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TIPO_VESTIGIO", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "INTERVENIENTE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Did = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PerfilId = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Nome = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Matricula = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Orgao = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Lotacao = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Situacao = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DidEmissor = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AtivadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INTERVENIENTE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_INTERVENIENTE_PERFIL_PerfilId",
                        column: x => x.PerfilId,
                        principalTable: "PERFIL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VESTIGIO",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RotuloEvidencia = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RotuloConjunto = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NumeroEvidencia = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProcessoId = table.Column<long>(type: "bigint", nullable: false),
                    TipoVestigioId = table.Column<short>(type: "smallint", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadorId = table.Column<long>(type: "bigint", nullable: true),
                    CustodianteAtualId = table.Column<long>(type: "bigint", nullable: true),
                    LocalColeta = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DataHoraColeta = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    MetodoColeta = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HouveIntercorrencia = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    DescricaoIntercorrencia = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EtapaAtual = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    FaseAtual = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Estado = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VESTIGIO", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VESTIGIO_INTERVENIENTE_CriadorId",
                        column: x => x.CriadorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VESTIGIO_INTERVENIENTE_CustodianteAtualId",
                        column: x => x.CustodianteAtualId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VESTIGIO_PROCESSO_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "PROCESSO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VESTIGIO_TIPO_VESTIGIO_TipoVestigioId",
                        column: x => x.TipoVestigioId,
                        principalTable: "TIPO_VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_INTERVENIENTE_Did",
                table: "INTERVENIENTE",
                column: "Did",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_INTERVENIENTE_PerfilId",
                table: "INTERVENIENTE",
                column: "PerfilId");

            migrationBuilder.CreateIndex(
                name: "IX_PERFIL_Codigo",
                table: "PERFIL",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSO_Numero",
                table: "PROCESSO",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VESTIGIO_CriadorId",
                table: "VESTIGIO",
                column: "CriadorId");

            migrationBuilder.CreateIndex(
                name: "IX_VESTIGIO_CustodianteAtualId",
                table: "VESTIGIO",
                column: "CustodianteAtualId");

            migrationBuilder.CreateIndex(
                name: "IX_VESTIGIO_ProcessoId",
                table: "VESTIGIO",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_VESTIGIO_RotuloConjunto",
                table: "VESTIGIO",
                column: "RotuloConjunto");

            migrationBuilder.CreateIndex(
                name: "IX_VESTIGIO_RotuloEvidencia",
                table: "VESTIGIO",
                column: "RotuloEvidencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VESTIGIO_TipoVestigioId",
                table: "VESTIGIO",
                column: "TipoVestigioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VESTIGIO");

            migrationBuilder.DropTable(
                name: "INTERVENIENTE");

            migrationBuilder.DropTable(
                name: "PROCESSO");

            migrationBuilder.DropTable(
                name: "TIPO_VESTIGIO");

            migrationBuilder.DropTable(
                name: "PERFIL");
        }
    }
}
