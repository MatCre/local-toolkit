# TkEndpoint

Created with `dotnet new tk-endpoint`.

- `src/TkEndpoint.Api`: a minimal API with one resource at `/tkendpoint` (list, get, create, delete).
  Validation rules are attributes on `CreateTkItemRequest`; `AddValidation()` enforces them and
  returns `400` with an error per field. Data is kept in memory by `TkItemStore`.
- `tests/TkEndpoint.Tests`: integration tests that run the real app in memory.

```
dotnet test
dotnet run --project src/TkEndpoint.Api
```
