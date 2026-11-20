using System.Data;
using Microsoft.Data.SqlClient;
using VicidialSync.Application.Abstracciones;
using VicidialSync.Domain.Cargas;

namespace VicidialSync.Infrastructure.BaseDatos
{
    public sealed class ControlSqlServer(FabricaConexiones conexiones) : IControlCargas
    {
        private const byte LeerControl = 1;
        private const byte GuardarControl = 2;
        private const byte AbrirBitacora = 3;
        private const byte CerrarBitacora = 4;

        public async Task<EstadoControl> LeerAsync(TablaVicidial tabla, CancellationToken ct)
        {
            await using var sql = await conexiones.AbrirSqlServerAsync(ct);
            await using var comando = Comando(sql, LeerControl);
            comando.Parameters.Add("@TABLA_ORIGEN", SqlDbType.VarChar, 64).Value = tabla.Nombre;

            await using var lector = await comando.ExecuteReaderAsync(ct);
            await lector.ReadAsync(ct);
            return new EstadoControl(
                lector.GetBoolean(lector.GetOrdinal("CARGA_INICIAL_COMPLETA")),
                lector["FECHA_HASTA_CARGADA"] as DateTime?);
        }

        public async Task GuardarAsync(TablaVicidial tabla, DateTime? fechaHastaCargada, bool cargaInicialCompleta, bool corridaOk, CancellationToken ct)
        {
            await using var sql = await conexiones.AbrirSqlServerAsync(ct);
            await using var comando = Comando(sql, GuardarControl);
            comando.Parameters.Add("@TABLA_ORIGEN", SqlDbType.VarChar, 64).Value = tabla.Nombre;
            comando.Parameters.Add("@FECHA_HASTA_CARGADA", SqlDbType.DateTime2).Value = Valor(fechaHastaCargada);
            comando.Parameters.Add("@CARGA_INICIAL_COMPLETA", SqlDbType.Bit).Value = cargaInicialCompleta;
            comando.Parameters.Add("@MARCAR_CORRIDA_OK", SqlDbType.Bit).Value = corridaOk;
            await comando.ExecuteNonQueryAsync(ct);
        }

        public async Task<long> AbrirBitacoraAsync(Guid idCorrida, TablaVicidial tabla, string tipoCarga, DateTime? desde, DateTime? hasta, CancellationToken ct)
        {
            await using var sql = await conexiones.AbrirSqlServerAsync(ct);
            await using var comando = Comando(sql, AbrirBitacora);
            comando.Parameters.Add("@ID_CORRIDA", SqlDbType.UniqueIdentifier).Value = idCorrida;
            comando.Parameters.Add("@TABLA_ORIGEN", SqlDbType.VarChar, 64).Value = tabla.Nombre;
            comando.Parameters.Add("@TIPO_CARGA", SqlDbType.VarChar, 10).Value = tipoCarga;
            comando.Parameters.Add("@FECHA_DESDE", SqlDbType.DateTime2).Value = Valor(desde);
            comando.Parameters.Add("@FECHA_HASTA", SqlDbType.DateTime2).Value = Valor(hasta);
            var id = comando.Parameters.Add("@ID_BITACORA", SqlDbType.BigInt);
            id.Direction = ParameterDirection.Output;
            await comando.ExecuteNonQueryAsync(ct);
            return (long)id.Value;
        }

        public async Task CerrarBitacoraAsync(long idBitacora, string estado, ResultadoBloque totales, DateTime? hasta, string? mensaje, CancellationToken ct)
        {
            await using var sql = await conexiones.AbrirSqlServerAsync(ct);
            await using var comando = Comando(sql, CerrarBitacora);
            comando.Parameters.Add("@ID_BITACORA", SqlDbType.BigInt).Value = idBitacora;
            comando.Parameters.Add("@ESTADO", SqlDbType.VarChar, 10).Value = estado;
            comando.Parameters.Add("@FILAS_LEIDAS", SqlDbType.BigInt).Value = totales.Leidas;
            comando.Parameters.Add("@FILAS_INSERTADAS", SqlDbType.BigInt).Value = totales.Insertadas;
            comando.Parameters.Add("@FILAS_ACTUALIZADAS", SqlDbType.BigInt).Value = totales.Actualizadas;
            comando.Parameters.Add("@FECHA_HASTA", SqlDbType.DateTime2).Value = Valor(hasta);
            comando.Parameters.Add("@MENSAJE", SqlDbType.NVarChar, 4000).Value = (object?)mensaje ?? DBNull.Value;
            await comando.ExecuteNonQueryAsync(ct);
        }

        private static SqlCommand Comando(SqlConnection sql, byte opcion)
        {
            var comando = new SqlCommand("dbo.USP_CONTROL_CARGAS_VICIDIAL", sql)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 600
            };
            comando.Parameters.Add("@OPCION", SqlDbType.TinyInt).Value = opcion;
            return comando;
        }

        private static object Valor(DateTime? fecha) => (object?)fecha ?? DBNull.Value;
    }
}
