using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Globalization;
using VicidialSync.Application.Cargas;
using VicidialSync.Infrastructure;
using VicidialSync.Infrastructure.BaseDatos;

var carpetaLogs = Path.Combine(AppContext.BaseDirectory, "logs");

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(carpetaLogs, "vicidialsync-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    // Host con configuracion (appsettings junto al .exe, user-secrets, variables de entorno)
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });

    // --destino Sql205 usa ConnectionStrings:Sql205; sin la opción usa ConnectionStrings:SqlServer
    var posicionDestino = Array.IndexOf(args, "--destino");
    var nombreDestino = posicionDestino >= 0 && args.Length > posicionDestino + 1 ? args[posicionDestino + 1] : "SqlServer";
    var cadenaSqlServer = builder.Configuration.GetConnectionString(nombreDestino)
        ?? throw new InvalidOperationException($"Falta ConnectionStrings:{nombreDestino}");
    var cadenaMariaDb = builder.Configuration.GetConnectionString("MariaDb") ?? "";

    builder.Services.AddSerilog();
    builder.Services.AddInfraestructura(cadenaSqlServer, cadenaMariaDb);

    using var host = builder.Build();

    Log.Information("VicidialSync iniciado. Ambiente: {Ambiente}. Destino: {Destino}", builder.Environment.EnvironmentName, nombreDestino);

    if (args.Contains("--migrar"))
    {
        var cadena = host.Services.GetRequiredService<FabricaConexiones>().CadenaSqlServer;
        var ok = host.Services.GetRequiredService<MigradorBaseDatos>().Migrar(cadena);
        return ok ? 0 : 1;
    }

    var cargas = host.Services.GetRequiredService<CargaVicidialService>();

    // Carga inicial reanudable: --inicial [minutosPorBloque]
    var posicionInicial = Array.IndexOf(args, "--inicial");
    if (posicionInicial >= 0)
    {
        var fechaInicio = builder.Configuration.GetValue("Carga:FechaInicio", new DateTime(2026, 11, 1));
        var minutos = LeerMinutos(args, posicionInicial + 1);

        var ok = await cargas.CargaInicialAsync(fechaInicio, minutos);
        return ok ? 0 : 1;
    }

    // Carga manual de un rango: --cargar 2026-11-10T08:00 2026-11-10T10:00 [minutosPorBloque]
    var posicionCargar = Array.IndexOf(args, "--cargar");
    if (posicionCargar >= 0)
    {
        var desde = DateTime.Parse(args[posicionCargar + 1], CultureInfo.InvariantCulture);
        var hasta = DateTime.Parse(args[posicionCargar + 2], CultureInfo.InvariantCulture);
        var minutos = LeerMinutos(args, posicionCargar + 3);

        Log.Information("Carga de {Desde:yyyy-MM-dd HH:mm} a {Hasta:yyyy-MM-dd HH:mm} en bloques de {Minutos} min", desde, hasta, minutos?.ToString() ?? "por tabla");

        var ok = await cargas.CargarRangoAsync(desde, hasta, minutos);
        return ok ? 0 : 1;
    }

    Log.Warning("Sin opción. Use --migrar, --inicial [minutos] o --cargar <desde> <hasta> [minutos].");
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

static int? LeerMinutos(string[] args, int posicion) =>
    args.Length > posicion && int.TryParse(args[posicion], out var minutos) ? minutos : null;
