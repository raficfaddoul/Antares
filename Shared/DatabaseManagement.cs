using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Protocols;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System.Collections.Specialized;
using System.Xml.Linq;

namespace AliantWebS.Data
{
    public class DatabaseManagement
    {
        static public string Detach(string connectString)
        { 

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectString);
            // Retrieve the DataSource property.    
            string serverName = builder.DataSource;
            string databaseName = builder.InitialCatalog;

            Server smoServer = new Server(serverName);

            

            Database database = smoServer.Databases[databaseName];

            string path = database.PrimaryFilePath;

            smoServer.DetachDatabase(databaseName, false);

            return path;

        }

        static public void Attach(string connectString)
        {

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectString);
            // Retrieve the DataSource property.    
            string serverName = builder.DataSource;
            string databaseName = builder.InitialCatalog;

            Server smoServer = new Server(serverName);
            StringCollection sc = new StringCollection() { "AliantWeb.mdf", "AliantWeb_log.ldf" };
            smoServer.AttachDatabase(databaseName, sc);
           
        }

        static public void CopyDatabase()
        {
            ServerConnection conn = new ServerConnection("rune\\sql2008");
            Server server = new Server(conn);

            Database newdb = new Database(server, "new database");
            newdb.Create();

            Transfer transfer = new Transfer(server.Databases["source database"]);
            transfer.CopyAllObjects = true;
            transfer.CopyAllUsers = true;
            transfer.Options.WithDependencies = true;
            transfer.DestinationDatabase = newdb.Name;
            transfer.DestinationServer = server.Name;
            transfer.DestinationLoginSecure = true;
            transfer.CopySchema = true;
            transfer.CopyData = true;
            transfer.Options.ContinueScriptingOnError = true;
            transfer.TransferData();
        }


        static public string BackupDatabase(string connectString, string path, string fileName = "AliantWeb.bak")
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectString);
            string serverName = builder.DataSource;
            string databaseName = builder.InitialCatalog;

            // Use SQL auth when not using Windows auth (required for Linux SQL Server containers)
            Server srv;
            if (builder.IntegratedSecurity)
            {
                srv = new Server(serverName);
            }
            else
            {
                var conn = new ServerConnection(serverName, builder.UserID, builder.Password);
                conn.EncryptConnection = false;
                conn.TrustServerCertificate = true;
                srv = new Server(conn);
            }

            Database db = srv.Databases[databaseName];

            int recoverymod = (int)db.DatabaseOptions.RecoveryModel;

            Backup bk = new Backup();
            bk.Action = BackupActionType.Database;
            bk.BackupSetDescription = $"Full backup of {databaseName}";
            bk.BackupSetName = $"{databaseName} Backup";
            bk.Database = databaseName;

            string destinationPath = System.IO.Path.Combine(path, fileName);
            BackupDeviceItem bdi = new BackupDeviceItem(destinationPath, DeviceType.File);
            bk.Devices.Add(bdi);

            bk.Incremental = false;
            bk.ExpirationDate = new System.DateTime(2030, 10, 5);

            bk.SqlBackup(srv);

            Console.WriteLine("Full Backup complete.");
            bk.Devices.Remove(bdi);

            return db.PrimaryFilePath;

            //NT Service\MSSQLSERVER

        }

        static public void Restore(string connectString, string destinationPath)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectString);

            // Connect to MASTER — completely separate from the app DB connection pool
            string masterConnString = new SqlConnectionStringBuilder
            {
                DataSource = builder.DataSource,
                InitialCatalog = "master",           // ← key: not the app DB
                UserID = builder.UserID,
                Password = builder.Password,
                IntegratedSecurity = builder.IntegratedSecurity,
                TrustServerCertificate = true,
                Encrypt = false
            }.ConnectionString;

            using var con = new SqlConnection(masterConnString);
            con.Open();

            string dbName = builder.InitialCatalog;

            // ① Drop all active connections to the app DB only
            using (var cmd = new SqlCommand(
                $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE", con))
                cmd.ExecuteNonQuery();

            try
            {
                // ② Restore
                using var cmd = new SqlCommand(
                    $@"RESTORE DATABASE [{dbName}]
               FROM DISK = @path
               WITH REPLACE, RECOVERY", con);
                cmd.Parameters.AddWithValue("@path", destinationPath);
                cmd.CommandTimeout = 300; // 5 minutes for large DBs
                cmd.ExecuteNonQuery();
            }
            finally
            {
                // ③ Always restore multi-user even on failure
                using var cmd = new SqlCommand(
                    $"ALTER DATABASE [{dbName}] SET MULTI_USER", con);
                cmd.ExecuteNonQuery();
            }

            // ④ Clear EF Core pool so all contexts get fresh connections
            SqlConnection.ClearAllPools();

            Console.WriteLine("Full Database Restore complete.");
        }


    }
}