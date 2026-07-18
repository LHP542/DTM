namespace DTM.Data.Odbc
{
    public interface IDtmOdbc
    {
        public List<DatabaseInfo> get_Datenbank_Names();
        public DatabaseStats GetDatabase_Stats(DatabaseInfo database);
    }
}
