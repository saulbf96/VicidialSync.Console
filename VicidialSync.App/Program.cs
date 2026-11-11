using MailKit;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using Serilog;


var carpetaLogs = Path.Combine(AppContext.BaseDirectory, "logs");


Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(carpetaLogs, "vicidialsync-.log"),
    rollingInterval: RollingInterval.Day,
    retainedFileCountLimit: 30)
    .CreateLogger();



try
{
    //host c configuracion 
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();

    using var host = builder.Build();

    Log.Information("VicidialSync iniciado. Ambiente: {Ambiente}", builder.Environment.EnvironmentName);

    return 0; // 0 = ok para el task de tareas 
}
catch (Exception ex )
{

    Log.Fatal(ex, "VicidialSync  termino de correr");

    return 1; // 1 = fallo

}
finally
{
    Log.CloseAndFlush(); // escribe lo pendiente antes de correr 
    
}







