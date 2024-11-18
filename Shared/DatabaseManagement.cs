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


        static public string BackupDatabase(string connectString,string path)
        {
    
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectString);
                // Retrieve the DataSource property.    
                string serverName = builder.DataSource;


                Server srv = new Server(serverName);
                // Reference the AliantWeb database.   
                Database db = default(Database);
                db = srv.Databases["AliantWeb"];

        

                // Store the current recovery model in a variable.   
                int recoverymod;
                recoverymod = (int)db.DatabaseOptions.RecoveryModel;

                // Define a Backup object variable.   
                Backup bk = new Backup();

                // Specify the type of backup, the description, the name, and the database to be backed up.   
                bk.Action = BackupActionType.Database;
                bk.BackupSetDescription = "Full backup of AliantWeb";
                bk.BackupSetName = "AliantWeb Backup";
                bk.Database = "AliantWeb";

                // Declare a BackupDeviceItem by supplying the backup device file name in the constructor, and the type of device is a file.   
                BackupDeviceItem bdi = default(BackupDeviceItem);

              string destinationPath = System.IO.Path.Combine(path, "AliantWeb.bak");
            bdi = new BackupDeviceItem(destinationPath, DeviceType.File);



                // Add the device to the Backup object.   
                bk.Devices.Add(bdi);
                // Set the Incremental property to False to specify that this is a full database backup.   
                bk.Incremental = false;

           // Set the expiration date.   
                System.DateTime backupdate = new System.DateTime();
            backupdate = new System.DateTime(2030, 10, 5);
            bk.ExpirationDate = backupdate;

            // Specify that the log must be truncated after the backup is complete.   
            //bk.LogTruncation = BackupTruncateLogType.Truncate;

            // Run SqlBackup to perform the full database backup on the instance of SQL Server.   
            bk.SqlBackup(srv);

                // Inform the user that the backup has been completed.   
                System.Console.WriteLine("Full Backup complete.");

                // Remove the backup device from the Backup object.   
                bk.Devices.Remove(bdi);

            return db.PrimaryFilePath;

            //NT Service\MSSQLSERVER

        }

        static public void Restore(string connectString, string destinationPath)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectString);
            // Retrieve the DataSource property.    
            string serverName = builder.DataSource;


            Server srv = new Server(serverName);


            BackupDeviceItem bdi = default(BackupDeviceItem);
           // destinationPath = System.IO.Path.Combine(destinationPath, "AliantWeb" + ".bak");
            bdi = new BackupDeviceItem(destinationPath, DeviceType.File);
            


            Restore rs = new Restore();

            // Set the NoRecovery property to true, so the transactions are not recovered.   
            rs.NoRecovery = false;

            // Add the device that contains the full database backup to the Restore object.   
            rs.Devices.Add(bdi);

            // Specify the database name.   
            rs.Database = "AliantWeb";
           

            // Restore the full database backup with no recovery.   
            rs.SqlRestore(srv);

            rs.Devices.Remove(bdi);

            // Inform the user that the Full Database Restore is complete.   
            Console.WriteLine("Full Database Restore complete.");


            return;

        }


    }
}