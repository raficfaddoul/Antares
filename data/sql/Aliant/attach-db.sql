-- Replace 'YourDatabaseName' with the name you want for your database
-- Replace 'YourMDFFileName.mdf' with the name of your MDF file


IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'AliantWeb')
BEGIN
    CREATE DATABASE AliantWeb
    ON (FILENAME = '/var/opt/mssql/data/AliantWeb.mdf'),
    (FILENAME = '/var/opt/mssql/data/AliantWeb.ldf')
    FOR ATTACH;
END

GO



