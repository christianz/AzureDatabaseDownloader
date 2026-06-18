# PII anonymization

Optional anonymization scripts run against the **local target** database right after
import, so production data never lands unmasked in non-production. Production/Azure is
only ever read (`ExportBacpac`); these scripts only ever write to the local copy.

## How it runs

- **CLI:** add `-m/--masking-script <path>` to `db2db` or `f2db`.
  ```
  AzureDatabaseDownloader db2db -i "<azure>" -o "server=localhost;..." -d checkiris -m masking/CheckIris.sql
  ```
- **Interactive/profiles:** add a `maskingScripts` map (database name → .sql path) to the profile.
  ```json
  "maskingScripts": { "checkiris": "masking/CheckIris.sql" }
  ```

When a masking script runs on a `db2db` sync, the intermediate `.bacpac` (which still
holds raw production data) is **deleted** afterwards, so only the masked database remains
on disk.

> `db2f` (export-to-file) intentionally produces a **raw** bacpac and is not masked — it's
> a straight export. To get a masked file, `db2db` into a scratch DB, then export that.

## Design

- **UPDATE-in-place.** Row counts and all foreign keys are preserved, so tests still
  exercise the real object graph; only PII *values* are overwritten.
- **Deterministic.** Names/emails derive from a stable per-row number, so a given row maps
  to the same fake on every refresh.
- **Dense payloads scrubbed.** Free-text / JSON result columns (`CreditReport.RawJson`,
  `ActivityResult.JsonData`, `ActivityRawPayloads.Payload`, `CandidateCV.ParsedData`,
  signing evidence, NIN, etc.) embed unstructured PII and are NULLed.

## Maintenance

`CheckIris.sql` enumerates PII columns by hand. When the schema changes, update it.
Validate every referenced column still exists before running against real data:

```sql
-- compare the table/column pairs in CheckIris.sql against INFORMATION_SCHEMA.COLUMNS
```

Columns that are product/template content (activity descriptions, instructions, reference
question text, translations) are intentionally **not** masked.

### Known follow-ups
- Result rendering in dev loses realism because result payloads are NULLed. If a test needs
  a realistic rendered result, seed a synthetic fixture rather than relaxing the masking.
- Sections marked `[VERIFY]` in `CheckIris.sql` should be re-checked when the schema drifts.
</content>
