using System.Data;
using Microsoft.Data.SqlClient;
using VicidialSync.Application.Abstracciones;
using VicidialSync.Domain.Cargas;
using VicidialSync.Infrastructure.Vicidial;

namespace VicidialSync.Infrastructure.BaseDatos
{
    public sealed class DestinoSqlServer(FabricaConexiones conexiones) : IDestinoCargas
    {
        private const byte LimpiarPaso = 1;
        private const byte PasarBloque = 2;

        public async Task<ResultadoBloque> CargarAsync(TablaVicidial tabla, DataTable datos, CancellationToken ct)
        {
            await using var sql = await conexiones.AbrirSqlServerAsync(ct);

            await EjecutarAsync(sql, LimpiarPaso, tabla, ct);

            using (var copia = new SqlBulkCopy(sql)
            {
                DestinationTableName = ConsultasVicidial.De(tabla).TablaPaso,
                BatchSize = 10000,
                BulkCopyTimeout = 0
            })
            {
                foreach (DataColumn columna in datos.Columns)
                    copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
                await copia.WriteToServerAsync(datos, ct);
            }

            return await EjecutarAsync(sql, PasarBloque, tabla, ct);
        }

        private static async Task<ResultadoBloque> EjecutarAsync(SqlConnection sql, byte opcion, TablaVicidial tabla, CancellationToken ct)
        {
            await using var comando = new SqlCommand("dbo.USP_CARGAR_DATOS_VICIDIAL", sql)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 0
            };
            comando.Parameters.Add("@OPCION", SqlDbType.TinyInt).Value = opcion;
            comando.Parameters.Add("@TABLA_ORIGEN", SqlDbType.VarChar, 64).Value = tabla.Nombre;
            var leidas = comando.Parameters.Add("@FILAS_LEIDAS", SqlDbType.Int);
            var insertadas = comando.Parameters.Add("@FILAS_INSERTADAS", SqlDbType.Int);
            var actualizadas = comando.Parameters.Add("@FILAS_ACTUALIZADAS", SqlDbType.Int);
            leidas.Direction = insertadas.Direction = actualizadas.Direction = ParameterDirection.Output;

            await comando.ExecuteNonQueryAsync(ct);

            return new ResultadoBloque(
                leidas.Value as int? ?? 0,
                insertadas.Value as int? ?? 0,
                actualizadas.Value as int? ?? 0);
        }
    }
}
