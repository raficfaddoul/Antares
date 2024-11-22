#!/bin/bash
# Wait for SQL Server to start
sleep 300
# Run the SQL script to attach the database
/opt/mssql-tools/bin/sqlcmd -S localhost -U SA -P 'password@12345#' -i /usr/src/app/attach-db.sql  #/var/opt/mssql/data/attach-db.sql
