namespace VicidialSync.Domain.Cargas
{
    public sealed record ResultadoBloque(long Leidas, long Insertadas, long Actualizadas)
    {
        public static readonly ResultadoBloque Vacio = new(0, 0, 0);

        public ResultadoBloque Sumar(ResultadoBloque otro) =>
            new(Leidas + otro.Leidas, Insertadas + otro.Insertadas, Actualizadas + otro.Actualizadas);
    }
}
