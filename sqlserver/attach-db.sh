#!/bin/bash
# Wait for SQL Server to start
sleep 300
# Run the SQL script to attach the database
/opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "$MSSQL_SA_PASSWORD" -i /var/opt/mssql/scripts/attach-db.sql
