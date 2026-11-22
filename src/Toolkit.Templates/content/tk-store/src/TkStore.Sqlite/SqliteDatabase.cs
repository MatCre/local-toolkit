using Dapper;
using Microsoft.Data.Sqlite;

namespace TkStore.Sqlite;

/// <summary>
/// One SQLite file plus its ordered schema migrations. The first time a connection is opened, any
/// migrations the file hasn't had yet run in order, each in a transaction with a bump of SQLite's
/// <c>PRAGMA user_version</c> counter.
/// </summary>
/// <remarks>Never edit a migration that has shipped; add a new one to the end.</remarks>
public sealed class SqliteDatabase(string path, params string[] migrations)
{
    private readonly Lock _initLock = new();
    private volatile bool _migrated;

    public string FullPath { get; } = Path.GetFullPath(path);

    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = FullPath }.ToString();

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        EnsureMigrated();
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private void EnsureMigrated()
    {
        if (_migrated)
        {
            return;
        }

        lock (_initLock)
        {
            if (_migrated)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var version = connection.ExecuteScalar<long>("PRAGMA user_version;");
            if (version > migrations.Length)
            {
                throw new InvalidOperationException(
                    $"{FullPath} is at schema version {version}, newer than this program knows ({migrations.Length}).");
            }

            for (var next = (int)version; next < migrations.Length; next++)
            {
                using var transaction = connection.BeginTransaction();
                connection.Execute(migrations[next], transaction: transaction);
                connection.Execute($"PRAGMA user_version = {next + 1};", transaction: transaction);
                transaction.Commit();
            }

            _migrated = true;
        }
    }
}
