# TkCli

Created with `dotnet new tk-cli`: a command-line tool built on System.CommandLine.

- `src/TkCli/Cli.cs`: the commands. Start by replacing `hello` with your first real command.
- `tests/TkCli.Tests`: runs commands in memory and checks their output and exit codes.

```
dotnet test
dotnet run --project src/TkCli -- hello Ada
```

Install it as a command you can run from any folder:

```
dotnet pack src/TkCli -o artifacts
dotnet tool install --global TkCli --add-source ./artifacts
tkcli hello Ada
```

To install a newer build, raise `<Version>` in the .csproj, pack again, and run
`dotnet tool update --global TkCli --add-source ./artifacts`.
