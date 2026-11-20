using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using VicidialSync.Application.Abstracciones;
using VicidialSync.Domain.Cargas;

namespace VicidialSync.Infrastructure.BaseDatos
{
    // Carga de una sola vez: vicidial_carrier_log de octubre a dbo.TBL_CARRIER_LOG_OCTUBRE_VICIDIAL.
    // Se retoma sola: arranca desde el bloque de la última call_date ya cargada.
    public sealed class CargaCarrierOctubre(
        IOrigenVicidial origen,
        FabricaConexiones conexiones,
        ILogger<CargaCarrierOctubre> logger)
    {
        private const string Procedimiento = "dbo.USP_CARGAR_CARRIER_LOG_OCTUBRE_VICIDIAL";
        private const string TablaPaso = "dbo.TBL_STG_CARRIER_LOG_OCTUBRE_VICIDIAL";

        public async Task<bool> CargarAsync(DateTime inicio, DateTime fin, int minutosPorBloque, CancellationToken ct = default)
        {
            try
            {
                var desde = await PuntoDeArranqueAsync(inicio, fin, minutosPorBloque, ct);
                logger.LogInformation("Carrier octubre: de {Desde:yyyy-MM-dd HH:mm} a {Hasta:yyyy-MM-dd HH:mm} en bloques de {Minutos} min",
                    desde, fin, minutosPorBloque);

                long totalLeidas = 0, totalInsertadas = 0;

                foreach (var (bloqueDesde, bloqueHasta) in RangoFechas.Bloques(desde, fin, minutosPorBloque))
                {
                    var reloj = Stopwatch.StartNew();
                    using var datos = await origen.LeerAsync(TablaVicidial.CarrierLog, bloqueDesde, bloqueHasta, ct);
                    var segundosLectura = reloj.Elapsed.TotalSeconds;

                    if (datos.Rows.Count == 0)
                    {
                        logger.LogInformation("Carrier octubre {Desde:yyyy-MM-dd HH:mm}: sin filas", bloqueDesde);
                        continue;
                    }

                    var (leidas, insertadas) = await GuardarBloqueAsync(datos, ct);
                    totalLeidas += leidas;
                    totalInsertadas += insertadas;

                    logger.LogInformation("Carrier octubre {Desde:yyyy-MM-dd HH:mm} -> {Hasta:yyyy-MM-dd HH:mm}: leídas {Leidas}, insertadas {Insertadas} (lectura {Lectura:N0} s, total {Total:N0} s)",
                        bloqueDesde, bloqueHasta, leidas, insertadas, segundosLectura, reloj.Elapsed.TotalSeconds);
                }

                logger.LogInformation("Carrier octubre terminado. Leídas {Leidas}, insertadas {Insertadas}", totalLeidas, totalInsertadas);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falló la carga de carrier octubre; vuelva a correr el mismo comando para retomar.");
                return false;
            }
        }

        private async Task<DateTime> PuntoDeArranqueAsync(DateTime inicio, DateTime fin, int minutosPorBloque, CancellationToken ct)
        {
            await using var sql = await conexiones.AbrirSqlServerAsync(ct);
            await using var comando = Comando(sql, 3);
            var ultima = comando.Parameters.Add("@ULTIMA_FECHA", SqlDbType.DateTime2);
            ultima.Direction = ParameterDirection.Output;
            await comando.ExecuteNonQueryAsync(ct);

            if (ultima.Value is not DateTime ultimaFecha || ultimaFecha < inicio)
                return inicio;
            if (ultimaFecha >= fin)
                return fin;

            // Se repite el bloque de la última fecha; el SP no inserta duplicados.
            var bloques = (long)((ultimaFecha - inicio).TotalMinutes / minutosPorBloque);
            return inicio.AddMinutes(bloques * minutosPorBloque);
        }

        private async Task<(int Leidas, int Insertadas)> GuardarBloqueAsync(DataTable datos, CancellationToken ct)
        {
            await using var sql = await conexiones.AbrirSqlServerAsync(ct);

            await using (var limpiar = Comando(sql, 1))
                await limpiar.ExecuteNonQueryAsync(ct);

            using (var copia = new SqlBulkCopy(sql)
            {
                DestinationTableName = TablaPaso,
                BatchSize = 10000,
                BulkCopyTimeout = 0
            })
            {
                foreach (DataColumn columna in datos.Columns)
                    copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
                await copia.WriteToServerAsync(datos, ct);
            }

            await using var pasar = Comando(sql, 2);
            var leidas = pasar.Parameters.Add("@FILAS_LEIDAS", SqlDbType.Int);
            var insertadas = pasar.Parameters.Add("@FILAS_INSERTADAS", SqlDbType.Int);
            leidas.Direction = insertadas.Direction = ParameterDirection.Output;
            await pasar.ExecuteNonQueryAsync(ct);

            return (leidas.Value as int? ?? 0, insertadas.Value as int? ?? 0);
        }

        private static SqlCommand Comando(SqlConnection sql, byte opcion)
        {
            var comando = new SqlCommand(Procedimiento, sql)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 0
            };
            comando.Parameters.Add("@OPCION", SqlDbType.TinyInt).Value = opcion;
            return comando;
        }
    }
}
