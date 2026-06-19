# PII anonymization

Optional anonymization scripts run against the **local target** database right after
import, so production data never lands unmasked in non-production. Production is only ever
read (`ExportBacpac`); these scripts only ever write to the local copy.

Anonymization scripts themselves are application-specific (they reference your schema), so
they live in the application's own repository, not here. This tool only provides the
mechanism to run one during a sync.

## How it runs

- **CLI:** add `-m/--masking-script <path>` to `db2db` or `f2db`.
  ```
  AzureDatabaseDownloader db2db -i "<source>" -o "server=localhost;..." -d MyDb -m path/to/anonymize-MyDb.sql
  ```
- **Interactive/profiles:** add a `maskingScripts` map (database name → .sql path) to the profile.
  ```json
  "maskingScripts": { "MyDb": "path/to/anonymize-MyDb.sql" }
  ```

When a masking script (or a post-import procedure, below) runs on a `db2db` sync, the
intermediate `.bacpac` (which still holds raw production data) is **deleted** afterwards, so
only the sanitized database remains on disk.

> `db2f` (export-to-file) intentionally produces a **raw** bacpac and is not masked — it's a
> straight export. To get a masked file, `db2db` into a scratch DB, then export that.

## Post-import procedures

Instead of (or in addition to) a script file, you can have the tool `EXEC` stored procedures
that already live in the database — handy when the cleanup logic ships with your schema, so
there is no external file/path to distribute. They run **after** any masking script.

- **CLI:** `-p/--post-import-procedures` (`;`-separated).
  ```
  AzureDatabaseDownloader db2db -i "<source>" -o "server=localhost;..." -d MyDb -p "dbo.MyCleanup @Confirm = 1"
  ```
- **Interactive/profiles:** a `postImportProcedures` list on the profile, applied to every
  database that profile syncs.
  ```json
  "postImportProcedures": [ "dbo.MyCleanup @Confirm = 1" ]
  ```

Each entry is run as `EXEC <entry>`, so it may include arguments. As with masking, this only
ever writes to the local target — never the source. (Masking scripts stay keyed per database,
since different databases in a profile may need different anonymization; procedures are a
project-level operation.)

## Recommended script design

- **UPDATE-in-place, not delete** — overwrite PII *values* while keeping primary/foreign keys
  intact, so row counts and the object graph survive and tests still pass.
- **Deterministic** — derive fakes from a stable per-row number so a given row maps to the
  same fake on every refresh.
- **Scrub dense payloads** — NULL free-text / JSON columns that embed unstructured PII;
  column-level masking can't reach inside them.
- Wrap the whole thing in a transaction and run it only against the local target.
