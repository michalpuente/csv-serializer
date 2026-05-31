using CsvSerializer.Api.Data;
using CsvSerializer.Api.Models;
using CsvSerializer.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CsvSerializer.Api.Controllers;

[ApiController]
[Route("api/imports")]
public class ImportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ImportService _importService;

    public ImportsController(AppDbContext db, ImportService importService)
    {
        _db = db;
        _importService = importService;
    }

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> StartImport(
        IFormFile file,
        [FromForm] string tableName,
        [FromForm] bool replaceExisting = false)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".csv" && ext != ".txt")
            return BadRequest(new { error = "Only .csv and .txt files are accepted." });

        if (string.IsNullOrWhiteSpace(tableName))
            return BadRequest(new { error = "Table name is required." });

        string safeTableName;
        try
        {
            safeTableName = SqlIdentifierService.NormalizeTableName(tableName);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var job = new ImportJob
        {
            Status = "queued",
            Message = "Import queued"
        };
        _db.ImportJobs.Add(job);
        await _db.SaveChangesAsync();

        _ = Task.Run(() => _importService.RunImportAsync(job.Id, bytes, tableName, replaceExisting));

        return StatusCode(202, new { jobId = job.Id, status = "queued" });
    }
}
