using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.VerifiableCredentials;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

// RN07: somente administradores cadastram intervenientes e emitem
// credencial de permissão.
[Authorize(Roles = "ADMIN")]
public class GestaoPerfisController(
    CustodyChainDbContext db,
    IServicoLedger ledger,
    IConfiguration configuration,
    CriarVcPermissao criarVcPermissao,
    IEmissaoVcPendenteStore emissoesPendentes) : Controller
{
    private static readonly Dictionary<string, TipoAtor> MapaPerfilParaAtor = new()
    {
        ["ADMIN"] = TipoAtor.Admin,
        ["CUSTODIA"] = TipoAtor.Custodian,
        ["COLETOR"] = TipoAtor.Delegate,
        ["PERITO"] = TipoAtor.Expert,
        ["EXTERNO"] = TipoAtor.Judge,
    };

    [HttpGet("/gestao-perfis")]
    public async Task<IActionResult> Index()
    {
        var intervenientes = await db.Intervenientes
            .Include(i => i.Perfil)
            .OrderByDescending(i => i.CriadoEm)
            .Select(i => new ItemIntervenienteViewModel(
                i.Id, i.Did, i.Nome, i.Perfil.Nome, i.Situacao.ToString(), i.CriadoEm, i.AtivadoEm))
            .ToListAsync();

        return View(new GestaoPerfisListaViewModel { Intervenientes = intervenientes });
    }

    [HttpGet("/gestao-perfis/cadastrar")]
    public async Task<IActionResult> Cadastrar()
    {
        var modelo = new CadastrarIntervenienteViewModel();
        await CarregarPerfisAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/gestao-perfis/cadastrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cadastrar(CadastrarIntervenienteViewModel modelo)
    {
        var perfil = modelo.PerfilId is not null
            ? await db.Perfis.FirstOrDefaultAsync(p => p.Id == modelo.PerfilId)
            : null;

        if (modelo.PerfilId is not null && perfil is null)
        {
            ModelState.AddModelError(nameof(modelo.PerfilId), "Perfil não encontrado.");
        }

        if (!ModelState.IsValid || perfil is null)
        {
            await CarregarPerfisAsync(modelo);
            return View(modelo);
        }

        var tipoAtor = MapaPerfilParaAtor[perfil.Codigo];
        var metodoDid = $"did:legal:{tipoAtor.ToString().ToLowerInvariant()}";
        var did = $"{metodoDid}:{Guid.NewGuid():N}";
        var agora = DateTime.UtcNow;
        var codigoInscricao = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var inscricao = new InscricaoDid
        {
            EnrollmentId = $"urn:uuid:{Guid.NewGuid()}",
            CodigoHash = CalcularHash(codigoInscricao),
            Situacao = SituacaoInscricaoDid.PENDENTE,
            CriadaEm = agora,
            ExpiraEm = agora.AddMinutes(15)
        };

        var interveniente = new Interveniente
        {
            Did = did,
            PerfilId = perfil.Id,
            Nome = modelo.Nome!.Trim(),
            Matricula = modelo.Matricula,
            Orgao = modelo.Orgao,
            Lotacao = modelo.Lotacao,
            Situacao = SituacaoInterveniente.GERADO,
            CriadoEm = agora,
        };
        db.Intervenientes.Add(interveniente);
        inscricao.Interveniente = interveniente;
        db.InscricoesDid.Add(inscricao);

        await db.SaveChangesAsync();

        return View("InscricaoCriada", new InscricaoDidCriadaViewModel(
            did, inscricao.EnrollmentId, codigoInscricao, inscricao.ExpiraEm));
    }

    [HttpPost("/gestao-perfis/inscricoes/{enrollmentId}/comando-registro")]
    [AllowAnonymous]
    public async Task<IActionResult> CriarComandoRegistro(
        string enrollmentId,
        [FromBody] SolicitarComandoRegistroDidRequest? request)
    {
        var inscricao = await ObterInscricaoPendenteAsync(enrollmentId, request?.CodigoInscricao);
        if (inscricao is null)
        {
            return Unauthorized(new { message = "Inscrição DID inválida ou expirada." });
        }

        var keyId = request?.VerificationMethodId?.Trim() ?? string.Empty;
        var publicKey = request?.PublicKeyMultibase?.Trim() ?? string.Empty;
        if (keyId != $"{inscricao.Interveniente.Did}#key-1" || string.IsNullOrWhiteSpace(publicKey))
        {
            return BadRequest(new { message = "A chave pública não corresponde ao DID reservado." });
        }

        return Ok(CriarComandoRegistro(inscricao, keyId, publicKey));
    }

    [HttpPost("/gestao-perfis/inscricoes/{enrollmentId}/prova-registro")]
    [AllowAnonymous]
    public async Task<IActionResult> RegistrarProva(
        string enrollmentId,
        [FromQuery] string codigo,
        [FromBody] EnviarProvaRegistroDidRequest? request,
        CancellationToken cancellationToken)
    {
        var inscricao = await ObterInscricaoPendenteAsync(enrollmentId, codigo);
        if (inscricao is null)
        {
            return Unauthorized(new { message = "Inscrição DID inválida ou expirada." });
        }

        if (!ComandoRegistroCorresponde(inscricao, request?.Command))
        {
            return BadRequest(new { message = "O comando de registro não corresponde à inscrição." });
        }

        try
        {
            await ledger.RegistrarDidV2PendenteAsync(
                new RegistroDidPendenteDto(request!.Command, request.Signature), cancellationToken);
            inscricao.Situacao = SituacaoInscricaoDid.REGISTRADA;
            inscricao.RegistradaEm = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(new { message = "Prova de posse registrada. Aguarde a ativação administrativa." });
        }
        catch (Exception error)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<GestaoPerfisController>>()
                .LogError(error, "Falha ao registrar o DID pendente {Did} no ledger.", inscricao.Interveniente.Did);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Não foi possível registrar o DID no ledger. Tente novamente."
            });
        }
    }

    [HttpGet("/gestao-perfis/{intervenienteId:long}/ativar")]
    public async Task<IActionResult> Ativar(long intervenienteId)
    {
        var emissorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var emissor = await db.Intervenientes.FindAsync(emissorId);

        var interveniente = await db.Intervenientes
            .FirstOrDefaultAsync(i => i.Id == intervenienteId && i.Situacao == SituacaoInterveniente.GERADO);

        var inscricaoRegistrada = await db.InscricoesDid.AnyAsync(i => i.IntervenienteId == intervenienteId
            && i.Situacao == SituacaoInscricaoDid.REGISTRADA);
        if (interveniente is null || emissor is null || !inscricaoRegistrada)
        {
            TempData["MensagemErro"] = "Interveniente não está pronto para ativação por prova de posse.";
            return RedirectToAction(nameof(Index));
        }

        return View("AtivarDidV2", new AtivarDidV2ViewModel(
            interveniente.Id, interveniente.Did, interveniente.Nome, emissor.Did,
            configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"));
    }

    [HttpPost("/gestao-perfis/{intervenienteId:long}/ativar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarComandoAtivacao(long intervenienteId, CancellationToken cancellationToken)
    {
        var emissorDid = User.FindFirstValue("did") ?? string.Empty;
        var interveniente = await db.Intervenientes.FirstOrDefaultAsync(
            i => i.Id == intervenienteId && i.Situacao == SituacaoInterveniente.GERADO,
            cancellationToken);
        if (interveniente is null || string.IsNullOrWhiteSpace(emissorDid))
        {
            return BadRequest(new { message = "Interveniente não está disponível para ativação." });
        }

        return Ok(CriarComandoAtivacao(interveniente.Did, emissorDid));
    }

    [HttpPost("/gestao-perfis/{intervenienteId:long}/ativar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtivarComProva(
        long intervenienteId,
        [FromBody] EnviarProvaAtivacaoDidRequest? request,
        CancellationToken cancellationToken)
    {
        var emissorDid = User.FindFirstValue("did") ?? string.Empty;
        var interveniente = await db.Intervenientes
            .FirstOrDefaultAsync(i => i.Id == intervenienteId && i.Situacao == SituacaoInterveniente.GERADO, cancellationToken);
        if (interveniente is null || !ComandoAtivacaoCorresponde(request?.Command, interveniente.Did, emissorDid))
        {
            return BadRequest(new { message = "O comando de ativação não corresponde à identidade pendente." });
        }

        await ledger.AtivarDidV2Async(interveniente.Did,
            new AtivacaoDidV2Dto(request!.Command, request.KeyId, request.Signature), cancellationToken);

        interveniente.Situacao = SituacaoInterveniente.ATIVO;
        interveniente.DidEmissor = emissorDid;
        interveniente.AtivadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return Ok(new { redirectUrl = Url.Action(nameof(Index)), message = $"{interveniente.Nome} ativado." });
    }

    [HttpPost("/gestao-perfis/revogar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revogar(long intervenienteId)
    {
        var interveniente = await db.Intervenientes
            .FirstOrDefaultAsync(i => i.Id == intervenienteId && i.Situacao == SituacaoInterveniente.ATIVO);

        if (interveniente is null)
        {
            TempData["MensagemErro"] = "Interveniente não encontrado ou não está ativo.";
            return RedirectToAction(nameof(Index));
        }

        interveniente.Situacao = SituacaoInterveniente.REVOGADO;
        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"{interveniente.Nome} revogado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/gestao-perfis/emitir-credencial")]
    public async Task<IActionResult> EmitirCredencial()
    {
        var modelo = new EmitirCredencialPermissaoViewModel();
        await CarregarOpcoesCredencialAsync(modelo);
        return View(modelo);
    }

    [HttpGet("/gestao-perfis/credenciais")]
    public async Task<IActionResult> Credenciais()
    {
        var credenciais = await db.Credenciais
            .Include(c => c.Titular)
            .Where(c => c.Tipo == TipoCredencial.PERMISSAO)
            .OrderByDescending(c => c.EmitidaEm)
            .Select(c => new ItemCredencialPermissaoViewModel(
                c.Id, c.Identificador, c.Titular.Nome, c.Situacao.ToString(), c.EmitidaEm, c.ValidaAte))
            .ToListAsync();
        return View(new GestaoCredenciaisViewModel { Credenciais = credenciais });
    }

    [HttpGet("/gestao-perfis/credenciais/{credencialId:long}/revogar")]
    public async Task<IActionResult> RevogarCredencial(long credencialId, CancellationToken cancellationToken)
    {
        var emissorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var credencial = await db.Credenciais.Include(c => c.Titular).Include(c => c.Emissor)
            .FirstOrDefaultAsync(c => c.Id == credencialId && c.Tipo == TipoCredencial.PERMISSAO
                && c.Situacao == SituacaoCredencial.VIGENTE && c.EmissorId == emissorId, cancellationToken);
        if (credencial is null)
        {
            TempData["MensagemErro"] = "Credencial não disponível para revogação pelo emissor atual.";
            return RedirectToAction(nameof(Credenciais));
        }
        return View(new RevogarCredencialViewModel(credencial.Id, credencial.Identificador,
            credencial.Titular.Nome, credencial.Emissor.Did,
            configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"));
    }

    [HttpPost("/gestao-perfis/credenciais/{credencialId:long}/revogar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarComandoRevogacao(long credencialId, CancellationToken cancellationToken)
    {
        var emissorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var credencial = await db.Credenciais.Include(c => c.Emissor)
            .FirstOrDefaultAsync(c => c.Id == credencialId && c.Tipo == TipoCredencial.PERMISSAO
                && c.Situacao == SituacaoCredencial.VIGENTE && c.EmissorId == emissorId, cancellationToken);
        if (credencial is null)
        {
            return BadRequest(new { message = "Credencial não disponível para revogação." });
        }
        var agora = DateTime.UtcNow;
        return Ok(new
        {
            type = "CustodyChainCredentialRevocation", version = 1,
            commandId = $"urn:uuid:{Guid.NewGuid()}", credentialId = credencial.Identificador,
            issuerDid = credencial.Emissor.Did, audience = "custodychain-ledger",
            issuedAt = agora.ToString("O"), expiresAt = agora.AddMinutes(5).ToString("O")
        });
    }

    [HttpPost("/gestao-perfis/credenciais/{credencialId:long}/revogar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevogarCredencialComProva(long credencialId,
        [FromBody] EnviarProvaRevogacaoVcRequest? request, CancellationToken cancellationToken)
    {
        var emissorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var credencial = await db.Credenciais.Include(c => c.Emissor)
            .FirstOrDefaultAsync(c => c.Id == credencialId && c.Tipo == TipoCredencial.PERMISSAO
                && c.Situacao == SituacaoCredencial.VIGENTE && c.EmissorId == emissorId, cancellationToken);
        if (credencial is null || !ComandoRevogacaoCorresponde(request?.Command, credencial.Identificador, credencial.Emissor.Did))
        {
            return BadRequest(new { message = "A prova não corresponde à credencial de permissão." });
        }
        await ledger.RevogarCredencialV2Async(credencial.Identificador,
            new RevogacaoCredencialV2Dto(request!.Command, request.KeyId, request.Signature), cancellationToken);
        credencial.Situacao = SituacaoCredencial.REVOGADA;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { redirectUrl = Url.Action(nameof(Credenciais)), message = "VC revogada." });
    }

    [HttpPost("/gestao-perfis/emitir-credencial/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarComandoEmissaoCredencial(
        EmitirCredencialPermissaoViewModel modelo,
        CancellationToken cancellationToken)
    {
        var emissorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var titular = modelo.TitularId is not null
            ? await db.Intervenientes.Include(i => i.Perfil)
                .FirstOrDefaultAsync(i => i.Id == modelo.TitularId && i.Situacao == SituacaoInterveniente.ATIVO)
            : null;

        if (modelo.TitularId is not null && titular is null)
        {
            ModelState.AddModelError(nameof(modelo.TitularId), "Titular não encontrado ou não está ativo.");
        }

        if (!ModelState.IsValid || titular is null)
        {
            return ValidationProblem(ModelState);
        }

        var emissor = await db.Intervenientes.FindAsync([emissorId], cancellationToken);
        if (emissor is null || emissor.Situacao != SituacaoInterveniente.ATIVO)
        {
            return BadRequest(new { message = "Emissor não está ativo para assinar a VC." });
        }

        VcPermissaoSemAssinatura vc;
        try
        {
            vc = criarVcPermissao.Executar(new CriarVcPermissaoInput(
                emissor.Did, titular.Did, titular.Perfil.Codigo, modelo.ProcessoId, modelo.ValidaAte));
        }
        catch (ArgumentException error)
        {
            return BadRequest(new { message = error.Message });
        }

        var emissaoId = Guid.NewGuid().ToString("N");
        emissoesPendentes.Armazenar(new EmissaoVcPendente(
            emissaoId, emissorId, titular.Id, modelo.ProcessoId, vc, DateTime.UtcNow.AddMinutes(2)));
        return Ok(new
        {
            emissaoId,
            credential = vc.Credencial,
            issuerDid = emissor.Did,
            walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
        });
    }

    [HttpPost("/gestao-perfis/emitir-credencial/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmitirCredencialComProva(
        [FromBody] EnviarProvaVcPermissaoRequest? request,
        CancellationToken cancellationToken)
    {
        var emissorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (request is null || string.IsNullOrWhiteSpace(request.EmissaoId)
            || !emissoesPendentes.TentarObter(request.EmissaoId, out var emissao)
            || emissao is null || emissao.EmissorId != emissorId
            || !CredencialCorresponde(emissao.Credencial.Credencial, request.Credential))
        {
            return BadRequest(new { message = "A VC não corresponde a uma emissão pendente válida." });
        }

        string credencialId;
        try
        {
            credencialId = await ledger.EmitirCredencialPermissaoV2Async(
                new CredencialPermissaoV2Dto(request.Credential), cancellationToken);
        }
        catch (Exception error)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<GestaoPerfisController>>()
                .LogError(error, "Falha ao registrar VC de permissão pendente {EmissaoId} no ledger.", request.EmissaoId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Não foi possível registrar a VC no ledger. Tente novamente com a mesma prova."
            });
        }

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.PERMISSAO,
            Identificador = credencialId,
            TitularId = emissao.TitularId,
            EmissorId = emissorId,
            ProcessoId = emissao.ProcessoId,
            EmitidaEm = emissao.Credencial.EmitidaEm,
            ValidaAte = emissao.Credencial.ExpiraEm,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        await db.SaveChangesAsync(cancellationToken);
        emissoesPendentes.Remover(request.EmissaoId);
        return Ok(new { redirectUrl = Url.Action(nameof(EmitirCredencial)), message = "VC de permissão emitida." });
    }

    private async Task CarregarPerfisAsync(CadastrarIntervenienteViewModel modelo)
    {
        modelo.PerfisDisponiveis = await db.Perfis
            .OrderBy(p => p.Nome)
            .Select(p => new OpcaoPerfilViewModel(p.Id, p.Codigo, p.Nome))
            .ToListAsync();
    }

    private async Task<InscricaoDid?> ObterInscricaoPendenteAsync(string enrollmentId, string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            return null;
        }

        var inscricao = await db.InscricoesDid
            .Include(i => i.Interveniente)
            .FirstOrDefaultAsync(i => i.EnrollmentId == enrollmentId && i.Situacao == SituacaoInscricaoDid.PENDENTE);
        if (inscricao is null || inscricao.ExpiraEm <= DateTime.UtcNow)
        {
            if (inscricao is not null)
            {
                inscricao.Situacao = SituacaoInscricaoDid.EXPIRADA;
                await db.SaveChangesAsync();
            }
            return null;
        }

        return CriptographicamenteIgual(inscricao.CodigoHash, CalcularHash(codigo)) ? inscricao : null;
    }

    private static object CriarComandoRegistro(InscricaoDid inscricao, string keyId, string publicKeyMultibase)
    {
        var agora = DateTime.UtcNow;
        return new
        {
            type = "CustodyChainDidRegistration",
            version = 1,
            commandId = $"urn:uuid:{Guid.NewGuid()}",
            enrollmentId = inscricao.EnrollmentId,
            did = inscricao.Interveniente.Did,
            metodoDid = string.Join(':', inscricao.Interveniente.Did.Split(':').Take(3)),
            verificationMethodId = keyId,
            publicKeyMultibase,
            audience = "custodychain-ledger",
            issuedAt = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O")
        };
    }

    private static object CriarComandoAtivacao(string subjectDid, string actorDid)
    {
        var agora = DateTime.UtcNow;
        return new
        {
            type = "CustodyChainDidActivation",
            version = 1,
            commandId = $"urn:uuid:{Guid.NewGuid()}",
            actorDid,
            subjectDid,
            expectedDocumentVersion = 1,
            audience = "custodychain-ledger",
            issuedAt = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O")
        };
    }

    private static bool ComandoRegistroCorresponde(InscricaoDid inscricao, object? comando)
    {
        var json = JsonSerializer.SerializeToElement(comando);
        return json.TryGetProperty("type", out var type) && type.GetString() == "CustodyChainDidRegistration"
            && json.TryGetProperty("did", out var did) && did.GetString() == inscricao.Interveniente.Did
            && json.TryGetProperty("enrollmentId", out var enrollment) && enrollment.GetString() == inscricao.EnrollmentId
            && json.TryGetProperty("verificationMethodId", out var keyId) && keyId.GetString() == $"{inscricao.Interveniente.Did}#key-1"
            && json.TryGetProperty("publicKeyMultibase", out var publicKey) && !string.IsNullOrWhiteSpace(publicKey.GetString());
    }

    private static bool ComandoAtivacaoCorresponde(object? comando, string subjectDid, string actorDid)
    {
        var json = JsonSerializer.SerializeToElement(comando);
        return json.TryGetProperty("type", out var type) && type.GetString() == "CustodyChainDidActivation"
            && json.TryGetProperty("subjectDid", out var subject) && subject.GetString() == subjectDid
            && json.TryGetProperty("actorDid", out var actor) && actor.GetString() == actorDid
            && json.TryGetProperty("expectedDocumentVersion", out var version) && version.GetInt32() == 1;
    }

    private static bool CredencialCorresponde(JsonElement semAssinatura, JsonElement assinada)
    {
        if (assinada.ValueKind != JsonValueKind.Object || !assinada.TryGetProperty("proof", out _))
        {
            return false;
        }

        var recebida = JsonNode.Parse(assinada.GetRawText())?.AsObject();
        recebida?.Remove("proof");
        var esperada = JsonNode.Parse(semAssinatura.GetRawText());
        return JsonNode.DeepEquals(esperada, recebida);
    }

    private static bool ComandoRevogacaoCorresponde(object? comando, string credentialId, string issuerDid)
    {
        var json = JsonSerializer.SerializeToElement(comando);
        return json.TryGetProperty("type", out var type) && type.GetString() == "CustodyChainCredentialRevocation"
            && json.TryGetProperty("credentialId", out var credential) && credential.GetString() == credentialId
            && json.TryGetProperty("issuerDid", out var issuer) && issuer.GetString() == issuerDid;
    }

    private static string CalcularHash(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));

    private static bool CriptographicamenteIgual(string esquerdo, string direito) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(esquerdo), Encoding.UTF8.GetBytes(direito));

    private async Task CarregarOpcoesCredencialAsync(EmitirCredencialPermissaoViewModel modelo)
    {
        modelo.TitularesDisponiveis = await db.Intervenientes
            .Include(i => i.Perfil)
            .Where(i => i.Situacao == SituacaoInterveniente.ATIVO)
            .OrderBy(i => i.Nome)
            .Select(i => new ItemIntervenienteViewModel(
                i.Id, i.Did, i.Nome, i.Perfil.Nome, i.Situacao.ToString(), i.CriadoEm, i.AtivadoEm))
            .ToListAsync();

        modelo.ProcessosDisponiveis = await db.Processos
            .OrderBy(p => p.Numero)
            .Select(p => new OpcaoProcessoViewModel(p.Id, p.Numero, p.NomeOperacao))
            .ToListAsync();
    }
}
