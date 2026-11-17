using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Globalization;
using VicidialSync.Infrastructure.BaseDatos;
using VicidialSync.Infrastructure.Vicidial;

var carpetaLogs = Path.Combine(AppContext.BaseDirectory, "logs");

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(carpetaLogs, "vicidialsync-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    // Host con configuracion (appsettings, user-secrets, variables de entorno)
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.Services.AddSerilog();
    builder.Services.AddSingleton<MigradorBaseDatos>();
    builder.Services.AddSingleton<CargadorVicidial>();

    using var host = builder.Build();

    Log.Information("VicidialSync iniciado. Ambiente: {Ambiente}", builder.Environment.EnvironmentName);

    var cadena = builder.Configuration.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:SqlServer");

    if (args.Contains("--migrar"))
    {
        var ok = host.Services.GetRequiredService<MigradorBaseDatos>().Migrar(cadena);
        return ok ? 0 : 1;
    }

    var cadenaMariaDb = builder.Configuration.GetConnectionString("MariaDb")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:MariaDb");

    // Carga inicial reanudable: --inicial [minutosPorBloque]
    var posicionInicial = Array.IndexOf(args, "--inicial");
    if (posicionInicial >= 0)
    {
        var fechaInicio = builder.Configuration.GetValue("Carga:FechaInicio", new DateTime(2026, 11, 1));
        var minutosInicial = args.Length > posicionInicial + 1 && int.TryParse(args[posicionInicial + 1], out var mi) ? mi : 30;

        var ok = await host.Services.GetRequiredService<CargadorVicidial>()
            .CargaInicialAsync(cadenaMariaDb, cadena, fechaInicio, minutosInicial);
        return ok ? 0 : 1;
    }

    // Prueba de carga: --cargar 2026-11-10T08:00 2026-11-10T10:00 [minutosPorBloque]
    var posicion = Array.IndexOf(args, "--cargar");
    if (posicion >= 0)
    {
        var desde = DateTime.Parse(args[posicion + 1], CultureInfo.InvariantCulture);
        var hasta = DateTime.Parse(args[posicion + 2], CultureInfo.InvariantCulture);
        var minutos = args.Length > posicion + 3 && int.TryParse(args[posicion + 3], out var m) ? m : 30;

        Log.Information("Carga de {Desde:yyyy-MM-dd HH:mm} a {Hasta:yyyy-MM-dd HH:mm} en bloques de {Minutos} min", desde, hasta, minutos);

        var ok = await host.Services.GetRequiredService<CargadorVicidial>()
            .CargarAsync(cadenaMariaDb, cadena, desde, hasta, minutos);
        return ok ? 0 : 1;
    }

    return 0; // 0 = ok para el Programador de tareas
}
catch (Exception ex)
{
    Log.Fatal(ex, "VicidialSync termino con error");
    return 1; // 1 = fallo
}
finally
{
    Log.CloseAndFlush(); // escribe lo pendiente antes de salir
}
