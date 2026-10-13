using CsvSerializer.Api.Data;
using CsvSerializer.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CsvSerializer.Api.Services;

public class ImportService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ImportService> _logger;

    public ImportService(IServiceProvider services, ILogger<ImportService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task RunImportAsync(long jobId, byte[] fileBytes, string tableName, bool replaceExisting)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.ImportJobs.FindAsync(jobId);
        if (job == null) return;

        try
        {
            job.Status = "processing";
            job.FileReceived = true;
            await db.SaveChangesAsync();
            await LogEvent(db, jobId, "info", "File received, starting import...");

            var parseResult = CsvParserService.Parse(fileBytes, fullParse: true);

            job.EncodingDetected = true;
            await db.SaveChangesAsync();
            await LogEvent(db, jobId, "info", $"Encoding: {parseResult.Encoding}, Delimiter: '{parseResult.Delimiter}'");

            job.StructureDetected = true;
            job.RowsScanned = parseResult.AllRows.Count;
            job.ProgressPercent = 20;
            await db.SaveChangesAsync();
            await LogEvent(db, jobId, "info", $"Headers: {string.Join(", ", parseResult.NormalizedHeaders)}. {parseResult.AllRows.Count} data rows found.");
            await LogEvent(db, jobId, "info",
                $"Table starts at record {parseResult.HeaderLineIndex + 1} ({(parseResult.HasHeader ? "header row" : "no header, generated column names")}); " +
                $"skipped {parseResult.SkippedLeadingRecords} preamble and {parseResult.SkippedTrailingRecords} footer records" +
                (parseResult.MojibakeRepaired ? "; repaired mis-encoded characters" : "") + ".");
            if (parseResult.MismatchedRows > 0)
                await LogEvent(db, jobId, "warning", $"{parseResult.MismatchedRows} rows had a different number of fields than the header (extra values appended to the last column).");
            var typed = parseResult.Columns.Where(c => c.IsDate || c.IsNumeric)
                .Select(c => $"{c.Name}:{(c.IsDate ? "date" : "number")}").ToList();
            if (typed.Count > 0)
                await LogEvent(db, jobId, "info", $"Typed columns: {string.Join(", ", typed)}");

            var safeTableName = SqlIdentifierService.NormalizeTableName(tableName);

            ImportedDataset? dataset = null;
            if (job.DatasetId.HasValue)
            {
                dataset = await db.ImportedDatasets.FindAsync(job.DatasetId.Value);
            }

            if (dataset == null)
            {
                dataset = new ImportedDataset
                {
                    TableName = safeTableName,
                    OriginalFilename = "upload",
                    Status = "processing",
                    DetectedEncoding = parseResult.Encoding,
                    Delimiter = parseResult.Delimiter,
                    HeaderRowJson = parseResult.NormalizedHeaders,
                    TotalRows = parseResult.AllRows.Count,
                    StartedAt = DateTime.UtcNow
                };
                db.ImportedDatasets.Add(dataset);
                await db.SaveChangesAsync();

                job.DatasetId = dataset.Id;
                await db.SaveChangesAsync();
            }
            else
            {
                dataset.TableName = safeTableName;
                dataset.Status = "processing";
                dataset.DetectedEncoding = parseResult.Encoding;
                dataset.Delimiter = parseResult.Delimiter;
                dataset.HeaderRowJson = parseResult.NormalizedHeaders;
                dataset.TotalRows = parseResult.AllRows.Count;
                dataset.StartedAt ??= DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            if (replaceExisting)
            {
                var existing = await db.ImportedDatasets
                    .FirstOrDefaultAsync(d => d.TableName == safeTableName && d.Id != dataset.Id);
                if (existing != null)
                {
                    await DropTableIfExists(db, safeTableName);
                    db.ImportedDatasets.Remove(existing);
                    await db.SaveChangesAsync();
                }
            }

            await CreateDynamicTable(db, safeTableName, parseResult.Columns);
            await LogEvent(db, jobId, "info", $"Table \"{safeTableName}\" created.");

            job.ProgressPercent = 40;
            job.RowsNormalized = parseResult.AllRows.Count;
            await db.SaveChangesAsync();

            int inserted = 0, duplicates = 0, invalid = 0;
            var occurrences = new Dictionary<string, int>();
            var rejections = new List<string>();
            const int batchSize = 500;

            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            for (int start = 0; start < parseResult.AllRows.Count; start += batchSize)
            {
                await using (var tx = await conn.BeginTransactionAsync())
                {
                    int end = Math.Min(start + batchSize, parseResult.AllRows.Count);
                    for (int i = start; i < end; i++)
                    {
                        var row = parseResult.AllRows[i];
                        // Identical rows are legitimate (e.g. two equal card fees on one day); number them instead of dropping.
                        var baseHash = NormalizationService.ComputeRowHash(row);
                        occurrences[baseHash] = occurrences.TryGetValue(baseHash, out var n) ? n + 1 : 0;
                        var rowHash = occurrences[baseHash] == 0 ? baseHash : NormalizationService.ComputeRowHash(row, occurrences[baseHash]);

                        await using var savepoint = new NpgsqlCommand("SAVEPOINT row_sp", conn, tx);
                        await savepoint.ExecuteNonQueryAsync();
                        try
                        {
                            var wasInserted = await InsertRow(conn, tx, safeTableName, parseResult.Columns, row, dataset.Id, rowHash);
                            if (wasInserted) inserted++; else duplicates++;
                        }
                        catch (Exception ex)
                        {
                            await using var rollback = new NpgsqlCommand("ROLLBACK TO SAVEPOINT row_sp", conn, tx);
                            await rollback.ExecuteNonQueryAsync();
                            invalid++;
                            _logger.LogWarning(ex, "Invalid row {Index}", i);
                            if (rejections.Count < 5) rejections.Add($"Row {i + 1} rejected: {ex.Message}");
                        }
                    }
                    await tx.CommitAsync();
                }

                job.RowsInserted = inserted;
                job.RowsDuplicates = duplicates;
                job.RowsInvalid = invalid;
                job.ProgressPercent = 40 + (int)(55.0 * Math.Min(start + batchSize, parseResult.AllRows.Count) / parseResult.AllRows.Count);
                await db.SaveChangesAsync();
            }

            foreach (var rejection in rejections)
                await LogEvent(db, jobId, "warning", rejection);

            job.RowsInserted = inserted;
            job.RowsDuplicates = duplicates;
            job.RowsInvalid = invalid;
            job.ProgressPercent = 100;
            job.Status = "completed";
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            dataset.InsertedRows = inserted;
            dataset.DuplicateRows = duplicates;
            dataset.InvalidRows = invalid;
            dataset.Status = "completed";
            dataset.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await LogEvent(db, jobId, "info",
                $"Import completed: {inserted} inserted, {duplicates} duplicates, {invalid} invalid.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed for job {JobId}", jobId);
            job.Status = "failed";
            job.ErrorMessage = ex.Message;
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await LogEvent(db, jobId, "error", $"Import failed: {ex.Message}");
        }
    }

    private static async Task LogEvent(AppDbContext db, long jobId, string level, string message)
    {
        db.ImportJobEvents.Add(new ImportJobEvent
        {
            JobId = jobId,
            Level = level,
            Message = message
        });
        await db.SaveChangesAsync();
    }

    private static async Task DropTableIfExists(AppDbContext db, string tableName)
    {
        var quotedName = $"\"{tableName}\"";
        await db.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS {quotedName} CASCADE");
    }

    private static async Task CreateDynamicTable(AppDbContext db, string tableName, List<ColumnProfile> columns)
    {
        var quotedName = $"\"{tableName}\"";
        var columnDefs = new List<string>
        {
            "id BIGSERIAL PRIMARY KEY",
            "dataset_id BIGINT NOT NULL"
        };

        // Raw text is always kept; typed shadow columns (_date/_num) are added when the column's values support them.
        foreach (var column in columns)
        {
            columnDefs.Add($"\"{column.Name}\" TEXT");
            if (column.IsDate) columnDefs.Add($"\"{column.Name}_date\" DATE");
            if (column.IsNumeric) columnDefs.Add($"\"{column.Name}_num\" NUMERIC");
        }

        columnDefs.Add("row_hash TEXT NOT NULL UNIQUE");
        columnDefs.Add("created_at TIMESTAMPTZ DEFAULT NOW()");

        var sql = $"CREATE TABLE {quotedName} ({string.Join(", ", columnDefs)})";
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task<bool> InsertRow(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string tableName,
        List<ColumnProfile> columns,
        List<string?> values,
        long datasetId,
        string rowHash)
    {
        var quotedName = $"\"{tableName}\"";
        var names = new List<string> { "dataset_id" };
        var parameters = new List<NpgsqlParameter> { new() { Value = datasetId } };

        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var val = i < values.Count ? values[i] : null;

            names.Add($"\"{column.Name}\"");
            parameters.Add(new NpgsqlParameter { Value = (object?)val ?? DBNull.Value });

            if (column.IsDate)
            {
                var dateVal = NormalizationService.TryParseDate(val);
                names.Add($"\"{column.Name}_date\"");
                parameters.Add(new NpgsqlParameter
                {
                    Value = dateVal != null ? DateOnly.ParseExact(dateVal, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : DBNull.Value,
                    NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Date
                });
            }

            if (column.IsNumeric)
            {
                var numVal = NormalizationService.TryParseAmount(val, column.DecimalSeparator);
                names.Add($"\"{column.Name}_num\"");
                parameters.Add(new NpgsqlParameter
                {
                    Value = (object?)numVal ?? DBNull.Value,
                    NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Numeric
                });
            }
        }

        names.Add("row_hash");
        parameters.Add(new NpgsqlParameter { Value = rowHash });

        var placeholders = Enumerable.Range(1, parameters.Count).Select(n => $"${n}");
        var sql = $"INSERT INTO {quotedName} ({string.Join(", ", names)}) VALUES ({string.Join(", ", placeholders)}) ON CONFLICT (row_hash) DO NOTHING";

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddRange(parameters.ToArray());
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
