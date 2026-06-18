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

When a masking script runs on a `db2db` sync, the intermediate `.bacpac` (which still holds
raw production data) is **deleted** afterwards, so only the masked database remains on disk.

> `db2f` (export-to-file) intentionally produces a **raw** bacpac and is not masked — it's a
> straight export. To get a masked file, `db2db` into a scratch DB, then export that.

## Recommended script design

- **UPDATE-in-place, not delete** — overwrite PII *values* while keeping primary/foreign keys
  intact, so row counts and the object graph survive and tests still pass.
- **Deterministic** — derive fakes from a stable per-row number so a given row maps to the
  same fake on every refresh.
- **Scrub dense payloads** — NULL free-text / JSON columns that embed unstructured PII;
  column-level masking can't reach inside them.
- Wrap the whole thing in a transaction and run it only against the local target.
