using System.Globalization;
using DTM.Data.Oracle;
using NLog;

namespace DTM.Data.Olvm;

/// <summary>
/// Phase 11.3: Snapshot-Verwaltung an OLVM-VMs. Aktuell nur ListAsync
/// (via oVirt-REST). Restore und Delete kommen in 11.4/11.5 sobald die
/// Ansible-Playbooks bereit sind — bis dahin sind die entsprechenden
/// UI-Buttons disabled.
///
/// Klasse besitzt den <see cref="OracleRestClient"/>-Client und disposed ihn.
/// </summary>
public sealed class OlvmSnapshotService : IDisposable
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private readonly OracleRestClient _rest;

    public OlvmSnapshotService(OracleRestClient rest)
    {
        _rest = rest;
    }

    /// <summary>
    /// Listet alle Snapshots einer VM (per UUID). Der oVirt-eigene "Active VM"-
    /// Eintrag (Type=active) wird ausgefiltert — der ist kein echter Snapshot,
    /// sondern der aktuelle Live-Zustand.
    /// </summary>
    public async Task<IReadOnlyList<OlvmSnapshotInfo>> ListAsync(
        string vmId, CancellationToken ct = default)
    {
        IReadOnlyList<SnapshotInfo> raw = await _rest.GetSnapshotsAsync(vmId, ct).ConfigureAwait(false);
        _logger.Debug("OlvmSnapshotService: {0} Rohsnapshots gelesen fuer VM {1}", raw.Count, vmId);

        return raw
            .Where(s => !string.Equals(s.SnapshotType, "active", StringComparison.OrdinalIgnoreCase))
            .Select(s => new OlvmSnapshotInfo(
                Id: s.Id,
                Description: s.Description ?? "(kein Name)",
                CreatedAt: TryParseDate(s.Date),
                Status: s.SnapshotStatus ?? "?",
                Type: s.SnapshotType ?? "?"))
            .OrderByDescending(s => s.CreatedAt ?? DateTime.MinValue)
            .ToList();
    }

    private static DateTime? TryParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // oVirt liefert ISO-8601-Strings (mit Offset). DateTime.Parse mit
        // RoundtripKind ist gut genug, wir zeigen nur die Local-Zeit an.
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                                 DateTimeStyles.RoundtripKind, out var v)
            ? v : (DateTime?)null;
    }

    public void Dispose() => _rest.Dispose();
}
