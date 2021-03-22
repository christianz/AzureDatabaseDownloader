# Azure Database Downloader

Export .bacpac files from Azure databases and imports them to another server (usually localhost). Useful for fetching copies of Azure databases for testing purposes.

Supports exporting/importing multiple databases at a time. Keeps backups of the databases on your local file system.

## Usage

### Interactive mode

#### Copy profiles.sample.json to profiles.json

#### Modify profiles.json

```json
[
  {
    "name": "Test",
    "fromConnectionString": "server=tcp:mydatabase.database.windows.net,1433;database=test_db;uid=test@mydatabase;pwd=MyPassword123;",
    "toConnectionString": "server=localhost;database=master;trusted_connection=true;",
    "workingDirectory":  "C:\\tmp\\Databases", 
    "databasesToSync": [ "test_db" ],
    "localDbUser": "testuser",
    "isActive": true
  },
  (...)
]
```

#### Then run 

```
AzureDatabaseDownloader.exe interactive
```

### Automated mode

#### Sync database(s) from one database server to another
```
AzureDatabaseDownloader db2db -i "server=tcp:mydatabase.database.windows.net,1433;database=test;uid=test@mydatabase;pwd=MyPassword123;" -o "server=localhost;database=master;trusted_connection=true;" -d "test_db"
```

#### Sync a single database from a database server to a local .bacpac file
```
AzureDatabaseDownloader db2f -i "server=tcp:mydatabase.database.windows.net,1433;database=test;uid=test@mydatabase;pwd=MyPassword123;" -o TestDatabase.bacpac -d "test_db"
```

#### Sync a single database from a .bacpac file to a database server
```
AzureDatabaseDownloader f2db -i TestDatabase.bacpac -o "server=tcp:mydatabase.database.windows.net,1433;database=test;uid=test@mydatabase;pwd=MyPassword123;" -d "test_db"
```
