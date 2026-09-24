namespace DTM
{

    public record Session
    {
        public string? Username { get; set; }
        public string? Maschine { get; set; }
        public string? Program { get; set; }
        public string? Status { get; set; }
    }

    public record Tablespace
    {
        public string? TableSpaceName { get; set; }
        public double TotalMB { get; set; }
        public double UsedMB { get; set; }
        public double FreeMB { get; set; }
        public double UsedPercent { get; set; }

    }
    public record File
    {
        public string? FileLogicalName { get; set; }
        public string? Type { get; set; }
        public double FileSizeMB { get; set; }
        public double FileMaxSizeMB { get; set; }
        public double Growth { get; set; }
        public bool IsPercentigGrowth { get; set; }
        public string? PysicalName { get; set; }
    }
    public record Database_Stats
    {
        public string? Server { get; set; }
        public string? Name { get; set; }
        public string? DatabaseTyp { get; set; }
        public string? State { get; set; }
        public double DataSizeMB { get; set; }
        public int ActiveConnections { get; set; }
        public List<Session>? Sessions { get; set; }

    }

    public record Database_Stats_MSSQL : Database_Stats
    {
        public string? RecorveryModel { get; set; }
        public int CompatibllityLevel { get; set; }

        public string? Collation { get; set; }
        public string? UserAccess { get; set; }
        public bool IsReadOnly { get; set; }
        public string? PageVerify { get; set; }
        public DateTime? CreationTime { get; set; }
        public double LogSizeMB { get; set; }
        public double TotalSizeMB { get; set; }
        public double BufferSizeMB { get; set; }

        public DateTime? LastFullBackup { get; set; }
        public DateTime? LastDiffBackup { get; set; }
        public DateTime? LastLogBackup { get; set; }
        public List<File>? Files { get; set; }


        public Database_Stats_MSSQL() : base()
        {

        }
    }

    /// <summary>
    /// Kennzahlen einer MariaDB-/MySQL-Datenbank (= eines Schemas).
    ///
    /// <para>Die Felder sind bewusst andere als bei MSSQL: ein Schema hat
    /// keine Dateien, keinen Recovery-Modus und keinen Online/Offline-Zustand.
    /// Was es gibt, kommt aus <c>information_schema</c> — Groesse getrennt
    /// nach Daten und Indizes, Zeichensatz, Sortierung und die verwendeten
    /// Storage-Engines.</para>
    /// </summary>
    public record Database_Stats_MariaDb : Database_Stats
    {
        /// <summary>Server-Version, z. B. „11.4.2-MariaDB".</summary>
        public string? ServerVersion { get; set; }

        /// <summary>Standard-Zeichensatz des Schemas (z. B. <c>utf8mb4</c>).</summary>
        public string? CharacterSet { get; set; }

        /// <summary>Standard-Sortierung des Schemas.</summary>
        public string? Collation { get; set; }

        public int TableCount { get; set; }

        /// <summary>Summe <c>index_length</c> ueber alle Tabellen.</summary>
        public double IndexSizeMB { get; set; }

        /// <summary>Daten + Indizes — das Gegenstueck zu MSSQLs TotalSizeMB.</summary>
        public double TotalSizeMB { get; set; }

        /// <summary>
        /// Verwendete Storage-Engines mit Tabellenzahl, z. B. „InnoDB (42)".
        /// Gemischte Engines sind ein haeufiger Grund, warum ein Backup oder
        /// eine Wartung sich anders verhaelt als erwartet — deshalb sichtbar.
        /// </summary>
        public string? Engines { get; set; }

        public Database_Stats_MariaDb() : base() { }
    }

    public record Database_Stats_ORACLE : Database_Stats
    {
        public string? InstanceName { get; set; }
        public string? HostName { get; set; }
        public string? OracleVersion { get; set; }
        public string? InstanceStatus { get; set; }
        public string? ArchiveLogMode { get; set; }

        public double SGASizeMB { get; set; }
        public double PGAAllocatedMB { get; set; }
        public List<Tablespace>? Tablespaces { get; set; }
        public Database_Stats_ORACLE() : base()
        {

        }
    }
}