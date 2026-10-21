#!/bin/bash
# Wait for SQL Server to start and listen on port 1433
sleep 30s

# Run your SQL script
/opt/mssql-tools/bin/sqlcmd -S localhost -U SA -P "$MSSQL_SA_PASSWORD" -i /var/opt/mssql/data/list-databases.sql
