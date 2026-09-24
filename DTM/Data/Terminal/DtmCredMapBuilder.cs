using System.Collections;
using System.Management.Automation;
using System.Security;

namespace DTM.Data.Terminal;

/// <summary>
/// Phase 9.5: baut aus der Server-Liste die <c>$global:DtmCredMap</c>-Hashtable,
/// die DTM in den pwsh-Runspace injiziert. Enthält PSCredential-Objekte für
/// alle MSSQL-Server mit gesetztem <see cref="ServerCredential.HasRemoteCredential"/> —
/// Server ohne Remote-Credential landen NICHT in der Map, damit FOC-SQL für die
/// weiterhin auf das globale <c>credential.xml</c> zurückfällt.
///
/// Cases:
/// - Server-Hostname als Key, case-insensitive (Windows-Semantik). Standard-
///   PS-Hashtable ist case-insensitive → matcht das Verhalten der ODBC_Factory.
/// - Passwörter werden nur als <see cref="SecureString"/> in <see cref="PSCredential"/>
///   übergeben — kein Klartext-String im Runspace-State.
/// - Oracle-Server werden ignoriert (SSH-Keys, kein WinRM/PSCredential).
/// </summary>
public static class DtmCredMapBuilder
{
    public static Hashtable Build(IReadOnlyList<DB_SERVER> servers)
    {
        var map = new Hashtable(StringComparer.OrdinalIgnoreCase);
        if (servers is null) return map;

        foreach (var s in servers)
        {
            if (s.Typ != DB_SERVER.ServerTyp.MSSQL) continue;
            if (s.serverCredential is null || !s.serverCredential.HasRemoteCredential) continue;

            var secure = new SecureString();
            foreach (char c in s.serverCredential.RemotePassword) secure.AppendChar(c);
            secure.MakeReadOnly();

            var psCred = new PSCredential(s.serverCredential.RemoteUser, secure);
            map[s.serverCredential.Server] = psCred;
        }
        return map;
    }
}
