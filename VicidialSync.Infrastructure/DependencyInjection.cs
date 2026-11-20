using Microsoft.Extensions.DependencyInjection;
using VicidialSync.Application.Abstracciones;
using VicidialSync.Application.Cargas;
using VicidialSync.Infrastructure.BaseDatos;
using VicidialSync.Infrastructure.Vicidial;

namespace VicidialSync.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfraestructura(this IServiceCollection services, string cadenaSqlServer, string cadenaMariaDb)
        {
            services.AddSingleton(new FabricaConexiones(cadenaSqlServer, cadenaMariaDb));
            services.AddSingleton<MigradorBaseDatos>();
            services.AddSingleton<IOrigenVicidial, OrigenMariaDb>();
            services.AddSingleton<IDestinoCargas, DestinoSqlServer>();
            services.AddSingleton<IControlCargas, ControlSqlServer>();
            services.AddSingleton<CargaVicidialService>();
            return services;
        }
    }
}
