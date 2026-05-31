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

            var safeTableName = SqlIdentifierService.NormalizeTableName(tableName);

            var dataset = new ImportedDataset
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

            if (replaceExisting)
            {
                var existing = await db.ImportedDatasets
                    .FirstOrDefaultAsync(d => d.TableName == safeTableName);
                if (existing != null)
                {
                    await DropTableIfExists(db, safeTableName);
                    db.ImportedDatasets.Remove(existing);
                    await db.SaveChangesAsync();
                }
            }

            db.ImportedDatasets.Add(dataset);
            await db.SaveChangesAsync();

            job.DatasetId = dataset.Id;
            await db.SaveChangesAsync();

            await CreateDynamicTable(db, safeTableName, parseResult.NormalizedHeaders, parseResult.AllRows);
            await LogEvent(db, jobId, "info", $"Table \"{safeTableName}\" created.");

            job.ProgressPercent = 40;
            job.RowsNormalized = parseResult.AllRows.Count;
            await db.SaveChangesAsync();

            int inserted = 0, duplicates = 0, invalid = 0;

            for (int i = 0; i < parseResult.AllRows.Count; i++)
            {
                var row = parseResult.AllRows[i];
                try
                {
                    var wasInserted = await InsertRow(db, safeTableName, parseResult.NormalizedHeaders, row, dataset.Id);
                    if (wasInserted) inserted++; else duplicates++;
                }
                catch (Exception ex)
                {
                    invalid++;
                    _logger.LogWarning(ex, "Invalid row {Index}", i);
                }

                if (i % 100 == 0)
                {
                    job.RowsInserted = inserted;
                    job.RowsDuplicates = duplicates;
                    job.RowsInvalid = invalid;
                    job.ProgressPercent = 40 + (int)(55.0 * i / parseResult.AllRows.Count);
                    await db.SaveChangesAsync();
                }
            }

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

    private static async Task CreateDynamicTable(AppDbContext db, string tableName, List<string> headers, List<List<string?>> rows)
    {
        var quotedName = $"\"{tableName}\"";
        var columnDefs = new List<string>
        {
            "id BIGSERIAL PRIMARY KEY",
            "dataset_id BIGINT NOT NULL"
        };

        var dateColumns = new HashSet<string>();
        var numColumns = new HashSet<string>();

        foreach (var header in headers)
        {
            columnDefs.Add($"\"{header}\" TEXT");

            var hasDate = rows.Take(20).Any(r =>
            {
                var idx = headers.IndexOf(header);
                return idx < r.Count && NormalizationService.TryParseDate(r[idx]) != null;
            });
            if (hasDate)
            {
                columnDefs.Add($"\"{header}_date\" DATE");
                dateColumns.Add(header);
            }

            var hasNum = rows.Take(20).Any(r =>
            {
                var idx = headers.IndexOf(header);
                return idx < r.Count && NormalizationService.TryParseAmount(r[idx]) != null;
            });
            if (hasNum)
            {
                columnDefs.Add($"\"{header}_num\" NUMERIC(18,2)");
                numColumns.Add(header);
            }
        }

        columnDefs.Add("row_hash TEXT NOT NULL UNIQUE");
        columnDefs.Add("created_at TIMESTAMPTZ DEFAULT NOW()");

        var sql = $"CREATE TABLE {quotedName} ({string.Join(", ", columnDefs)})";
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task<bool> InsertRow(AppDbContext db, string tableName, List<string> headers, List<string?> values, long datasetId)
    {
        var quotedName = $"\"{tableName}\"";
        var columns = new List<string> { "dataset_id" };
        var paramPlaceholders = new List<string> { "$1" };
        var parameters = new List<NpgsqlParameter>
        {
            new() { Value = datasetId }
        };

        int paramIndex = 2;

        for (int i = 0; i < headers.Count; i++)
        {
            var val = i < values.Count ? values[i] : null;

            columns.Add($"\"{headers[i]}\"");
            paramPlaceholders.Add($"${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = (object?)val ?? DBNull.Value });
            paramIndex++;

            var dateVal = NormalizationService.TryParseDate(val);
            if (dateVal != null)
            {
                columns.Add($"\"{headers[i]}_date\"");
                paramPlaceholders.Add($"${paramIndex}");
                parameters.Add(new NpgsqlParameter
                {
                    Value = DateOnly.Parse(dateVal),
                    NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Date
                });
                paramIndex++;
            }

            var numVal = NormalizationService.TryParseAmount(val);
            if (numVal != null)
            {
                columns.Add($"\"{headers[i]}_num\"");
                paramPlaceholders.Add($"${paramIndex}");
                parameters.Add(new NpgsqlParameter { Value = numVal.Value });
                paramIndex++;
            }
        }

        var rowHash = NormalizationService.ComputeRowHash(values);
        columns.Add("row_hash");
        paramPlaceholders.Add($"${paramIndex}");
        parameters.Add(new NpgsqlParameter { Value = rowHash });

        var sql = $"INSERT INTO {quotedName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", paramPlaceholders)}) ON CONFLICT (row_hash) DO NOTHING";

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, (NpgsqlConnection)conn);
        cmd.Parameters.AddRange(parameters.ToArray());
        var affected = await cmd.ExecuteNonQueryAsync();

        return affected > 0;
    }
}
