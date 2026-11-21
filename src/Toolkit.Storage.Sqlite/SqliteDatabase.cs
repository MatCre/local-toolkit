using Dapper;
using Microsoft.Data.Sqlite;

namespace Toolkit.Storage.Sqlite;

/// <summary>
/// One SQLite file plus the ordered list of migrations that build its schema. The first time a
/// connection is opened, any migrations the file hasn't had yet are applied, so callers never
/// have to think about whether the tables exist or are up to date.
/// </summary>
/// <remarks>
/// <para>
/// <b>How migrations work.</b> SQLite keeps a free integer in every database file,
/// <c>PRAGMA user_version</c>, which starts at 0. Here it records how many migrations have been
/// applied. On open, migrations from that number onwards run in order, each in its own transaction
/// together with the version bump, so a failed migration leaves the file as it was.
/// </para>
/// <para>
/// <b>Rules for writing them.</b> Never edit a migration that has shipped: a file that already
/// has it won't run it again. Add a new one to the end instead. The first migration of each store
/// uses CREATE ... IF NOT EXISTS, so files created before migrations existed (version 0, tables
/// already there) are adopted safely.
/// </para>
/// </remarks>
public sealed class SqliteDatabase(string path, params string[] migrations)
{
    private readonly Lock _initLock = new();
    private volatile bool _initialized;

    public string FullPath { get; } = Path.GetFullPath(path);

    /// <summary>How many migrations this code knows about: the version a file ends up at.</summary>
    public int LatestVersion => migrations.Length;

    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = FullPath }.ToString();

    public SqliteConnection Open()
    {
        EnsureMigrated();
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        EnsureMigrated(); // runs once per process and usually does nothing; not worth an async version
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private void EnsureMigrated()
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
            connection.Open();

            var version = connection.ExecuteScalar<long>("PRAGMA user_version;");
            if (version > migrations.Length)
            {
                throw new InvalidOperationException(
                    $"{FullPath} is at schema version {version}, newer than this program knows ({migrations.Length}). " +
                    "Update the program before opening this file.");
            }

            for (var next = (int)version; next < migrations.Length; next++)
            {
                using var transaction = connection.BeginTransaction();
                connection.Execute(migrations[next], transaction: transaction);
                connection.Execute($"PRAGMA user_version = {next + 1};", transaction: transaction);
                transaction.Commit();
            }

            _initialized = true;
        }
    }
}
