using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace VicidialSync.Infrastructure.Vicidial
{
    public sealed record ResultadoBloque(int Leidas, int Insertadas, int Actualizadas);

    public sealed class CargadorVicidial(ILogger<CargadorVicidial> logger)
    {
        public async Task<bool> CargarAsync(string cadenaMariaDb, string cadenaSqlServer,
            DateTime desde, DateTime hasta, int minutosPorBloque, CancellationToken ct = default)
        {
            var ok = true;

            foreach (var consulta in ConsultasVicidial.Todas)
            {
                try
                {
                    if (!consulta.PorFecha)
                    {
                        await CargarBloqueAsync(consulta, cadenaMariaDb, cadenaSqlServer, null, null, ct);
                        continue;
                    }

                    for (var inicio = desde; inicio < hasta; inicio = inicio.AddMinutes(minutosPorBloque))
                    {
                        var fin = inicio.AddMinutes(minutosPorBloque);
                        if (fin > hasta) fin = hasta;
                        await CargarBloqueAsync(consulta, cadenaMariaDb, cadenaSqlServer, inicio, fin, ct);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Falló la carga de {Tabla}", consulta.TablaOrigen);
                    ok = false;
                }
            }

            return ok;
        }

        private async Task CargarBloqueAsync(ConsultaVicidial consulta, string cadenaMariaDb, string cadenaSqlServer,
            DateTime? desde, DateTime? hasta, CancellationToken ct)
        {
            var reloj = Stopwatch.StartNew();

            // Se lee todo el bloque a memoria para soltar rápido el candado de MyISAM.
            var datos = await LeerAsync(consulta, cadenaMariaDb, desde, hasta, ct);
            var segundosLectura = reloj.Elapsed.TotalSeconds;

            await using var sql = new SqlConnection(cadenaSqlServer);
            await sql.OpenAsync(ct);

            await EjecutarSpAsync(sql, 1, consulta.TablaOrigen, ct);

            using (var copia = new SqlBulkCopy(sql) { DestinationTableName = consulta.TablaPaso, BatchSize = 10000, BulkCopyTimeout = 0 })
            {
                foreach (DataColumn columna in datos.Columns)
                    copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
                await copia.WriteToServerAsync(datos, ct);
            }

            var resultado = await EjecutarSpAsync(sql, 2, consulta.TablaOrigen, ct);

            logger.LogInformation(
                "{Tabla} {Desde:yyyy-MM-dd HH:mm} a {Hasta:HH:mm}: leídas {Leidas}, insertadas {Insertadas}, actualizadas {Actualizadas} (lectura {Lectura:F1}s, total {Total:F1}s)",
                consulta.TablaOrigen, desde, hasta, resultado.Leidas, resultado.Insertadas, resultado.Actualizadas,
                segundosLectura, reloj.Elapsed.TotalSeconds);
        }

        private static async Task<DataTable> LeerAsync(ConsultaVicidial consulta, string cadenaMariaDb,
            DateTime? desde, DateTime? hasta, CancellationToken ct)
        {
            await using var conexion = new MySqlConnection(cadenaMariaDb);
            await conexion.OpenAsync(ct);

            await using var comando = new MySqlCommand(consulta.Sql, conexion) { CommandTimeout = 600 };
            if (consulta.PorFecha)
            {
                comando.Parameters.AddWithValue("@desde", desde);
                comando.Parameters.AddWithValue("@hasta", hasta);
            }

            await using var lector = await comando.ExecuteReaderAsync(ct);

            // Tabla armada a mano: DataTable.Load copia la llave primaria y truena con duplicados de log + archive.
            var tabla = new DataTable();
            for (var i = 0; i < lector.FieldCount; i++)
                tabla.Columns.Add(lector.GetName(i), lector.GetFieldType(i));

            var valores = new object[lector.FieldCount];
            while (await lector.ReadAsync(ct))
            {
                lector.GetValues(valores);
                tabla.Rows.Add(valores);
            }

            return tabla;
        }

        private static async Task<ResultadoBloque> EjecutarSpAsync(SqlConnection sql, byte opcion, string tablaOrigen, CancellationToken ct)
        {
            await using var comando = new SqlCommand("dbo.USP_CARGAR_DATOS_VICIDIAL", sql)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 0
            };
            comando.Parameters.Add("@OPCION", SqlDbType.TinyInt).Value = opcion;
            comando.Parameters.Add("@TABLA_ORIGEN", SqlDbType.VarChar, 64).Value = tablaOrigen;
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
