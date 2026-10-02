using System.Security.Claims;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CustodyChain.Web.Controllers;

public class AutenticacaoController(
    CriarDesafioAutenticacaoUseCase criarDesafio,
    ConcluirAutenticacaoUseCase concluirAutenticacao,
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

    [HttpPost("/sair")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Entrar));
    }

}
