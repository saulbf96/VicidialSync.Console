using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
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
    // Host con configuracion (appsettings, user-secrets, variables de entorno)
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();
    builder.Services.AddSingleton<MigradorBaseDatos>();

    using var host = builder.Build();

    Log.Information("VicidialSync iniciado. Ambiente: {Ambiente}", builder.Environment.EnvironmentName);

    var cadena = builder.Configuration.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:SqlServer");

    if (args.Contains("--migrar"))
    {
        var ok = host.Services.GetRequiredService<MigradorBaseDatos>().Migrar(cadena);
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
