# Azure Database Downloader

Export .bacpac files from Azure databases and imports them to another server (usually localhost). Useful for fetching copies of Azure databases for testing purposes.

Supports exporting/importing multiple databases at a time. Keeps backups of the databases on your local file system.

The destination database can be named differently from the source, e.g. `KonstaliManagement` on Azure restored as `KonstaliDevelopment_Today` locally.

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
    "isActive": true,
    "excludeTables": [ "dbo.Tables", "dbo.To", "dbo.Exclude", "(must include schema)" ],
  },
  (...)
]
```

##### Restoring under a different name

Each entry in `databasesToSync` is either a plain name (same name on both sides) or a
source-to-destination mapping. Both of these restore `KonstaliManagement` as
`KonstaliDevelopment_Today` on the destination server:

```json
"databasesToSync": [
  "KonstaliManagement:KonstaliDevelopment_Today",
  { "source": "KonstaliManagement", "destination": "KonstaliDevelopment_Today" }
]
```

Use the object form if a database name contains a `:`. The `.bacpac` kept in `workingDirectory`
is always named after the source database.

#### Then run 

```
AzureDatabaseDownloader.exe interactive
```

### Automated mode

#### Sync database(s) from one database server to another
```
AzureDatabaseDownloader db2db -i "server=tcp:mydatabase.database.windows.net,1433;database=test;uid=test@mydatabase;pwd=MyPassword123;" -o "server=localhost;database=master;trusted_connection=true;" -d "test_db"
```

Append `:destination` to any name in `-d` to restore it under a different name. Here `test_db` keeps
its name while `KonstaliManagement` is restored as `KonstaliDevelopment_Today`:

```
AzureDatabaseDownloader db2db -i "..." -o "..." -d "test_db,KonstaliManagement:KonstaliDevelopment_Today"
```

#### Sync a single database from a database server to a local .bacpac file
```
AzureDatabaseDownloader db2f -i "server=tcp:mydatabase.database.windows.net,1433;database=test;uid=test@mydatabase;pwd=MyPassword123;" -o TestDatabase.bacpac -d "test_db"
```

#### Sync a single database from a .bacpac file to a database server
```
AzureDatabaseDownloader f2db -i TestDatabase.bacpac -o "server=tcp:mydatabase.database.windows.net,1433;database=test;uid=test@mydatabase;pwd=MyPassword123;" -d "test_db"
```

`-d` is the name of the database created on the output server — it does not have to match the
database the `.bacpac` was exported from.

## PII anonymization

Pass `-m/--masking-script <path>` to `db2db`/`f2db` (or a `maskingScripts` map in a profile)
to anonymize PII in the local copy immediately after import — production data never lands
unmasked in non-prod. See [masking/README.md](masking/README.md).

A `maskingScripts` entry may be keyed by either the source or the destination database name;
the source name is checked first.
