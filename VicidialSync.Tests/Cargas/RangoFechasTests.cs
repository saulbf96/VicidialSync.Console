using VicidialSync.Domain.Cargas;

namespace VicidialSync.Tests.Cargas
{
    public class RangoFechasTests
    {
        [Fact]
        public void Corta_en_bloques_y_el_ultimo_termina_en_hasta()
        {
            var bloques = RangoFechas.Bloques(new DateTime(2026, 11, 1, 0, 0, 0), new DateTime(2026, 11, 1, 1, 10, 0), 30).ToList();

            Assert.Equal(3, bloques.Count);
            Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0), bloques[0].Inicio);
            Assert.Equal(new DateTime(2026, 11, 1, 0, 30, 0), bloques[0].Fin);
            Assert.Equal(new DateTime(2026, 11, 1, 1, 0, 0), bloques[2].Inicio);
            Assert.Equal(new DateTime(2026, 11, 1, 1, 10, 0), bloques[2].Fin);
        }

        [Fact]
        public void Sin_bloques_cuando_desde_no_es_menor_que_hasta()
        {
            var fecha = new DateTime(2026, 11, 1);
            Assert.Empty(RangoFechas.Bloques(fecha, fecha, 30));
        }

        [Fact]
        public void Minutos_en_cero_marca_error()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RangoFechas.Bloques(new DateTime(2026, 11, 1), new DateTime(2026, 11, 2), 0).ToList());
        }
    }
}
