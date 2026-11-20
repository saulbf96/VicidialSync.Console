using VicidialSync.Domain.Cargas;

namespace VicidialSync.Infrastructure.Vicidial
{
    public sealed record ConsultaVicidial(string TablaPaso, string Sql);

    // Los UNSIGNED se convierten a SIGNED para que SqlBulkCopy los acepte.
    public static class ConsultasVicidial
    {
        private const string ColumnasLog = """
            uniqueid, CAST(lead_id AS SIGNED) AS lead_id, CAST(list_id AS SIGNED) AS list_id, campaign_id, call_date,
            CAST(start_epoch AS SIGNED) AS start_epoch, CAST(end_epoch AS SIGNED) AS end_epoch, length_in_sec,
            status, phone_code, phone_number, user, comments, processed, user_group, term_reason, alt_dial,
            CAST(called_count AS SIGNED) AS called_count
            """;

        private static readonly Dictionary<string, ConsultaVicidial> Consultas = new()
        {
            [TablaVicidial.PauseCodes.Nombre] = new("dbo.TBL_STG_PAUSE_CODES_VICIDIAL", """
                SELECT pause_code, pause_code_name, billable, campaign_id,
                       CAST(time_limit AS SIGNED) AS time_limit, require_mgr_approval
                  FROM vicidial_pause_codes
                """),

            [TablaVicidial.AgentLog.Nombre] = new("dbo.TBL_STG_AGENT_LOG_VICIDIAL", """
                SELECT 1 AS ORIGEN, CAST(agent_log_id AS SIGNED) AS agent_log_id, user, server_ip, event_time,
                       CAST(lead_id AS SIGNED) AS lead_id, campaign_id,
                       CAST(pause_epoch AS SIGNED) AS pause_epoch, CAST(pause_sec AS SIGNED) AS pause_sec,
                       CAST(wait_epoch AS SIGNED) AS wait_epoch, CAST(wait_sec AS SIGNED) AS wait_sec,
                       CAST(talk_epoch AS SIGNED) AS talk_epoch, CAST(talk_sec AS SIGNED) AS talk_sec,
                       CAST(dispo_epoch AS SIGNED) AS dispo_epoch, CAST(dispo_sec AS SIGNED) AS dispo_sec,
                       status, user_group, comments, sub_status,
                       CAST(dead_epoch AS SIGNED) AS dead_epoch, CAST(dead_sec AS SIGNED) AS dead_sec,
                       processed, uniqueid, pause_type
                  FROM vicidial_agent_log
                 WHERE event_time >= @desde AND event_time < @hasta
                """),

            [TablaVicidial.Log.Nombre] = new("dbo.TBL_STG_LOG_VICIDIAL", $"""
                SELECT 1 AS ORIGEN, {ColumnasLog}
                  FROM vicidial_log
                 WHERE call_date >= @desde AND call_date < @hasta
                UNION ALL
                SELECT 2 AS ORIGEN, {ColumnasLog}
                  FROM vicidial_log_archive
                 WHERE call_date >= @desde AND call_date < @hasta
                """),

            [TablaVicidial.CarrierLog.Nombre] = new("dbo.TBL_STG_CARRIER_LOG_VICIDIAL", """
                SELECT 1 AS ORIGEN, uniqueid, call_date, server_ip, CAST(lead_id AS SIGNED) AS lead_id,
                       CAST(hangup_cause AS SIGNED) AS hangup_cause, dialstatus, channel,
                       CAST(dial_time AS SIGNED) AS dial_time, CAST(answered_time AS SIGNED) AS answered_time,
                       CAST(sip_hangup_cause AS SIGNED) AS sip_hangup_cause, sip_hangup_reason, caller_code
                  FROM vicidial_carrier_log
                 WHERE call_date >= @desde AND call_date < @hasta
                """)
        };

        public static ConsultaVicidial De(TablaVicidial tabla) =>
            Consultas.TryGetValue(tabla.Nombre, out var consulta)
                ? consulta
                : throw new InvalidOperationException($"No hay consulta para {tabla.Nombre}.");
    }
}
