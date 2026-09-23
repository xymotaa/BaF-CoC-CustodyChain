namespace CustodyChain.Web.Models.ViewModels;

public record ContagemEstadoViewModel(string Estado, int Quantidade);

public class DashboardViewModel
{
    public int TotalVestigios { get; set; }
    public int TotalProcessosAtivos { get; set; }
    public int RegistrosLedgerPendentes { get; set; }
    public IReadOnlyList<ContagemEstadoViewModel> ContagemPorEstado { get; set; } = [];
}
