using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VicidialSync.Application.Abstracciones;
using VicidialSync.Domain.Cargas;

namespace VicidialSync.Application.Cargas
{
    public sealed class CargaVicidialService(
        IOrigenVicidial origen,
        IDestinoCargas destino,
        IControlCargas control,
        ILogger<CargaVicidialService> logger)
    {
        // Carga manual de un rango, sin tocar la tabla de control.
        public async Task<bool> CargarRangoAsync(DateTime desde, DateTime hasta, int? minutosPorBloque, CancellationToken ct = default)
        {
            var ok = true;

            foreach (var tabla in TablaVicidial.Todas)
            {
                try
                {
                    if (!tabla.PorFecha)
                    {
                        await CargarBloqueAsync(tabla, null, null, ct);
                        continue;
                    }

                    foreach (var (inicio, fin) in RangoFechas.Bloques(desde, hasta, minutosPorBloque ?? tabla.MinutosBloque))
                        await CargarBloqueAsync(tabla, inicio, fin, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Falló la carga de {Tabla}", tabla);
                    ok = false;
                }
            }

            return ok;
        }

        // Carga inicial reanudable: sigue desde FECHA_HASTA_CARGADA y llega hasta hoy 00:00 (hora del servidor VICIdial).
        public async Task<bool> CargaInicialAsync(DateTime fechaInicio, int? minutosPorBloque, CancellationToken ct = default)
        {
            var idCorrida = Guid.NewGuid();
            var hoyVicidial = await origen.HoyAsync(ct);
            var ok = true;

            logger.LogInformation("Carga inicial {Corrida}: desde {Inicio:yyyy-MM-dd HH:mm} hasta {Hasta:yyyy-MM-dd HH:mm} (hora VICIdial)",
                idCorrida, fechaInicio, hoyVicidial);

            foreach (var tabla in TablaVicidial.Todas)
            {
                var estado = await control.LeerAsync(tabla, ct);
                if (estado.CargaInicialCompleta)
                {
                    logger.LogInformation("{Tabla}: carga inicial ya completa, se omite.", tabla);
                    continue;
                }

                DateTime? desde = tabla.PorFecha ? estado.FechaHastaCargada ?? fechaInicio : null;
                DateTime? hasta = tabla.PorFecha ? hoyVicidial : null;
                var idBitacora = await control.AbrirBitacoraAsync(idCorrida, tabla, TipoCarga.Inicial, desde, hasta, ct);

                var totales = ResultadoBloque.Vacio;
                var llegoA = desde;

                try
                {
                    if (!tabla.PorFecha)
                    {
                        totales = await CargarBloqueAsync(tabla, null, null, ct);
                    }
                    else
                    {
                        if (desde < hasta)
                            logger.LogInformation("{Tabla}: cargando de {Desde:yyyy-MM-dd HH:mm} a {Hasta:yyyy-MM-dd HH:mm}", tabla, desde, hasta);

                        foreach (var (inicio, fin) in RangoFechas.Bloques(desde!.Value, hasta!.Value, minutosPorBloque ?? tabla.MinutosBloque))
                        {
                            var resultado = await CargarBloqueAsync(tabla, inicio, fin, ct);
                            totales = totales.Sumar(resultado);

                            // Bloques vacíos no tocan el 205; el avance se guarda en el siguiente bloque con datos o al final.
                            if (resultado.Leidas > 0)
                            {
                                await control.GuardarAsync(tabla, fin, false, false, ct);
                                llegoA = fin;
                            }
                        }
                    }

                    await control.GuardarAsync(tabla, hasta, true, true, ct);
                    await control.CerrarBitacoraAsync(idBitacora, EstadoCarga.Ok, totales, hasta, null, ct);
                    logger.LogInformation("{Tabla}: carga inicial completa. Leídas {Leidas}, insertadas {Insertadas}, actualizadas {Actualizadas}",
                        tabla, totales.Leidas, totales.Insertadas, totales.Actualizadas);
                }
                catch (Exception ex)
                {
                    ok = false;
                    logger.LogError(ex, "{Tabla}: falló la carga inicial; avance guardado hasta {LlegoA:yyyy-MM-dd HH:mm}", tabla, llegoA);
                    var mensaje = ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message;
                    await control.CerrarBitacoraAsync(idBitacora, EstadoCarga.Error, totales, llegoA, mensaje, CancellationToken.None);

                    if (ex is OperationCanceledException)
                        throw;
                }
            }

            return ok;
        }

        private async Task<ResultadoBloque> CargarBloqueAsync(TablaVicidial tabla, DateTime? desde, DateTime? hasta, CancellationToken ct)
        {
            var reloj = Stopwatch.StartNew();

            // Se lee todo el bloque a memoria para soltar rápido el candado de MyISAM.
            using var datos = await origen.LeerAsync(tabla, desde, hasta, ct);
            var segundosLectura = reloj.Elapsed.TotalSeconds;

            if (datos.Rows.Count == 0)
            {
                logger.LogInformation("{Tabla} {Desde:yyyy-MM-dd HH:mm} a {Hasta:yyyy-MM-dd HH:mm}: sin datos (lectura {Lectura:F1}s)",
                    tabla, desde, hasta, segundosLectura);
                return ResultadoBloque.Vacio;
            }

            var resultado = await destino.CargarAsync(tabla, datos, ct);

            logger.LogInformation(
                "{Tabla} {Desde:yyyy-MM-dd HH:mm} a {Hasta:yyyy-MM-dd HH:mm}: leídas {Leidas}, insertadas {Insertadas}, actualizadas {Actualizadas} (lectura {Lectura:F1}s, total {Total:F1}s)",
                tabla, desde, hasta, resultado.Leidas, resultado.Insertadas, resultado.Actualizadas,
                segundosLectura, reloj.Elapsed.TotalSeconds);

            return resultado;
        }
    }
}
