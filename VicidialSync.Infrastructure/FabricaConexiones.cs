using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace VicidialSync.Infrastructure
{
    public sealed class FabricaConexiones
    {
        private readonly string _sqlServer;
        private readonly string _mariaDb;

        public FabricaConexiones(string cadenaSqlServer, string cadenaMariaDb)
        {
            // El 205 llega a tardar más de 15 s en aceptar la conexión.
            var sql = new SqlConnectionStringBuilder(cadenaSqlServer);
            if (sql.ConnectTimeout < 90) sql.ConnectTimeout = 90;
            _sqlServer = sql.ConnectionString;
            _mariaDb = cadenaMariaDb;
        }

        public string CadenaSqlServer => _sqlServer;

        public async Task<SqlConnection> AbrirSqlServerAsync(CancellationToken ct)
        {
            var conexion = new SqlConnection(_sqlServer);
            await conexion.OpenAsync(ct);
            return conexion;
        }

        public async Task<MySqlConnection> AbrirMariaDbAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_mariaDb))
                throw new InvalidOperationException("Falta ConnectionStrings:MariaDb");

            var conexion = new MySqlConnection(_mariaDb);
            await conexion.OpenAsync(ct);
            return conexion;
        }
    }
}
