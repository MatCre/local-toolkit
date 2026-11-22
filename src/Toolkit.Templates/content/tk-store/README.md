# TkStore

Created with `dotnet new tk-store`: ports and adapters for storing tkitems.

- `src/TkStore`: the core. `TkItem`, `NewTkItem` (with its validation rules) and `ITkItemStore`.
  No database code or packages.
- `src/TkStore.Sqlite`: the SQLite adapter. Dapper for SQL; `SqliteDatabase` applies schema
  migrations tracked in `PRAGMA user_version` (add new ones to the end, never edit old ones).
- `tests/TkStore.Tests`: `TkItemStoreContractTests` says what any store must do;
  `SqliteTkItemStoreTests` runs it against SQLite.

To support another database, add an adapter project implementing `ITkItemStore` and a subclass of
the contract tests for it.

```
dotnet test
```
