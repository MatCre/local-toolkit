using Dapper;
using Microsoft.Data.Sqlite;

namespace Toolkit.AI.Storage;

/// <summary>
/// One SQLite file plus the SQL that creates its tables. Opening a connection runs that SQL
/// the first time only, so callers never have to think about whether the tables exist yet.
/// Shared by the call log and the response cache.
/// </summary>
internal sealed class SqliteDatabase(string path, string schemaSql)
{
    private readonly Lock _initLock = new();
    private volatile bool _initialized;

    public string FullPath { get; } = Path.GetFullPath(path);

    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = FullPath }.ToString();

    public SqliteConnection Open()
    {
        EnsureCreated();
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        EnsureCreated(); // runs once, takes milliseconds; not worth an async version
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private void EnsureCreated()
    {
        if (_initialized)
        {
            return;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
            using var connection = new SqliteConnection(ConnectionString);
            connection.Execute(schemaSql);
            _initialized = true;
        }
    }
}
