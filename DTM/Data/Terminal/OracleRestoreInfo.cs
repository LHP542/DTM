namespace DTM.Data.Terminal;

/// <summary>
/// Strukturiertes Ergebnis des FOC-SQL-Befehls <c>Get-OracleRestoreInfo</c>:
/// die PDB-Liste der Container-DB und die existierenden Restore Points.
/// </summary>
public sealed record OracleRestoreInfo(
    IReadOnlyList<OraclePdb> PdbNames,
    IReadOnlyList<OracleRestorePoint> RestorePoints);

/// <summary>Eine PDB innerhalb einer Oracle-Container-DB.</summary>
public sealed record OraclePdb(string Name, string ConId);

/// <summary>
/// Ein einzelner Oracle-Restore-Point (= Wiederherstellungspunkt).
/// <see cref="Guaranteed"/> ist die Roh-Spalte aus <c>v$restore_point</c>
/// (<c>YES</c>/<c>NO</c>).
/// </summary>
public sealed record OracleRestorePoint(
    string Name,
    string Time,
    string Guaranteed,
    string ConId,
    string Scn);
