using System.Text;
using CsvSerializer.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CsvSerializer.Api.Controllers;

[ApiController]
[Route("api/datasets")]
public class DatasetsController : ControllerBase
{
    private readonly AppDbContext _db;

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

    [HttpGet("{id:long}/rows")]
    public async Task<IActionResult> GetRows(
        long id,
        [FromQuery] int limit = 30,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null)
    {
        var dataset = await _db.ImportedDatasets.FindAsync(id);
        if (dataset == null) return NotFound(new { error = "Dataset not found." });

        limit = Math.Min(limit, 100);

        var quotedTable = $"\"{dataset.TableName}\"";
        var parameters = new List<NpgsqlParameter>();
        var whereClauses = new List<string>();
        int paramIdx = 1;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchConditions = dataset.HeaderRowJson.Select(h =>
                $"unaccent(lower(COALESCE(\"{h}\", ''))) LIKE unaccent(lower(${paramIdx}))");
            whereClauses.Add($"({string.Join(" OR ", searchConditions)})");
            parameters.Add(new NpgsqlParameter { Value = $"%{search}%" });
            paramIdx++;
        }

        var whereClause = whereClauses.Count > 0 ? $"WHERE {string.Join(" AND ", whereClauses)}" : "";

        var validSort = dataset.HeaderRowJson.Contains(sort) ? $"\"{sort}\"" : "id";
        var orderClause = $"ORDER BY {validSort}";

        var sql = $"SELECT * FROM {quotedTable} {whereClause} {orderClause} LIMIT {limit} OFFSET {offset}";

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        var rows = new List<Dictionary<string, object?>>();
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

        return Ok(new { rows, limit, offset });
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
}
