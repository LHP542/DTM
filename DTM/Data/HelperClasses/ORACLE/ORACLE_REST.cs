using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using NLog;

namespace DTM.ORACLE
{
    public class REST : IDisposable
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly HttpClient _http;
        private readonly bool _ownsHandler;
        private readonly SocketsHttpHandler? _handler;
        public ServerCredential serverCredential { get; private set; }
        public REST(ServerCredential credential, bool trustAllCertificates = false)
        {
            serverCredential = credential;

            // Kein Proxy: der OLVM-Manager ist immer ein interner Host, oft
            // sogar nur als Kurzname (z. B. 'olvm-mgmt'). Ohne diese Zeile
            // uebernimmt SocketsHttpHandler automatisch HttpClient.DefaultProxy,
            // also HTTP_PROXY/HTTPS_PROXY aus der Umgebung — was lokale
            // NTLM-Proxies wie px setzen. NO_PROXY greift dabei nicht, weil
            // .NET Kurznamen ohne Domain nicht als "lokal" behandelt: der
            // Request landet beim Firmen-Upstream und laeuft ins Timeout.
            _handler = new SocketsHttpHandler { UseProxy = false };

            if (trustAllCertificates)
            {
                // ACHTUNG: deaktiviert MITM-Schutz. Siehe Hinweise unten zur Produktivvariante.
                _handler.SslOptions.RemoteCertificateValidationCallback =
                    (_, _, _, _) => true;
                _logger.Warn("Oracle REST: SSL-Zertifikatsprüfung deaktiviert für {0}", credential.Server);
            }

            _http = new HttpClient(_handler, disposeHandler: true)
            {
                BaseAddress = new Uri($"https://{credential.Server}/ovirt-engine/api/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _ownsHandler = true;

            string basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{credential.User}:{credential.Password}"));
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", basic);

            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            // Fixiert API-Major-Version. Empfohlen, falls Engine später aktualisiert wird.
            _http.DefaultRequestHeaders.Add("Version", "4");

            // OLVM antwortet bei fehlendem User-Agent mit 403 in manchen Konfigs
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("OlvmCsharpClient/1.0");

            _logger.Info("Oracle REST-Client initialisiert: Server={0}, User={1}", credential.Server, credential.User);
        }

        public async Task<IReadOnlyList<VmInfo>> GetVmsAsync(string? search = null, CancellationToken ct = default)
        {
            string url = "vms";
            if (!string.IsNullOrWhiteSpace(search))
            {
                url += $"?search={Uri.EscapeDataString(search)}";
            }

            _logger.Debug("Oracle REST: Lade VMs (search={0})", search ?? "(alle)");
            try
            {
                VmListResponse? response = await _http.GetFromJsonAsync<VmListResponse>(url, ct);
                var result = response?.Vms ?? [];
                _logger.Info("Oracle REST: {0} VMs geladen.", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Oracle REST: GetVmsAsync fehlgeschlagen.");
                throw;
            }
        }

        /// <summary>Liefert (Name, FQDN, Status) je VM.</summary>
        public async Task<IReadOnlyList<VmFqdnEntry>> GetAllVmFqdnsAsync(bool onlyRunning = false, CancellationToken ct = default)
        {
            // Mit "status=up" sparst du Bandbreite, wenn dich nur laufende VMs interessieren.
            string? search = onlyRunning ? "status=up" : null;
            try
            {
                IReadOnlyList<VmInfo> vms = await GetVmsAsync(search, ct);
                var result = vms
                    .Select(v => new VmFqdnEntry(v.Name, v.Fqdn, v.Status, v.Id))
                    .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _logger.Info("Oracle REST: {0} VM-FQDNs geladen.", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Oracle REST: GetAllVmFqdnsAsync fehlgeschlagen.");
                throw;
            }
        }

        /// <summary>
        /// Liest alle Snapshots einer VM (Phase 11.3). OLVM-Endpoint
        /// <c>GET /vms/{id}/snapshots</c> — der "Active VM"-Snapshot ist
        /// immer dabei (Type=active), den filtert die UI-Schicht raus.
        /// </summary>
        public async Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync(
            string vmId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(vmId))
                throw new ArgumentException("vmId darf nicht leer sein.", nameof(vmId));

            string url = $"vms/{Uri.EscapeDataString(vmId)}/snapshots";
            _logger.Debug("Oracle REST: Lade Snapshots fuer VM {0}", vmId);
            try
            {
                SnapshotListResponse? response =
                    await _http.GetFromJsonAsync<SnapshotListResponse>(url, ct);
                var result = response?.Snapshots ?? [];
                _logger.Info("Oracle REST: {0} Snapshots fuer VM {1} geladen.", result.Count, vmId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Oracle REST: GetSnapshotsAsync fehlgeschlagen (VM {0}).", vmId);
                throw;
            }
        }

        public void Dispose()
        {
            _http.Dispose();
            if (_ownsHandler) _handler?.Dispose();
        }
    }
    public sealed record VmListResponse(
    [property: JsonPropertyName("vm")] List<VmInfo> Vms);

    public sealed record VmInfo(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("fqdn")] string? Fqdn,
        [property: JsonPropertyName("status")] string Status);

    public sealed record VmFqdnEntry(string Name, string? Fqdn, string Status, string Id);

    public sealed record SnapshotListResponse(
        [property: JsonPropertyName("snapshot")] List<SnapshotInfo> Snapshots);

    /// <summary>
    /// Einzelner OLVM/oVirt-Snapshot. Feldsatz absichtlich klein: nur was
    /// die UI zum Anzeigen + Restore/Delete-Aufruf braucht. Der "Active VM"-
    /// Snapshot hat <c>SnapshotType = "active"</c> und wird von der UI-
    /// Schicht rausgefiltert.
    /// </summary>
    public sealed record SnapshotInfo(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("snapshot_status")] string? SnapshotStatus,
        [property: JsonPropertyName("snapshot_type")] string? SnapshotType);
}
