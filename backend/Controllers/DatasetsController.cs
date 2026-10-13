using System.Globalization;
using System.Text;
using CsvSerializer.Api.Data;
using CsvSerializer.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CsvSerializer.Api.Controllers;

[ApiController]
[Route("api/datasets")]
public class DatasetsController : ControllerBase
{
    private readonly AppDbContext _db;

    private static readonly HashSet<string> SortDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "asc", "desc"
    };

    private static readonly (string Find, string Replace)[] DefaultRepairRules =
    [
        ("ďż˝", "ł"),
        ("ï¿½", "ł"),
        ("WP�ATA", "WPŁATA"),
        ("WP�AT�", "WPŁATĘ"),
        ("ZEWN�TRZNY", "ZEWNĘTRZNY"),
        ("WYCHODZ�CY", "WYCHODZĄCY"),
        ("PRZYCHODZ�CY", "PRZYCHODZĄCY"),
        ("OP�ATA", "OPŁATA"),
        ("�YCIE", "ŻYCIE"),
        ("SK�ADKA", "SKŁADKA"),
        ("�RODK�W", "ŚRODKÓW")
    ];

    public DatasetsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var datasets = await _db.ImportedDatasets
            .OrderByDescending(d => d.Id)
            .Take(100)
            .ToListAsync();

        return Ok(datasets.Select(d => new
        {
            id = d.Id,
            tableName = d.TableName,
            originalFilename = d.OriginalFilename,
            status = d.Status,
            totalRows = d.TotalRows,
            insertedRows = d.InsertedRows,
            duplicateRows = d.DuplicateRows,
            invalidRows = d.InvalidRows,
            createdAt = d.CreatedAt
        }));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        return Ok(new
        {
            id = dataset.Id,
            tableName = dataset.TableName,
            originalFilename = dataset.OriginalFilename,
            status = dataset.Status,
            detectedEncoding = dataset.DetectedEncoding,
            delimiter = dataset.Delimiter,
            headerRowJson = dataset.HeaderRowJson,
            totalRows = dataset.TotalRows,
            insertedRows = dataset.InsertedRows,
            duplicateRows = dataset.DuplicateRows,
            invalidRows = dataset.InvalidRows,
            startedAt = dataset.StartedAt,
            finishedAt = dataset.FinishedAt,
            createdAt = dataset.CreatedAt
        });
    }

    /// <summary>Deletes a dataset: its data table, import jobs (with their events) and the catalogue entry.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });
        if (dataset.Status is "queued" or "processing")
            return Conflict(new { error = "Import is still running; wait for it to finish." });

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Another dataset may have re-created the same table (replace import); keep it for that one.
        var tableShared = await _db.ImportedDatasets.AnyAsync(d => d.Id != id && d.TableName == dataset.TableName);
        if (!tableShared)
        {
            var quoted = "\"" + dataset.TableName.Replace("\"", "\"\"") + "\"";
            await _db.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS {quoted}");
        }

        await _db.ImportJobs.Where(j => j.DatasetId == id).ExecuteDeleteAsync();
        _db.ImportedDatasets.Remove(dataset);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return NoContent();
    }

    [HttpGet("{id:long}/rows")]
    public async Task<IActionResult> GetRows(
        long id,
        [FromQuery] int limit = 30,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? direction = "asc",
        [FromQuery] string? textColumn = null,
        [FromQuery] string? textMode = "contains",
        [FromQuery] string? textValue = null,
        [FromQuery] string? dateColumn = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] string? numColumn = null,
        [FromQuery] string? numMin = null,
        [FromQuery] string? numMax = null)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        var tableColumns = await GetTableColumns(dataset.TableName);
        var filters = BuildFilters(
            dataset,
            tableColumns,
            search,
            textColumn,
            textMode,
            textValue,
            dateColumn,
            dateFrom,
            dateTo,
            numColumn,
            numMin,
            numMax);

        if (!filters.Ok)
            return BadRequest(new { error = filters.Error });

        var quotedTable = $"\"{dataset.TableName}\"";
        var whereClause = filters.WhereSql;

        var validSort = GetValidSort(sort, tableColumns);
        var sortDirection = SortDirections.Contains(direction ?? "asc") ? direction : "asc";
        var orderClause = $"ORDER BY {validSort} {sortDirection}";

        var sql = $"SELECT * FROM {quotedTable} {whereClause} {orderClause} LIMIT ${filters.Values.Count + 1} OFFSET ${filters.Values.Count + 2}";
        var countSql = $"SELECT COUNT(*) FROM {quotedTable} {whereClause}";

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParams(cmd, filters.Values);
        cmd.Parameters.Add(new NpgsqlParameter { Value = limit });
        cmd.Parameters.Add(new NpgsqlParameter { Value = offset });

        var rows = new List<Dictionary<string, object?>>();
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }
        }

        await using var countCmd = new NpgsqlCommand(countSql, conn);
        AddParams(countCmd, filters.Values);
        var filteredRows = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        return Ok(new { rows, limit, offset, filteredRows });
    }

    [HttpGet("{id:long}/analytics")]
    public async Task<IActionResult> GetAnalytics(
        long id,
        [FromQuery] string? search = null,
        [FromQuery] string? textColumn = null,
        [FromQuery] string? textMode = "contains",
        [FromQuery] string? textValue = null,
        [FromQuery] string? dateColumn = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] string? numColumn = null,
        [FromQuery] string? numMin = null,
        [FromQuery] string? numMax = null)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        var tableColumns = await GetTableColumns(dataset.TableName);
        var filters = BuildFilters(
            dataset,
            tableColumns,
            search,
            textColumn,
            textMode,
            textValue,
            dateColumn,
            dateFrom,
            dateTo,
            numColumn,
            numMin,
            numMax);

        if (!filters.Ok)
            return BadRequest(new { error = filters.Error });

        var quotedTable = $"\"{dataset.TableName}\"";
        var whereClause = filters.WhereSql;
        var baseColumns = dataset.HeaderRowJson.Where(h => tableColumns.Contains(h)).ToList();
        var dateColumns = tableColumns.Where(c => c.EndsWith("_date", StringComparison.Ordinal)).OrderBy(c => c).ToList();
        var numericColumns = tableColumns.Where(c => c.EndsWith("_num", StringComparison.Ordinal)).OrderBy(c => c).ToList();

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var totalCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {quotedTable}", conn);
        var rowsTotal = Convert.ToInt32(await totalCmd.ExecuteScalarAsync());

        await using var filteredCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {quotedTable} {whereClause}", conn);
        AddParams(filteredCmd, filters.Values);
        var rowsFiltered = Convert.ToInt32(await filteredCmd.ExecuteScalarAsync());

        var columnStats = new List<object>();
        int columnsWithAnyValue = 0;
        foreach (var col in baseColumns)
        {
            var statSql = $"SELECT COUNT(*) FROM {quotedTable} {whereClause} {(string.IsNullOrWhiteSpace(whereClause) ? "WHERE" : "AND")} COALESCE(\"{col}\", '') <> ''";
            await using var statCmd = new NpgsqlCommand(statSql, conn);
            AddParams(statCmd, filters.Values);
            var nonNull = Convert.ToInt32(await statCmd.ExecuteScalarAsync());
            if (nonNull > 0) columnsWithAnyValue++;

            var distinctSql = $"SELECT COUNT(DISTINCT \"{col}\") FROM {quotedTable} {whereClause}";
            await using var distinctCmd = new NpgsqlCommand(distinctSql, conn);
            AddParams(distinctCmd, filters.Values);
            var distinct = Convert.ToInt32(await distinctCmd.ExecuteScalarAsync());

            columnStats.Add(new
            {
                column = col,
                nonNullCount = nonNull,
                distinctCount = distinct,
                fillPercent = rowsFiltered == 0 ? 0 : Math.Round(100.0 * nonNull / rowsFiltered, 2)
            });
        }

        var numericAggregates = new List<object>();
        foreach (var col in numericColumns)
        {
            var aggSql = $"SELECT COUNT(\"{col}\"), COALESCE(SUM(\"{col}\"), 0), COALESCE(AVG(\"{col}\"), 0), MIN(\"{col}\"), MAX(\"{col}\") FROM {quotedTable} {whereClause}";
            await using var aggCmd = new NpgsqlCommand(aggSql, conn);
            AddParams(aggCmd, filters.Values);
            await using var aggReader = await aggCmd.ExecuteReaderAsync();
            if (await aggReader.ReadAsync())
            {
                numericAggregates.Add(new
                {
                    column = col,
                    nonNullCount = aggReader.IsDBNull(0) ? 0 : aggReader.GetInt64(0),
                    sum = aggReader.IsDBNull(1) ? 0 : Convert.ToDecimal(aggReader.GetValue(1)),
                    avg = aggReader.IsDBNull(2) ? 0 : Convert.ToDecimal(aggReader.GetValue(2)),
                    min = aggReader.IsDBNull(3) ? (decimal?)null : Convert.ToDecimal(aggReader.GetValue(3)),
                    max = aggReader.IsDBNull(4) ? (decimal?)null : Convert.ToDecimal(aggReader.GetValue(4))
                });
            }
        }

        return Ok(new
        {
            rowsTotal,
            rowsFiltered,
            columnsTotal = baseColumns.Count,
            columnsWithAnyValue,
            columnsAllNull = baseColumns.Count - columnsWithAnyValue,
            availableDateColumns = dateColumns,
            availableNumericColumns = numericColumns,
            columnStats,
            numericAggregates
        });
    }

    [HttpGet("{id:long}/operation")]
    public async Task<IActionResult> RunOperation(
        long id,
        [FromQuery] string operation,
        [FromQuery] string? column = null,
        [FromQuery] string? search = null,
        [FromQuery] string? textColumn = null,
        [FromQuery] string? textMode = "contains",
        [FromQuery] string? textValue = null,
        [FromQuery] string? dateColumn = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] string? numColumn = null,
        [FromQuery] string? numMin = null,
        [FromQuery] string? numMax = null)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        var tableColumns = await GetTableColumns(dataset.TableName);
        var filters = BuildFilters(
            dataset,
            tableColumns,
            search,
            textColumn,
            textMode,
            textValue,
            dateColumn,
            dateFrom,
            dateTo,
            numColumn,
            numMin,
            numMax);

        if (!filters.Ok)
            return BadRequest(new { error = filters.Error });

        var op = operation.Trim().ToLowerInvariant();
        string selectSql;
        if (op == "count")
        {
            selectSql = "COUNT(*)";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(column) || !tableColumns.Contains(column))
                return BadRequest(new { error = "Invalid column for selected operation." });

            var col = $"\"{column}\"";
            selectSql = op switch
            {
                "sum" => $"COALESCE(SUM({col}), 0)",
                "avg" => $"COALESCE(AVG({col}), 0)",
                "min" => $"MIN({col})",
                "max" => $"MAX({col})",
                "countdistinct" => $"COUNT(DISTINCT {col})",
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(selectSql))
                return BadRequest(new { error = "Unsupported operation." });
        }

        var quotedTable = $"\"{dataset.TableName}\"";
        var sql = $"SELECT {selectSql} FROM {quotedTable} {filters.WhereSql}";

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParams(cmd, filters.Values);
        var result = await cmd.ExecuteScalarAsync();

        return Ok(new
        {
            operation = op,
            column,
            value = result == DBNull.Value ? null : result
        });
    }

    [HttpGet("{id:long}/summary")]
    public async Task<IActionResult> GetSummary(long id)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        return Ok(new
        {
            tableName = dataset.TableName,
            originalFilename = dataset.OriginalFilename,
            encoding = dataset.DetectedEncoding,
            delimiter = dataset.Delimiter,
            headers = dataset.HeaderRowJson,
            totalRows = dataset.TotalRows,
            insertedRows = dataset.InsertedRows,
            duplicateRows = dataset.DuplicateRows,
            invalidRows = dataset.InvalidRows,
            startedAt = dataset.StartedAt,
            finishedAt = dataset.FinishedAt
        });
    }

    [HttpGet("{id:long}/summary.csv")]
    public async Task<IActionResult> GetSummaryCsv(long id)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        var sb = new StringBuilder();
        sb.AppendLine("field,value");
        sb.AppendLine($"table_name,{dataset.TableName}");
        sb.AppendLine($"original_filename,{dataset.OriginalFilename}");
        sb.AppendLine($"encoding,{dataset.DetectedEncoding}");
        sb.AppendLine($"delimiter,\"{dataset.Delimiter}\"");
        sb.AppendLine($"total_rows,{dataset.TotalRows}");
        sb.AppendLine($"inserted_rows,{dataset.InsertedRows}");
        sb.AppendLine($"duplicate_rows,{dataset.DuplicateRows}");
        sb.AppendLine($"invalid_rows,{dataset.InvalidRows}");

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "summary.csv");
    }

    [HttpGet("{id:long}/rows.csv")]
    public async Task<IActionResult> ExportFilteredRowsCsv(
        long id,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? direction = "asc",
        [FromQuery] string? textColumn = null,
        [FromQuery] string? textMode = "contains",
        [FromQuery] string? textValue = null,
        [FromQuery] string? dateColumn = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] string? numColumn = null,
        [FromQuery] string? numMin = null,
        [FromQuery] string? numMax = null)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        var tableColumns = await GetTableColumns(dataset.TableName);
        var filters = BuildFilters(
            dataset,
            tableColumns,
            search,
            textColumn,
            textMode,
            textValue,
            dateColumn,
            dateFrom,
            dateTo,
            numColumn,
            numMin,
            numMax);

        if (!filters.Ok)
            return BadRequest(new { error = filters.Error });

        var quotedTable = $"\"{dataset.TableName}\"";
        var validSort = GetValidSort(sort, tableColumns);
        var sortDirection = SortDirections.Contains(direction ?? "asc") ? direction : "asc";

        var sql = $"SELECT * FROM {quotedTable} {filters.WhereSql} ORDER BY {validSort} {sortDirection}";

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParams(cmd, filters.Values);

        var sb = new StringBuilder();

        await using var reader = await cmd.ExecuteReaderAsync();
        var headers = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToList();

        sb.AppendLine(string.Join(',', headers.Select(EscapeCsv)));

        while (await reader.ReadAsync())
        {
            var values = new List<string>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                values.Add(reader.IsDBNull(i) ? "" : EscapeCsv(Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? ""));
            }

            sb.AppendLine(string.Join(',', values));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"{dataset.TableName}_filtered.csv");
    }

    [HttpGet("{id:long}/repair-suggestions")]
    public async Task<IActionResult> GetRepairSuggestions(long id)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        var tableColumns = await GetTableColumns(dataset.TableName);
        var textColumns = dataset.HeaderRowJson.Where(h => tableColumns.Contains(h)).ToList();
        var quotedTable = $"\"{dataset.TableName}\"";

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var samples = new List<object>();
        foreach (var col in textColumns.Take(4))
        {
                        var sql =
                                $"SELECT \"{col}\", COUNT(*) " +
                                $"FROM {quotedTable} " +
                                $"WHERE \"{col}\" IS NOT NULL " +
                                $"AND (\"{col}\" LIKE '%�%' OR \"{col}\" LIKE '%ďż˝%' OR \"{col}\" LIKE '%ï¿½%') " +
                                $"GROUP BY \"{col}\" " +
                                "ORDER BY COUNT(*) DESC LIMIT 5";

            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                samples.Add(new
                {
                    column = col,
                    value = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    count = reader.GetInt64(1)
                });
            }
        }

        var rules = DefaultRepairRules.Select(r => new { find = r.Find, replace = r.Replace });

        return Ok(new { rules, samples });
    }

    [HttpPost("{id:long}/repair-apply")]
    public async Task<IActionResult> ApplyRepairRule(long id, [FromBody] RepairRequest request)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        if (string.IsNullOrWhiteSpace(request.Column) || string.IsNullOrWhiteSpace(request.Find))
            return BadRequest(new { error = "Column and find text are required." });

        var tableColumns = await GetTableColumns(dataset.TableName);
        if (!tableColumns.Contains(request.Column) || request.Column.EndsWith("_num", StringComparison.Ordinal) || request.Column.EndsWith("_date", StringComparison.Ordinal))
            return BadRequest(new { error = "Invalid repair column." });

        var quotedTable = $"\"{dataset.TableName}\"";
        var sql = $"UPDATE {quotedTable} SET \"{request.Column}\" = REPLACE(\"{request.Column}\", $1, $2) WHERE \"{request.Column}\" LIKE $3";

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = request.Find });
        cmd.Parameters.Add(new NpgsqlParameter { Value = request.ReplaceWith ?? "" });
        cmd.Parameters.Add(new NpgsqlParameter { Value = $"%{request.Find}%" });

        var affected = await cmd.ExecuteNonQueryAsync();
        return Ok(new { affectedRows = affected });
    }

    [HttpPost("{id:long}/repair-apply-defaults")]
    public async Task<IActionResult> ApplyDefaultRepairs(long id)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        var tableColumns = await GetTableColumns(dataset.TableName);
        var textColumns = dataset.HeaderRowJson.Where(h => tableColumns.Contains(h)).ToList();
        if (textColumns.Count == 0)
            return Ok(new { affectedRows = 0 });

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var quotedTable = $"\"{dataset.TableName}\"";
        var affectedRows = 0;

        foreach (var col in textColumns)
        {
            foreach (var rule in DefaultRepairRules)
            {
                var sql = $"UPDATE {quotedTable} SET \"{col}\" = REPLACE(\"{col}\", $1, $2) WHERE \"{col}\" LIKE $3";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.Add(new NpgsqlParameter { Value = rule.Find });
                cmd.Parameters.Add(new NpgsqlParameter { Value = rule.Replace });
                cmd.Parameters.Add(new NpgsqlParameter { Value = $"%{rule.Find}%" });
                affectedRows += await cmd.ExecuteNonQueryAsync();
            }
        }

        return Ok(new { affectedRows });
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    private async Task<HashSet<string>> GetTableColumns(string tableName)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        const string sql = @"SELECT column_name
                             FROM information_schema.columns
                             WHERE table_schema = 'public' AND table_name = $1";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = tableName });

        var cols = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            cols.Add(reader.GetString(0));
        }

        return cols;
    }

    private static string GetValidSort(string? sort, HashSet<string> tableColumns)
    {
        if (!string.IsNullOrWhiteSpace(sort) && tableColumns.Contains(sort))
            return $"\"{sort}\"";

        return "id";
    }

    private static void AddParams(NpgsqlCommand cmd, IEnumerable<object?> values)
    {
        foreach (var value in values)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        }
    }

    private static FilterBuildResult BuildFilters(
        ImportedDataset dataset,
        HashSet<string> tableColumns,
        string? search,
        string? textColumn,
        string? textMode,
        string? textValue,
        string? dateColumn,
        string? dateFrom,
        string? dateTo,
        string? numColumn,
        string? numMin,
        string? numMax)
    {
        var values = new List<object?>();
        var whereClauses = new List<string>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchableColumns = dataset.HeaderRowJson.Where(h => tableColumns.Contains(h)).ToList();
            if (searchableColumns.Count > 0)
            {
                var param = $"${values.Count + 1}";
                var searchConditions = searchableColumns
                    .Select(h => $"COALESCE(\"{h}\", '') ILIKE {param}");
                whereClauses.Add($"({string.Join(" OR ", searchConditions)})");
                values.Add($"%{search}%");
            }
        }

        if (!string.IsNullOrWhiteSpace(textColumn) && !string.IsNullOrWhiteSpace(textValue))
        {
            if (!tableColumns.Contains(textColumn) || textColumn.EndsWith("_num", StringComparison.Ordinal) || textColumn.EndsWith("_date", StringComparison.Ordinal))
                return FilterBuildResult.Fail("Invalid text filter column.");

            var param = $"${values.Count + 1}";
            var mode = (textMode ?? "contains").ToLowerInvariant();
            if (mode == "equals")
            {
                whereClauses.Add($"COALESCE(\"{textColumn}\", '') = {param}");
                values.Add(textValue);
            }
            else if (mode == "startswith")
            {
                whereClauses.Add($"COALESCE(\"{textColumn}\", '') ILIKE {param}");
                values.Add($"{textValue}%");
            }
            else
            {
                whereClauses.Add($"COALESCE(\"{textColumn}\", '') ILIKE {param}");
                values.Add($"%{textValue}%");
            }
        }

        if (!string.IsNullOrWhiteSpace(dateColumn) && (!string.IsNullOrWhiteSpace(dateFrom) || !string.IsNullOrWhiteSpace(dateTo)))
        {
            if (!tableColumns.Contains(dateColumn) || !dateColumn.EndsWith("_date", StringComparison.Ordinal))
                return FilterBuildResult.Fail("Invalid date filter column.");

            if (!string.IsNullOrWhiteSpace(dateFrom))
            {
                if (!DateOnly.TryParse(dateFrom, out var fromDate))
                    return FilterBuildResult.Fail("Invalid dateFrom value.");

                whereClauses.Add($"\"{dateColumn}\" >= ${values.Count + 1}");
                values.Add(fromDate);
            }

            if (!string.IsNullOrWhiteSpace(dateTo))
            {
                if (!DateOnly.TryParse(dateTo, out var toDate))
                    return FilterBuildResult.Fail("Invalid dateTo value.");

                whereClauses.Add($"\"{dateColumn}\" <= ${values.Count + 1}");
                values.Add(toDate);
            }
        }

        if (!string.IsNullOrWhiteSpace(numColumn) && (!string.IsNullOrWhiteSpace(numMin) || !string.IsNullOrWhiteSpace(numMax)))
        {
            if (!tableColumns.Contains(numColumn) || !numColumn.EndsWith("_num", StringComparison.Ordinal))
                return FilterBuildResult.Fail("Invalid numeric filter column.");

            if (!string.IsNullOrWhiteSpace(numMin))
            {
                if (!decimal.TryParse(numMin, NumberStyles.Any, CultureInfo.InvariantCulture, out var minValue))
                    return FilterBuildResult.Fail("Invalid numMin value.");

                whereClauses.Add($"\"{numColumn}\" >= ${values.Count + 1}");
                values.Add(minValue);
            }

            if (!string.IsNullOrWhiteSpace(numMax))
            {
                if (!decimal.TryParse(numMax, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxValue))
                    return FilterBuildResult.Fail("Invalid numMax value.");

                whereClauses.Add($"\"{numColumn}\" <= ${values.Count + 1}");
                values.Add(maxValue);
            }
        }

        var whereSql = whereClauses.Count > 0 ? $"WHERE {string.Join(" AND ", whereClauses)}" : "";
        return FilterBuildResult.Success(whereSql, values);
    }

    private sealed record FilterBuildResult(bool Ok, string WhereSql, List<object?> Values, string? Error)
    {
        public static FilterBuildResult Success(string whereSql, List<object?> values) => new(true, whereSql, values, null);
        public static FilterBuildResult Fail(string error) => new(false, "", new List<object?>(), error);
    }

    public class RepairRequest
    {
        public string Column { get; set; } = "";
        public string Find { get; set; } = "";
        public string ReplaceWith { get; set; } = "";
    }
}
