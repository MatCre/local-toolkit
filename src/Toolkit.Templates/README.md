# Toolkit templates

`dotnet new` templates for the things this toolkit keeps scaffolding, so boilerplate comes from a
template (deterministic, free, instant) instead of an AI call.

| Template | Creates | Example |
|---|---|---|
| `tk-endpoint` | Minimal API resource with built-in validation (400 problem details) and integration tests | `dotnet new tk-endpoint -n Orders --item Order` |
| `tk-store` | Core library + SQLite adapter (Dapper, migrations) + contract tests | `dotnet new tk-store -n Notes --item Note` |
| `tk-cli` | System.CommandLine tool, packable as a global tool, with tests | `dotnet new tk-cli -n Greeter` |

## Install

```
dotnet pack src/Toolkit.Templates -o artifacts
dotnet new install artifacts/LocalToolkit.Templates.0.1.0.nupkg
```

After changing a template, raise `<Version>` in `Toolkit.Templates.csproj`, pack again and install
the new file (`dotnet new install` replaces the older version). `dotnet new uninstall LocalToolkit.Templates`
removes the pack.

## How a template works

Each folder under `content/` is a normal, buildable solution plus `.template.config/template.json`.
`dotnet new` copies it and replaces placeholders:

- `sourceName` (`TkEndpoint`, `TkStore`, `TkCli`) becomes the `-n` name, in file names and code.
  Its lower-case form (`tkendpoint`) becomes the lower-case name, e.g. the route `/orders`.
- The `item` parameter replaces `TkItem` (and `tkitem` via a casing generator), e.g. `--item Order`.

`content/Directory.Build.props` and `content/Directory.Packages.props` stop this repo's build settings
from applying to the template sources, so they build exactly as they will after `dotnet new`. They
aren't packed.

## Check them

```
./src/Toolkit.Templates/test-templates.ps1
```

Generates a project from each template in a temp folder, with a private template list, and runs its tests.
