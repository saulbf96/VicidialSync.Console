using System.Data;
using MySqlConnector;
using VicidialSync.Application.Abstracciones;
using VicidialSync.Domain.Cargas;

namespace VicidialSync.Infrastructure.Vicidial
{
    public sealed class OrigenMariaDb(FabricaConexiones conexiones) : IOrigenVicidial
    {
        public async Task<DataTable> LeerAsync(TablaVicidial tabla, DateTime? desde, DateTime? hasta, CancellationToken ct)
        {
            await using var conexion = await conexiones.AbrirMariaDbAsync(ct);
            await using var comando = new MySqlCommand(ConsultasVicidial.De(tabla).Sql, conexion) { CommandTimeout = 600 };

            if (tabla.PorFecha)
            {
                comando.Parameters.AddWithValue("@desde", desde);
                comando.Parameters.AddWithValue("@hasta", hasta);
            }

            await using var lector = await comando.ExecuteReaderAsync(ct);

            // Tabla armada a mano: DataTable.Load copia la llave primaria y truena con duplicados de log + archive.
            var datos = new DataTable();
            for (var i = 0; i < lector.FieldCount; i++)
                datos.Columns.Add(lector.GetName(i), lector.GetFieldType(i));

            var valores = new object[lector.FieldCount];
            while (await lector.ReadAsync(ct))
            {
                lector.GetValues(valores);
                datos.Rows.Add(valores);
            }

            return datos;
        }

        public async Task<DateTime> HoyAsync(CancellationToken ct)
        {
            await using var conexion = await conexiones.AbrirMariaDbAsync(ct);
            await using var comando = new MySqlCommand("SELECT CAST(CURDATE() AS DATETIME)", conexion);
            return Convert.ToDateTime(await comando.ExecuteScalarAsync(ct));
        }
    }
}
