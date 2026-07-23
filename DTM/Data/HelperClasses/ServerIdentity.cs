namespace DTM;

/// <summary>
/// Eindeutige Identitaet eines Datenbank-Servers in DTM: das Tupel
/// <see cref="DB_SERVER.ServerTyp"/> + Hostname. Mit der Einfuehrung von
/// Phase 6 (Multi-Server-Support) ist diese Composite-Identitaet noetig,
/// weil pro Server-Typ mehrere Hosts existieren koennen
/// (z. B. „FOC-SQL01" und „DEVFOC-SQL01" beides MSSQL).
///
/// Case-insensitive Vergleich auf <see cref="Server"/> — Windows-Hostnames
/// sind nicht case-sensitive, und die Persistenzschicht (connections.json)
/// gibt keine Garantie ueber Schreibweise.
/// </summary>
public sealed record ServerIdentity(DB_SERVER.ServerTyp Typ, string Server)
{
    public bool Equals(ServerIdentity? other)
    {
        if (other is null) return false;
        return Typ == other.Typ
            && string.Equals(Server, other.Server, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode() =>
        HashCode.Combine(Typ, Server?.ToLowerInvariant() ?? string.Empty);

    public override string ToString() => $"{Typ}:{Server}";
}
