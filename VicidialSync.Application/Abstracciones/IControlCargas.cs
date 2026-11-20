using VicidialSync.Domain.Cargas;

namespace VicidialSync.Application.Abstracciones
{
    public interface IControlCargas
    {
        Task<EstadoControl> LeerAsync(TablaVicidial tabla, CancellationToken ct);

        Task GuardarAsync(TablaVicidial tabla, DateTime? fechaHastaCargada, bool cargaInicialCompleta, bool corridaOk, CancellationToken ct);

        Task<long> AbrirBitacoraAsync(Guid idCorrida, TablaVicidial tabla, string tipoCarga, DateTime? desde, DateTime? hasta, CancellationToken ct);

        Task CerrarBitacoraAsync(long idBitacora, string estado, ResultadoBloque totales, DateTime? hasta, string? mensaje, CancellationToken ct);
    }
}
