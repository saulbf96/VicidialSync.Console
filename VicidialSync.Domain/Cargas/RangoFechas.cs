namespace VicidialSync.Domain.Cargas
{
    public static class RangoFechas
    {
        public static IEnumerable<(DateTime Inicio, DateTime Fin)> Bloques(DateTime desde, DateTime hasta, int minutos)
        {
            if (minutos <= 0)
                throw new ArgumentOutOfRangeException(nameof(minutos), "Los minutos por bloque deben ser mayores a 0.");

            for (var inicio = desde; inicio < hasta; inicio = inicio.AddMinutes(minutos))
            {
                var fin = inicio.AddMinutes(minutos);
                yield return (inicio, fin > hasta ? hasta : fin);
            }
        }
    }
}
