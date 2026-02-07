using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool
{
    class DatabaseConfig
    {
        public string User { get; set; } = "SYSDBA";
        public string Password { get; set; } = "masterkey";
        public string DataSource { get; set; } = "localhost";
        public int Port { get; set; } = 3050;
        public int Dialect { get; set; } = 3;
        public string Charset { get; set; } = "UTF8";
        public int PageSize = 4096;


        public DatabaseConfig() { }

        public string GetFbConnectionString(string databasePath)
        {
            var connectionBuilder = new FbConnectionStringBuilder
            {
                UserID = this.User,
                Password = this.Password,
                DataSource = this.DataSource,
                Port = this.Port,
                Database = databasePath,
                Dialect = this.Dialect,
                Charset = this.Charset
            };

            return connectionBuilder.ToString();
        }
    }
}
