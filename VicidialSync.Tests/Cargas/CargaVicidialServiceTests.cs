using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using VicidialSync.Application.Abstracciones;
using VicidialSync.Application.Cargas;
using VicidialSync.Domain.Cargas;

namespace VicidialSync.Tests.Cargas
{
    public class CargaVicidialServiceTests
    {
        private static readonly DateTime Hoy = new(2026, 11, 3);
        private static readonly DateTime Inicio = new(2026, 11, 1);

        [Fact]
        public async Task Carga_inicial_desde_cero_llega_hasta_hoy_y_marca_completa()
        {
            var (servicio, origen, control) = Crear();

            var ok = await servicio.CargaInicialAsync(Inicio, 720);

            Assert.True(ok);
            Assert.Equal(1 + 3 * 4, origen.Lecturas.Count);
            foreach (var tabla in TablaVicidial.Todas)
                Assert.True(control.Estados[tabla.Nombre].CargaInicialCompleta);
            Assert.Equal(Hoy, control.Estados[TablaVicidial.Log.Nombre].FechaHastaCargada);
            Assert.All(control.Bitacora.Values, b => Assert.Equal(EstadoCarga.Ok, b.Estado));
        }

        [Fact]
        public async Task Carga_inicial_retoma_desde_la_ultima_fecha_guardada()
        {
            var (servicio, origen, control) = Crear();
            control.Estados[TablaVicidial.Log.Nombre] = new EstadoControl(false, new DateTime(2026, 11, 2, 12, 0, 0));

            await servicio.CargaInicialAsync(Inicio, 720);

            var lecturasLog = origen.Lecturas.Where(l => l.Tabla == TablaVicidial.Log).ToList();
            Assert.Single(lecturasLog);
            Assert.Equal(new DateTime(2026, 11, 2, 12, 0, 0), lecturasLog[0].Desde);
        }

        [Fact]
        public async Task Carga_inicial_se_salta_tablas_completas()
        {
            var (servicio, origen, control) = Crear();
            foreach (var tabla in TablaVicidial.Todas)
                control.Estados[tabla.Nombre] = new EstadoControl(true, Hoy);

            await servicio.CargaInicialAsync(Inicio, 720);

            Assert.Empty(origen.Lecturas);
            Assert.Empty(control.Bitacora);
        }

        [Fact]
        public async Task Si_falla_un_bloque_guarda_avance_y_bitacora_en_error()
        {
            var (servicio, origen, control) = Crear();
            origen.FallarEn = (TablaVicidial.CarrierLog, new DateTime(2026, 11, 2, 0, 0, 0));

            var ok = await servicio.CargaInicialAsync(Inicio, 720);

            Assert.False(ok);
            var carrier = control.Estados[TablaVicidial.CarrierLog.Nombre];
            Assert.False(carrier.CargaInicialCompleta);
            Assert.Equal(new DateTime(2026, 11, 2, 0, 0, 0), carrier.FechaHastaCargada);
            Assert.Equal(EstadoCarga.Error, control.Bitacora.Values.Single(b => b.Tabla == TablaVicidial.CarrierLog).Estado);
            Assert.True(control.Estados[TablaVicidial.Log.Nombre].CargaInicialCompleta);
        }

        [Fact]
        public async Task Bloques_vacios_no_tocan_el_destino_y_aun_asi_marcan_completa()
        {
            var origen = new OrigenFalso { SinDatos = true };
            var destino = new DestinoFalso();
            var control = new ControlFalso();
            var servicio = new CargaVicidialService(origen, destino, control, NullLogger<CargaVicidialService>.Instance);

            var ok = await servicio.CargaInicialAsync(Inicio, null);

            Assert.True(ok);
            Assert.Equal(0, destino.Llamadas);
            Assert.Equal(2, origen.Lecturas.Count(l => l.Tabla == TablaVicidial.AgentLog));
            Assert.True(control.Estados[TablaVicidial.CarrierLog.Nombre].CargaInicialCompleta);
            Assert.Equal(Hoy, control.Estados[TablaVicidial.CarrierLog.Nombre].FechaHastaCargada);
        }

        private static (CargaVicidialService, OrigenFalso, ControlFalso) Crear()
        {
            var origen = new OrigenFalso();
            var control = new ControlFalso();
            var servicio = new CargaVicidialService(origen, new DestinoFalso(), control, NullLogger<CargaVicidialService>.Instance);
            return (servicio, origen, control);
        }

        private sealed class OrigenFalso : IOrigenVicidial
        {
            public List<(TablaVicidial Tabla, DateTime? Desde, DateTime? Hasta)> Lecturas { get; } = [];
            public (TablaVicidial Tabla, DateTime Desde)? FallarEn { get; set; }
            public bool SinDatos { get; set; }

            public Task<DataTable> LeerAsync(TablaVicidial tabla, DateTime? desde, DateTime? hasta, CancellationToken ct)
            {
                if (FallarEn is { } f && f.Tabla == tabla && f.Desde == desde)
                    throw new InvalidOperationException("Falla simulada");

                Lecturas.Add((tabla, desde, hasta));
                var datos = new DataTable();
                datos.Columns.Add("x", typeof(int));
                if (!SinDatos) datos.Rows.Add(1);
                return Task.FromResult(datos);
            }

            public Task<DateTime> HoyAsync(CancellationToken ct) => Task.FromResult(Hoy);
        }

        private sealed class DestinoFalso : IDestinoCargas
        {
            public int Llamadas { get; private set; }

            public Task<ResultadoBloque> CargarAsync(TablaVicidial tabla, DataTable datos, CancellationToken ct)
            {
                Llamadas++;
                return Task.FromResult(new ResultadoBloque(10, 10, 0));
            }
        }

        private sealed class ControlFalso : IControlCargas
        {
            public Dictionary<string, EstadoControl> Estados { get; } = [];
            public Dictionary<long, (TablaVicidial Tabla, string Estado)> Bitacora { get; } = [];

            public Task<EstadoControl> LeerAsync(TablaVicidial tabla, CancellationToken ct) =>
                Task.FromResult(Estados.TryGetValue(tabla.Nombre, out var e) ? e : new EstadoControl(false, null));

            public Task GuardarAsync(TablaVicidial tabla, DateTime? fechaHastaCargada, bool cargaInicialCompleta, bool corridaOk, CancellationToken ct)
            {
                Estados[tabla.Nombre] = new EstadoControl(cargaInicialCompleta, fechaHastaCargada);
                return Task.CompletedTask;
            }

            public Task<long> AbrirBitacoraAsync(Guid idCorrida, TablaVicidial tabla, string tipoCarga, DateTime? desde, DateTime? hasta, CancellationToken ct)
            {
                var id = Bitacora.Count + 1L;
                Bitacora[id] = (tabla, "EN_PROCESO");
                return Task.FromResult(id);
            }

            public Task CerrarBitacoraAsync(long idBitacora, string estado, ResultadoBloque totales, DateTime? hasta, string? mensaje, CancellationToken ct)
            {
                Bitacora[idBitacora] = (Bitacora[idBitacora].Tabla, estado);
                return Task.CompletedTask;
            }
        }
    }
}
