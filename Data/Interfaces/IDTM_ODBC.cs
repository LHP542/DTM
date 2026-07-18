namespace DTM.ODBC
{
    public interface IDTM_ODBC
    {
        public List<DatabaseInfo> get_Datenbank_Names();
        public DatabaseStats GetDatabase_Stats(DatabaseInfo database);
    }
}
