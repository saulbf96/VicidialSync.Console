using System.Data;
using VicidialSync.Domain.Cargas;

namespace VicidialSync.Application.Abstracciones
{
    public interface IOrigenVicidial
    {
        Task<DataTable> LeerAsync(TablaVicidial tabla, DateTime? desde, DateTime? hasta, CancellationToken ct);

        Task<DateTime> HoyAsync(CancellationToken ct);
    }
}
