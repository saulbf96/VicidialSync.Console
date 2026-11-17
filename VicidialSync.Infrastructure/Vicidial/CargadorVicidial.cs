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
        private sealed record EstadoControl(bool Completa, DateTime? FechaHastaCargada);

        // Carga manual de un rango, sin tocar la tabla de control.
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

                    foreach (var (inicio, fin) in Bloques(desde, hasta, minutosPorBloque))
                        await CargarBloqueAsync(consulta, cadenaMariaDb, cadenaSqlServer, inicio, fin, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Falló la carga de {Tabla}", consulta.TablaOrigen);
                    ok = false;
                }
            }

            return ok;
        }

        // Carga inicial reanudable: sigue desde FECHA_HASTA_CARGADA y llega hasta hoy 00:00 (hora del servidor VICIdial).
        public async Task<bool> CargaInicialAsync(string cadenaMariaDb, string cadenaSqlServer,
            DateTime fechaInicio, int minutosPorBloque, CancellationToken ct = default)
        {
            var idCorrida = Guid.NewGuid();
            var hoyVicidial = await HoyVicidialAsync(cadenaMariaDb, ct);
            var ok = true;

            logger.LogInformation("Carga inicial {Corrida}: desde {Inicio:yyyy-MM-dd} hasta {Hasta:yyyy-MM-dd HH:mm} (hora VICIdial)",
                idCorrida, fechaInicio, hoyVicidial);

            foreach (var consulta in ConsultasVicidial.Todas)
            {
                var estado = await LeerControlAsync(cadenaSqlServer, consulta.TablaOrigen, ct);
                if (estado.Completa)
                {
                    logger.LogInformation("{Tabla}: carga inicial ya completa, se omite.", consulta.TablaOrigen);
                    continue;
                }

                var desde = consulta.PorFecha ? estado.FechaHastaCargada ?? fechaInicio : (DateTime?)null;
                var hasta = consulta.PorFecha ? hoyVicidial : (DateTime?)null;
                var idBitacora = await AbrirBitacoraAsync(cadenaSqlServer, idCorrida, consulta.TablaOrigen, "INICIAL", desde, hasta, ct);

                long leidas = 0, insertadas = 0, actualizadas = 0;
                var llegoA = desde;

                try
                {
                    if (!consulta.PorFecha)
                    {
                        var r = await CargarBloqueAsync(consulta, cadenaMariaDb, cadenaSqlServer, null, null, ct);
                        (leidas, insertadas, actualizadas) = (r.Leidas, r.Insertadas, r.Actualizadas);
                    }
                    else
                    {
                        if (desde < hasta)
                            logger.LogInformation("{Tabla}: cargando de {Desde:yyyy-MM-dd HH:mm} a {Hasta:yyyy-MM-dd HH:mm}",
                                consulta.TablaOrigen, desde, hasta);

                        foreach (var (inicio, fin) in Bloques(desde!.Value, hasta!.Value, minutosPorBloque))
                        {
                            var r = await CargarBloqueAsync(consulta, cadenaMariaDb, cadenaSqlServer, inicio, fin, ct);
                            leidas += r.Leidas; insertadas += r.Insertadas; actualizadas += r.Actualizadas;
                            llegoA = fin;
                            await GuardarControlAsync(cadenaSqlServer, consulta.TablaOrigen, fin, false, false, ct);
                        }
                    }

                    await GuardarControlAsync(cadenaSqlServer, consulta.TablaOrigen, hasta, true, true, ct);
                    await CerrarBitacoraAsync(cadenaSqlServer, idBitacora, "OK", leidas, insertadas, actualizadas, hasta, null, ct);
                    logger.LogInformation("{Tabla}: carga inicial completa. Leídas {Leidas}, insertadas {Insertadas}, actualizadas {Actualizadas}",
                        consulta.TablaOrigen, leidas, insertadas, actualizadas);
                }
                catch (Exception ex)
                {
                    ok = false;
                    logger.LogError(ex, "{Tabla}: falló la carga inicial; avance guardado hasta {LlegoA:yyyy-MM-dd HH:mm}",
                        consulta.TablaOrigen, llegoA);
                    await CerrarBitacoraAsync(cadenaSqlServer, idBitacora, "ERROR", leidas, insertadas, actualizadas, llegoA,
                        ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message, CancellationToken.None);
                }
            }

            return ok;
        }

        private static IEnumerable<(DateTime Inicio, DateTime Fin)> Bloques(DateTime desde, DateTime hasta, int minutos)
        {
            for (var inicio = desde; inicio < hasta; inicio = inicio.AddMinutes(minutos))
            {
                var fin = inicio.AddMinutes(minutos);
                yield return (inicio, fin > hasta ? hasta : fin);
            }
        }

        private async Task<ResultadoBloque> CargarBloqueAsync(ConsultaVicidial consulta, string cadenaMariaDb, string cadenaSqlServer,
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

            return resultado;
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

        private static async Task<DateTime> HoyVicidialAsync(string cadenaMariaDb, CancellationToken ct)
        {
            await using var conexion = new MySqlConnection(cadenaMariaDb);
            await conexion.OpenAsync(ct);
            await using var comando = new MySqlCommand("SELECT CAST(CURDATE() AS DATETIME)", conexion);
            return Convert.ToDateTime(await comando.ExecuteScalarAsync(ct));
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

        private static async Task<SqlCommand> ComandoControlAsync(SqlConnection sql, byte opcion, CancellationToken ct)
        {
            await sql.OpenAsync(ct);
            var comando = new SqlCommand("dbo.USP_CONTROL_CARGAS_VICIDIAL", sql) { CommandType = CommandType.StoredProcedure };
            comando.Parameters.Add("@OPCION", SqlDbType.TinyInt).Value = opcion;
            return comando;
        }

        private static async Task<EstadoControl> LeerControlAsync(string cadenaSqlServer, string tablaOrigen, CancellationToken ct)
        {
            await using var sql = new SqlConnection(cadenaSqlServer);
            await using var comando = await ComandoControlAsync(sql, 1, ct);
            comando.Parameters.Add("@TABLA_ORIGEN", SqlDbType.VarChar, 64).Value = tablaOrigen;

            await using var lector = await comando.ExecuteReaderAsync(ct);
            await lector.ReadAsync(ct);
            return new EstadoControl(
                lector.GetBoolean(lector.GetOrdinal("CARGA_INICIAL_COMPLETA")),
                lector["FECHA_HASTA_CARGADA"] as DateTime?);
        }

        private static async Task GuardarControlAsync(string cadenaSqlServer, string tablaOrigen, DateTime? fechaHasta,
            bool completa, bool corridaOk, CancellationToken ct)
        {
            await using var sql = new SqlConnection(cadenaSqlServer);
            await using var comando = await ComandoControlAsync(sql, 2, ct);
            comando.Parameters.Add("@TABLA_ORIGEN", SqlDbType.VarChar, 64).Value = tablaOrigen;
            comando.Parameters.Add("@FECHA_HASTA_CARGADA", SqlDbType.DateTime2).Value = (object?)fechaHasta ?? DBNull.Value;
            comando.Parameters.Add("@CARGA_INICIAL_COMPLETA", SqlDbType.Bit).Value = completa;
            comando.Parameters.Add("@MARCAR_CORRIDA_OK", SqlDbType.Bit).Value = corridaOk;
            await comando.ExecuteNonQueryAsync(ct);
        }

        private static async Task<long> AbrirBitacoraAsync(string cadenaSqlServer, Guid idCorrida, string tablaOrigen, string tipo,
            DateTime? desde, DateTime? hasta, CancellationToken ct)
        {
            await using var sql = new SqlConnection(cadenaSqlServer);
            await using var comando = await ComandoControlAsync(sql, 3, ct);
            comando.Parameters.Add("@ID_CORRIDA", SqlDbType.UniqueIdentifier).Value = idCorrida;
            comando.Parameters.Add("@TABLA_ORIGEN", SqlDbType.VarChar, 64).Value = tablaOrigen;
            comando.Parameters.Add("@TIPO_CARGA", SqlDbType.VarChar, 10).Value = tipo;
            comando.Parameters.Add("@FECHA_DESDE", SqlDbType.DateTime2).Value = (object?)desde ?? DBNull.Value;
            comando.Parameters.Add("@FECHA_HASTA", SqlDbType.DateTime2).Value = (object?)hasta ?? DBNull.Value;
            var id = comando.Parameters.Add("@ID_BITACORA", SqlDbType.BigInt);
            id.Direction = ParameterDirection.Output;
            await comando.ExecuteNonQueryAsync(ct);
            return (long)id.Value;
        }

        private static async Task CerrarBitacoraAsync(string cadenaSqlServer, long idBitacora, string estado,
            long leidas, long insertadas, long actualizadas, DateTime? hasta, string? mensaje, CancellationToken ct)
        {
            await using var sql = new SqlConnection(cadenaSqlServer);
            await using var comando = await ComandoControlAsync(sql, 4, ct);
            comando.Parameters.Add("@ID_BITACORA", SqlDbType.BigInt).Value = idBitacora;
            comando.Parameters.Add("@ESTADO", SqlDbType.VarChar, 10).Value = estado;
            comando.Parameters.Add("@FILAS_LEIDAS", SqlDbType.BigInt).Value = leidas;
            comando.Parameters.Add("@FILAS_INSERTADAS", SqlDbType.BigInt).Value = insertadas;
            comando.Parameters.Add("@FILAS_ACTUALIZADAS", SqlDbType.BigInt).Value = actualizadas;
            comando.Parameters.Add("@FECHA_HASTA", SqlDbType.DateTime2).Value = (object?)hasta ?? DBNull.Value;
            comando.Parameters.Add("@MENSAJE", SqlDbType.NVarChar, 4000).Value = (object?)mensaje ?? DBNull.Value;
            await comando.ExecuteNonQueryAsync(ct);
        }
    }
}
