namespace DTM.Data.Olvm;

/// <summary>
/// Ein OLVM-VM-Snapshot fuer den Auswahl-Dialog (Phase 11.3). Wird aus
/// <see cref="DTM.ORACLE.SnapshotInfo"/> gemappt — kompaktes UI-Modell
/// ohne die JSON-Rohfelder.
/// </summary>
public sealed record OlvmSnapshotInfo(
    string Id,
    string Description,
    DateTime? CreatedAt,
    string Status,
    string Type);
