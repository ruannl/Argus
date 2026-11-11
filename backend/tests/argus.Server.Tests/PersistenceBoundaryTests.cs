namespace argus.Server.Tests;

/// <summary>
/// Production boundary evidence. Every converted store and query builder must
/// execute only through EF; this inventory currently expects no raw production
/// caller at all.
///
/// This is a source inventory guard, not a full semantic detector. It covers
/// the command and COPY spellings the migration used; the Phase 6 inventory
/// must also inspect compiled paths, connection opening and other execution
/// escape hatches.
/// </summary>
public sealed class PersistenceBoundaryTests
{
    [Fact]
    public void ConvertedPersistencePathsDoNotExecuteRawCommands()
    {
        string backend = BackendRoot();
        string[] converted =
        [
            "Data/Store.cs",
            "Data/FinanceStore.cs",
            "Data/SuggestionLoader.cs",
            "Data/StatementImportStore.cs",
            "Data/TransactionStore.cs",
            "Transactions/TransactionQuery.cs",
            "Seed.cs",
        ];

        foreach (string relativePath in converted)
        {
            string source = File.ReadAllText(Path.Combine(backend, relativePath));
            Assert.DoesNotContain("CreateCommand", source, StringComparison.Ordinal);
            Assert.DoesNotContain("NpgsqlCommand", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ExecuteReaderAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ExecuteScalarAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ExecuteNonQueryAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginBinaryImport", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginBinaryExport", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginTextImport", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginRawBinaryCopy", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RawCommandCallersMatchTheDocumentedPhaseThreeList()
    {
        string backend = BackendRoot();
        string[] expected = [];
        string[] actual = Directory
            .EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => ContainsRawCommand(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(backend, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProductionHasNoDirectDatabaseExecutionOrBareMigrationApply()
    {
        string backend = BackendRoot();
        string[] sources = Directory
            .EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        string[] directExecution =
        [
            "new NpgsqlConnection(",
            "NpgsqlCommand(",
            "NpgsqlDataReader",
            "CreateCommand(",
            "BeginBinaryImport",
            "BeginTextImport",
            "GetDbConnection",
        ];
        string[] offenders = sources
            .Where(path =>
            {
                string source = File.ReadAllText(path);
                return directExecution.Any(token => source.Contains(token, StringComparison.Ordinal));
            })
            .Select(path => Path.GetRelativePath(backend, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(offenders);

        string[] migrationApply = sources
            .Where(path =>
            {
                string source = File.ReadAllText(path);
                return source.Contains("Database.MigrateAsync(", StringComparison.Ordinal) ||
                       source.Contains("Database.Migrate(", StringComparison.Ordinal) ||
                       source.Contains(".MigrateAsync()", StringComparison.Ordinal) ||
                       source.Contains(".Migrate();", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(backend, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Data/InitializationCoordinator.cs"], migrationApply);
    }

    private static bool ContainsRawCommand(string source) =>
        source.Contains("CreateCommand", StringComparison.Ordinal) ||
        source.Contains("NpgsqlCommand", StringComparison.Ordinal) ||
        source.Contains("ExecuteReaderAsync", StringComparison.Ordinal) ||
        source.Contains("ExecuteScalarAsync", StringComparison.Ordinal) ||
        source.Contains("ExecuteNonQueryAsync", StringComparison.Ordinal) ||
        source.Contains("ExecuteReader(", StringComparison.Ordinal) ||
        source.Contains("ExecuteScalar(", StringComparison.Ordinal) ||
        source.Contains("ExecuteNonQuery(", StringComparison.Ordinal) ||
        source.Contains("BeginBinaryImport", StringComparison.Ordinal) ||
        source.Contains("BeginBinaryExport", StringComparison.Ordinal) ||
        source.Contains("BeginTextImport", StringComparison.Ordinal) ||
        source.Contains("BeginRawBinaryCopy", StringComparison.Ordinal) ||
        source.Contains("NpgsqlBinaryImporter", StringComparison.Ordinal) ||
        source.Contains("NpgsqlBinaryExporter", StringComparison.Ordinal);

    private static string BackendRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "argus.Server.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Data")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("backend source root could not be located from the test output directory");
    }
}
