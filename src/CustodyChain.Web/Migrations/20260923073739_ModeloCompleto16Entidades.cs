using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations
{
    /// <inheritdoc />
    public partial class ModeloCompleto16Entidades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ANEXO",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VestigioId = table.Column<long>(type: "bigint", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NomeArquivo = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CaminhoRelativo = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: true),
                    HashSha256 = table.Column<string>(type: "char(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Algoritmo = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EnviadoPorId = table.Column<long>(type: "bigint", nullable: true),
                    EnviadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ANEXO", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ANEXO_INTERVENIENTE_EnviadoPorId",
                        column: x => x.EnviadoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ANEXO_VESTIGIO_VestigioId",
                        column: x => x.VestigioId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ARMAZENAMENTO",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VestigioId = table.Column<long>(type: "bigint", nullable: false),
                    Central = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Posicao = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntradaEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SaidaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PrazoGuardaAte = table.Column<DateOnly>(type: "date", nullable: true),
                    RecebidoPorId = table.Column<long>(type: "bigint", nullable: true),
                    Situacao = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ARMAZENAMENTO", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ARMAZENAMENTO_INTERVENIENTE_RecebidoPorId",
                        column: x => x.RecebidoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ARMAZENAMENTO_VESTIGIO_VestigioId",
                        column: x => x.VestigioId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CREDENCIAL",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Tipo = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Identificador = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TitularId = table.Column<long>(type: "bigint", nullable: false),
                    EmissorId = table.Column<long>(type: "bigint", nullable: false),
                    ProcessoId = table.Column<long>(type: "bigint", nullable: true),
                    VestigioId = table.Column<long>(type: "bigint", nullable: true),
                    EmitidaEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ValidaAte = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Situacao = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CREDENCIAL", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CREDENCIAL_INTERVENIENTE_EmissorId",
                        column: x => x.EmissorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CREDENCIAL_INTERVENIENTE_TitularId",
                        column: x => x.TitularId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CREDENCIAL_PROCESSO_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "PROCESSO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CREDENCIAL_VESTIGIO_VestigioId",
                        column: x => x.VestigioId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LOG_AUDITORIA",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    IntervenienteId = table.Column<long>(type: "bigint", nullable: true),
                    Acao = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Entidade = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegistroId = table.Column<long>(type: "bigint", nullable: true),
                    DataHora = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Ip = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserAgent = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HashAnterior = table.Column<string>(type: "char(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HashRegistro = table.Column<string>(type: "char(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LOG_AUDITORIA", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LOG_AUDITORIA_INTERVENIENTE_IntervenienteId",
                        column: x => x.IntervenienteId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MOVIMENTACAO",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VestigioId = table.Column<long>(type: "bigint", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Etapa = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    OrigemId = table.Column<long>(type: "bigint", nullable: true),
                    DestinoId = table.Column<long>(type: "bigint", nullable: true),
                    DataHoraSaida = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DataHoraChegada = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CondicoesAdequadas = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    HouveIntercorrencia = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    DescricaoIntercorrencia = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CodigoRastreamento = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Situacao = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MotivoRecusa = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoPorId = table.Column<long>(type: "bigint", nullable: false),
                    AprovadoPorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MOVIMENTACAO", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MOVIMENTACAO_INTERVENIENTE_AprovadoPorId",
                        column: x => x.AprovadoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MOVIMENTACAO_INTERVENIENTE_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MOVIMENTACAO_INTERVENIENTE_DestinoId",
                        column: x => x.DestinoId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MOVIMENTACAO_INTERVENIENTE_OrigemId",
                        column: x => x.OrigemId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MOVIMENTACAO_VESTIGIO_VestigioId",
                        column: x => x.VestigioId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "REGISTRO_LEDGER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EntidadeOrigem = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegistroOrigemId = table.Column<long>(type: "bigint", nullable: false),
                    VestigioId = table.Column<long>(type: "bigint", nullable: true),
                    Evento = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayloadJson = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Estado = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tentativas = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    TxHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Bloco = table.Column<long>(type: "bigint", nullable: true),
                    Erro = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AncoradoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REGISTRO_LEDGER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REGISTRO_LEDGER_VESTIGIO_VestigioId",
                        column: x => x.VestigioId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DESCARTE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VestigioId = table.Column<long>(type: "bigint", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AutorizacaoAnexoId = table.Column<long>(type: "bigint", nullable: false),
                    DidMagistrado = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SolicitadoPorId = table.Column<long>(type: "bigint", nullable: true),
                    AprovadoPorId = table.Column<long>(type: "bigint", nullable: true),
                    ExecutadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Observacao = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DESCARTE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DESCARTE_ANEXO_AutorizacaoAnexoId",
                        column: x => x.AutorizacaoAnexoId,
                        principalTable: "ANEXO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DESCARTE_INTERVENIENTE_AprovadoPorId",
                        column: x => x.AprovadoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DESCARTE_INTERVENIENTE_SolicitadoPorId",
                        column: x => x.SolicitadoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DESCARTE_VESTIGIO_VestigioId",
                        column: x => x.VestigioId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LACRE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VestigioId = table.Column<long>(type: "bigint", nullable: false),
                    Numero = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LacreAnteriorId = table.Column<long>(type: "bigint", nullable: true),
                    Situacao = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AplicadoPorId = table.Column<long>(type: "bigint", nullable: true),
                    AplicadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RompidoPorId = table.Column<long>(type: "bigint", nullable: true),
                    RompidoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    JustificativaRompimento = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FotoAnexoId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LACRE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LACRE_ANEXO_FotoAnexoId",
                        column: x => x.FotoAnexoId,
                        principalTable: "ANEXO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LACRE_INTERVENIENTE_AplicadoPorId",
                        column: x => x.AplicadoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LACRE_INTERVENIENTE_RompidoPorId",
                        column: x => x.RompidoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LACRE_LACRE_LacreAnteriorId",
                        column: x => x.LacreAnteriorId,
                        principalTable: "LACRE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LACRE_VESTIGIO_VestigioId",
                        column: x => x.VestigioId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PERICIA",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VestigioId = table.Column<long>(type: "bigint", nullable: false),
                    ProcessoId = table.Column<long>(type: "bigint", nullable: false),
                    PeritoId = table.Column<long>(type: "bigint", nullable: true),
                    CredencialId = table.Column<long>(type: "bigint", nullable: true),
                    AreaPericial = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Prioridade = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SolicitadaEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RecebidaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConcluidaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Situacao = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MotivoRecusa = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PERICIA", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PERICIA_CREDENCIAL_CredencialId",
                        column: x => x.CredencialId,
                        principalTable: "CREDENCIAL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PERICIA_INTERVENIENTE_PeritoId",
                        column: x => x.PeritoId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PERICIA_PROCESSO_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "PROCESSO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PERICIA_VESTIGIO_VestigioId",
                        column: x => x.VestigioId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LAUDO",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PericiaId = table.Column<long>(type: "bigint", nullable: false),
                    Numero = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Versao = table.Column<short>(type: "smallint", nullable: false),
                    LaudoAnteriorId = table.Column<long>(type: "bigint", nullable: true),
                    Conteudo = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HashVestigios = table.Column<string>(type: "char(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HashLaudo = table.Column<string>(type: "char(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssinaturaEd25519 = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssinadoPorId = table.Column<long>(type: "bigint", nullable: true),
                    AssinadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AnexoPdfId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LAUDO", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LAUDO_ANEXO_AnexoPdfId",
                        column: x => x.AnexoPdfId,
                        principalTable: "ANEXO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LAUDO_INTERVENIENTE_AssinadoPorId",
                        column: x => x.AssinadoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LAUDO_LAUDO_LaudoAnteriorId",
                        column: x => x.LaudoAnteriorId,
                        principalTable: "LAUDO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LAUDO_PERICIA_PericiaId",
                        column: x => x.PericiaId,
                        principalTable: "PERICIA",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OPERACAO_AMOSTRA",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PericiaId = table.Column<long>(type: "bigint", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VestigioOrigemId = table.Column<long>(type: "bigint", nullable: false),
                    VestigioResultanteId = table.Column<long>(type: "bigint", nullable: true),
                    QuantidadeDescrita = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Justificativa = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExecutadoPorId = table.Column<long>(type: "bigint", nullable: true),
                    ExecutadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OPERACAO_AMOSTRA", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OPERACAO_AMOSTRA_INTERVENIENTE_ExecutadoPorId",
                        column: x => x.ExecutadoPorId,
                        principalTable: "INTERVENIENTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OPERACAO_AMOSTRA_PERICIA_PericiaId",
                        column: x => x.PericiaId,
                        principalTable: "PERICIA",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OPERACAO_AMOSTRA_VESTIGIO_VestigioOrigemId",
                        column: x => x.VestigioOrigemId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OPERACAO_AMOSTRA_VESTIGIO_VestigioResultanteId",
                        column: x => x.VestigioResultanteId,
                        principalTable: "VESTIGIO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ANEXO_EnviadoPorId",
                table: "ANEXO",
                column: "EnviadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ANEXO_HashSha256",
                table: "ANEXO",
                column: "HashSha256");

            migrationBuilder.CreateIndex(
                name: "IX_ANEXO_VestigioId",
                table: "ANEXO",
                column: "VestigioId");

            migrationBuilder.CreateIndex(
                name: "IX_ARMAZENAMENTO_RecebidoPorId",
                table: "ARMAZENAMENTO",
                column: "RecebidoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ARMAZENAMENTO_VestigioId",
                table: "ARMAZENAMENTO",
                column: "VestigioId");

            migrationBuilder.CreateIndex(
                name: "IX_CREDENCIAL_EmissorId",
                table: "CREDENCIAL",
                column: "EmissorId");

            migrationBuilder.CreateIndex(
                name: "IX_CREDENCIAL_Identificador",
                table: "CREDENCIAL",
                column: "Identificador",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CREDENCIAL_ProcessoId",
                table: "CREDENCIAL",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_CREDENCIAL_TitularId",
                table: "CREDENCIAL",
                column: "TitularId");

            migrationBuilder.CreateIndex(
                name: "IX_CREDENCIAL_VestigioId",
                table: "CREDENCIAL",
                column: "VestigioId");

            migrationBuilder.CreateIndex(
                name: "IX_DESCARTE_AprovadoPorId",
                table: "DESCARTE",
                column: "AprovadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_DESCARTE_AutorizacaoAnexoId",
                table: "DESCARTE",
                column: "AutorizacaoAnexoId");

            migrationBuilder.CreateIndex(
                name: "IX_DESCARTE_SolicitadoPorId",
                table: "DESCARTE",
                column: "SolicitadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_DESCARTE_VestigioId",
                table: "DESCARTE",
                column: "VestigioId");

            migrationBuilder.CreateIndex(
                name: "IX_LACRE_AplicadoPorId",
                table: "LACRE",
                column: "AplicadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_LACRE_FotoAnexoId",
                table: "LACRE",
                column: "FotoAnexoId");

            migrationBuilder.CreateIndex(
                name: "IX_LACRE_LacreAnteriorId",
                table: "LACRE",
                column: "LacreAnteriorId");

            migrationBuilder.CreateIndex(
                name: "IX_LACRE_Numero",
                table: "LACRE",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LACRE_RompidoPorId",
                table: "LACRE",
                column: "RompidoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_LACRE_VestigioId",
                table: "LACRE",
                column: "VestigioId");

            migrationBuilder.CreateIndex(
                name: "IX_LAUDO_AnexoPdfId",
                table: "LAUDO",
                column: "AnexoPdfId");

            migrationBuilder.CreateIndex(
                name: "IX_LAUDO_AssinadoPorId",
                table: "LAUDO",
                column: "AssinadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_LAUDO_LaudoAnteriorId",
                table: "LAUDO",
                column: "LaudoAnteriorId");

            migrationBuilder.CreateIndex(
                name: "IX_LAUDO_Numero_Versao",
                table: "LAUDO",
                columns: new[] { "Numero", "Versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LAUDO_PericiaId",
                table: "LAUDO",
                column: "PericiaId");

            migrationBuilder.CreateIndex(
                name: "IX_LOG_AUDITORIA_HashRegistro",
                table: "LOG_AUDITORIA",
                column: "HashRegistro",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LOG_AUDITORIA_IntervenienteId",
                table: "LOG_AUDITORIA",
                column: "IntervenienteId");

            migrationBuilder.CreateIndex(
                name: "IX_MOVIMENTACAO_AprovadoPorId",
                table: "MOVIMENTACAO",
                column: "AprovadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_MOVIMENTACAO_CriadoPorId",
                table: "MOVIMENTACAO",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_MOVIMENTACAO_DestinoId",
                table: "MOVIMENTACAO",
                column: "DestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_MOVIMENTACAO_OrigemId",
                table: "MOVIMENTACAO",
                column: "OrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_MOVIMENTACAO_VestigioId",
                table: "MOVIMENTACAO",
                column: "VestigioId");

            migrationBuilder.CreateIndex(
                name: "IX_OPERACAO_AMOSTRA_ExecutadoPorId",
                table: "OPERACAO_AMOSTRA",
                column: "ExecutadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_OPERACAO_AMOSTRA_PericiaId",
                table: "OPERACAO_AMOSTRA",
                column: "PericiaId");

            migrationBuilder.CreateIndex(
                name: "IX_OPERACAO_AMOSTRA_VestigioOrigemId",
                table: "OPERACAO_AMOSTRA",
                column: "VestigioOrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_OPERACAO_AMOSTRA_VestigioResultanteId",
                table: "OPERACAO_AMOSTRA",
                column: "VestigioResultanteId");

            migrationBuilder.CreateIndex(
                name: "IX_PERICIA_CredencialId",
                table: "PERICIA",
                column: "CredencialId");

            migrationBuilder.CreateIndex(
                name: "IX_PERICIA_PeritoId",
                table: "PERICIA",
                column: "PeritoId");

            migrationBuilder.CreateIndex(
                name: "IX_PERICIA_ProcessoId",
                table: "PERICIA",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_PERICIA_VestigioId",
                table: "PERICIA",
                column: "VestigioId");

            migrationBuilder.CreateIndex(
                name: "IX_REGISTRO_LEDGER_EntidadeOrigem_RegistroOrigemId_Evento",
                table: "REGISTRO_LEDGER",
                columns: new[] { "EntidadeOrigem", "RegistroOrigemId", "Evento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REGISTRO_LEDGER_Estado_CriadoEm",
                table: "REGISTRO_LEDGER",
                columns: new[] { "Estado", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_REGISTRO_LEDGER_VestigioId",
                table: "REGISTRO_LEDGER",
                column: "VestigioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ARMAZENAMENTO");

            migrationBuilder.DropTable(
                name: "DESCARTE");

            migrationBuilder.DropTable(
                name: "LACRE");

            migrationBuilder.DropTable(
                name: "LAUDO");

            migrationBuilder.DropTable(
                name: "LOG_AUDITORIA");

            migrationBuilder.DropTable(
                name: "MOVIMENTACAO");

            migrationBuilder.DropTable(
                name: "OPERACAO_AMOSTRA");

            migrationBuilder.DropTable(
                name: "REGISTRO_LEDGER");

            migrationBuilder.DropTable(
                name: "ANEXO");

            migrationBuilder.DropTable(
                name: "PERICIA");

            migrationBuilder.DropTable(
                name: "CREDENCIAL");
        }
    }
}
