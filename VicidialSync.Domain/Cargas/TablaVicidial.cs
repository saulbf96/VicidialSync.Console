namespace VicidialSync.Domain.Cargas
{
    // MinutosBloque: tamaño de bloque por omisión; agent_log es chica y va por día completo.
    public sealed record TablaVicidial(string Nombre, bool PorFecha, int MinutosBloque)
    {
        public static readonly TablaVicidial PauseCodes = new("vicidial_pause_codes", false, 0);
        public static readonly TablaVicidial AgentLog = new("vicidial_agent_log", true, 1440);
        public static readonly TablaVicidial Log = new("vicidial_log", true, 30);
        public static readonly TablaVicidial CarrierLog = new("vicidial_carrier_log", true, 30);

        public static readonly IReadOnlyList<TablaVicidial> Todas = [PauseCodes, AgentLog, Log, CarrierLog];

        public override string ToString() => Nombre;
    }
}
