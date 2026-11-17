using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Logging;

namespace VicidialSync.Infrastructure.BaseDatos
{
    public sealed class MigradorBaseDatos(ILogger<MigradorBaseDatos> logger)
    {
        private const string Prefijo = "Scripts.";

        public bool Migrar(string cadenaConexion)
        {
            var motor = DeployChanges.To
                .SqlDatabase(cadenaConexion)
                .WithScripts(LeerScripts())
                .WithScriptSorter(s => s.OrderBy(x => x.SqlScriptOptions.RunGroupOrder))
                .JournalToSqlTable("dbo", "TBL_VERSIONES_VICIDIAL")
                .WithTransactionPerScript()
                .LogToNowhere()
                .Build();

            var pendientes = motor.GetScriptsToExecute();

            if (pendientes.Count == 0)
            {
                logger.LogInformation("Base de datos al día.");
                return true;
            }

            foreach (var script in pendientes)
                logger.LogInformation("Pendiente: {Script}", script.Name);

            var resultado = motor.PerformUpgrade();
            if (!resultado.Successful)
            {
                logger.LogError(resultado.Error, "Falló la migración en {Script}", resultado.ErrorScript?.Name);
                return false;
            }

            logger.LogInformation("Migración terminada: {Cantidad} scripts aplicados.", resultado.Scripts.Count());
            return true;
        }

        // Lee los .sql metidos en el .dll y saca el número del nombre ("10.TBL_...sql" -> 10)
        private static IEnumerable<SqlScript> LeerScripts()
        {
            var ensamblado = typeof(MigradorBaseDatos).Assembly;

            foreach (var recurso in ensamblado.GetManifestResourceNames()
                         .Where(n => n.StartsWith(Prefijo) && n.EndsWith(".sql")))
            {
                var archivo = recurso[Prefijo.Length..];
                var numero = int.Parse(archivo[..archivo.IndexOf('.')]);

                using var stream = ensamblado.GetManifestResourceStream(recurso)!;
                using var lector = new StreamReader(stream);

                yield return new SqlScript(archivo, lector.ReadToEnd(),
                    new SqlScriptOptions { RunGroupOrder = numero });


            }
        }
    }
}


  