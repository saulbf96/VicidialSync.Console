using System.Data;
using VicidialSync.Domain.Cargas;

namespace VicidialSync.Application.Abstracciones
{
    public interface IDestinoCargas
    {
        Task<ResultadoBloque> CargarAsync(TablaVicidial tabla, DataTable datos, CancellationToken ct);
    }
}
