using System.Security.Claims;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using CustodyChain.Web.Services.Ledger;
using System.Security.Cryptography;
using System.Text.Json;

namespace CustodyChain.Web.Controllers;

public class AutenticacaoController(
    CriarDesafioAutenticacaoUseCase criarDesafio,
    ConcluirAutenticacaoUseCase concluirAutenticacao,
    IDidRegistry didRegistry,
    IServicoLedger ledger,
    IConfiguration configuration,
    ILogger<AutenticacaoController> logger) : Controller
{
    [HttpGet("/entrar")]
    [AllowAnonymous]
    public IActionResult Entrar()
    {
        return View(new LoginViewModel
        {
            WalletEndpoint = configuration["AuthenticationDid:WalletEndpoint"]
                ?? "http://127.0.0.1:43123"
        });
    }

    [HttpPost("/autenticacao/desafios")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AutenticacaoDid")]
    public async Task<IActionResult> CriarDesafio(
        [FromBody] CriarDesafioLoginRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var desafio = await criarDesafio.ExecutarAsync(request?.Did ?? string.Empty, cancellationToken);
            return Ok(desafio);
        }
        catch (AutenticacaoException erro)
        {
            return Unauthorized(new { code = erro.Codigo, message = erro.Message });
        }
        catch (Exception erro)
        {
            logger.LogError(erro, "Falha ao criar desafio de autenticação DID.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "servico_did_indisponivel",
                message = "Não foi possível consultar o registro DID. Tente novamente."
            });
        }
    }

    [HttpPost("/autenticacao/provas")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AutenticacaoDid")]
    public async Task<IActionResult> Concluir(
        [FromBody] ConcluirLoginRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var identidade = await concluirAutenticacao.ExecutarAsync(
                new ProvaAutenticacao(
                    request?.ChallengeId ?? string.Empty,
                    request?.Did ?? string.Empty,
                    request?.KeyId ?? string.Empty,
                    request?.Signature ?? string.Empty),
                cancellationToken);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, identidade.Id.ToString()),
                new(ClaimTypes.Name, identidade.Nome),
                new("did", identidade.Did),
                new(ClaimTypes.Role, identidade.PerfilCodigo),
                new("perfil_nome", identidade.PerfilNome),
                new("did_key_id", identidade.KeyId ?? string.Empty),
                new("did_document_version", identidade.DocumentVersion.ToString()),
            };

            var identidadeCookie = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identidadeCookie));

            return Ok(new { redirectUrl = Url.Action("Index", "Home") ?? "/" });
        }
        catch (AutenticacaoException erro)
        {
            return Unauthorized(new { code = erro.Codigo, message = erro.Message });
        }
        catch (Exception erro)
        {
            logger.LogError(erro, "Falha ao concluir autenticação DID.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "servico_did_indisponivel",
                message = "Não foi possível validar a prova DID. Tente novamente."
            });
        }
    }

    [HttpGet("/identidade/chave")]
    [Authorize]
    public async Task<IActionResult> RotacionarChave(CancellationToken cancellationToken)
    {
        var did = User.FindFirstValue("did");
        var documento = string.IsNullOrWhiteSpace(did)
            ? null
            : await didRegistry.ResolverAsync(did, cancellationToken);
        var chaveAtual = documento?.Authentication.FirstOrDefault();
        if (documento is null || string.IsNullOrWhiteSpace(chaveAtual))
        {
            return RedirectToAction(nameof(Entrar));
        }

        return View(new RotacionarChaveDidViewModel(
            did!,
            chaveAtual,
            documento.DocumentVersion,
            configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"));
    }

    [HttpPost("/identidade/chave/rotacao/comando")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarComandoRotacao(
        [FromBody] CriarComandoRotacaoDidRequest? request,
        CancellationToken cancellationToken)
    {
        var did = User.FindFirstValue("did");
        var documento = string.IsNullOrWhiteSpace(did)
            ? null
            : await didRegistry.ResolverAsync(did, cancellationToken);
        var chaveAtual = documento?.CapabilityInvocation?.FirstOrDefault()
            ?? documento?.Authentication.FirstOrDefault();
        if (request is null || documento is null || string.IsNullOrWhiteSpace(chaveAtual)
            || !ChaveCandidataValida(did!, request.KeyId, request.PublicKeyMultibase))
        {
            return BadRequest(new { message = "Não foi possível preparar a rotação desta identidade." });
        }

        var agora = DateTime.UtcNow;
        return Ok(new
        {
            type = "CustodyChainDidKeyRotation",
            version = 1,
            commandId = $"urn:uuid:{Guid.NewGuid()}",
            subjectDid = did,
            expectedDocumentVersion = documento.DocumentVersion,
            currentKeyId = chaveAtual,
            newVerificationMethod = new
            {
                id = request.KeyId,
                type = "Multikey",
                controller = did,
                publicKeyMultibase = request.PublicKeyMultibase
            },
            algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1",
            audience = "custodychain-ledger",
            issuedAt = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O"),
            nonce = Base64Url(RandomNumberGenerator.GetBytes(24))
        });
    }

    [HttpPost("/identidade/chave/rotacao/prova")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarRotacao(
        [FromBody] EnviarProvaRotacaoDidRequest? request,
        CancellationToken cancellationToken)
    {
        var did = User.FindFirstValue("did");
        if (request is null
            || !ComandoRotacaoCorresponde(request.Command, did, request.CurrentKeyProof, request.NewKeyProof))
        {
            return BadRequest(new { message = "A prova de rotação não corresponde à sessão autenticada." });
        }

        var documento = await ledger.RotacionarChaveDidV2Async(
            did!,
            new RotacaoChaveDidV2Dto(
                request.Command,
                new ProvaChaveDidLedgerDto(
                    request.CurrentKeyProof.KeyId,
                    request.CurrentKeyProof.Algorithm,
                    request.CurrentKeyProof.Signature),
                new ProvaChaveDidLedgerDto(
                    request.NewKeyProof.KeyId,
                    request.NewKeyProof.Algorithm,
                    request.NewKeyProof.Signature)),
            cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new
        {
            keyId = documento.KeyId,
            documentVersion = documento.DocumentVersion,
            redirectUrl = Url.Action(nameof(Entrar)) ?? "/entrar"
        });
    }

    [HttpPost("/identidade/chave/recuperacao/prova")]
    [Authorize(Roles = "ADMIN")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarRecuperacao(
        [FromBody] EnviarProvaRecuperacaoDidRequest? request,
        CancellationToken cancellationToken)
    {
        var adminDid = User.FindFirstValue("did");
        if (request is null || request.Command.ValueKind != JsonValueKind.Object
            || !request.Command.TryGetProperty("actorDid", out var actor)
            || actor.GetString() != adminDid
            || !request.Command.TryGetProperty("subjectDid", out var subject)
            || string.IsNullOrWhiteSpace(subject.GetString()))
        {
            return BadRequest(new { message = "A recuperação não corresponde à identidade administrativa autenticada." });
        }

        var documento = await ledger.RecuperarChaveDidV2Async(subject.GetString()!, new RecuperacaoChaveDidV2Dto(
            request.Command, request.AdminKeyId, request.AdminSignature, request.CandidateSignature), cancellationToken);
        return Ok(new { keyId = documento.KeyId, documentVersion = documento.DocumentVersion });
    }

    [HttpPost("/sair")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Entrar));
    }

    private static bool ChaveCandidataValida(string did, string? keyId, string? publicKeyMultibase) =>
        !string.IsNullOrWhiteSpace(keyId)
        && !string.IsNullOrWhiteSpace(publicKeyMultibase)
        && keyId.StartsWith($"{did}#", StringComparison.Ordinal)
        && publicKeyMultibase.Length is >= 40 and <= 64
        && publicKeyMultibase[0] == 'z';

    private static bool ComandoRotacaoCorresponde(
        JsonElement command,
        string? did,
        ProvaChaveDidDto? currentProof,
        ProvaChaveDidDto? newProof) =>
        !string.IsNullOrWhiteSpace(did)
        && currentProof is not null && newProof is not null
        && command.ValueKind == JsonValueKind.Object
        && command.TryGetProperty("type", out var type) && type.GetString() == "CustodyChainDidKeyRotation"
        && command.TryGetProperty("subjectDid", out var subject) && subject.GetString() == did
        && command.TryGetProperty("currentKeyId", out var currentKey) && currentKey.GetString() == currentProof.KeyId
        && command.TryGetProperty("newVerificationMethod", out var method)
        && method.TryGetProperty("id", out var newKey) && newKey.GetString() == newProof.KeyId
        && currentProof.Algorithm == "Ed25519" && newProof.Algorithm == "Ed25519";

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

}
