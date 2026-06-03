using CsvSerializer.Api.Data;
using CsvSerializer.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CsvSerializer.Api.Controllers;

[ApiController]
[Route("api/import-jobs")]
public class ImportJobsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ImportJobsController(AppDbContext db) => _db = db;

    [HttpGet("{jobId:long}")]
    public async Task<IActionResult> GetJob(long jobId)
    {
        var job = await _db.ImportJobs
            .Include(j => j.Events.OrderByDescending(e => e.CreatedAt).Take(20))
            .FirstOrDefaultAsync(j => j.Id == jobId);

        if (job == null)
            return NotFound(new { error = "Job not found." });

        return Ok(new
        {
            id = job.Id,
            datasetId = job.DatasetId,
            status = job.Status,
            message = job.Message,
            progressPercent = job.ProgressPercent,
            fileReceived = job.FileReceived,
            encodingDetected = job.EncodingDetected,
            structureDetected = job.StructureDetected,
            rowsScanned = job.RowsScanned,
            rowsNormalized = job.RowsNormalized,
            rowsInserted = job.RowsInserted,
            rowsDuplicates = job.RowsDuplicates,
            rowsInvalid = job.RowsInvalid,
            errorMessage = job.ErrorMessage,
            createdAt = job.CreatedAt,
            updatedAt = job.UpdatedAt,
            finishedAt = job.FinishedAt,
            events = job.Events.Select(e => new
            {
                id = e.Id,
                level = e.Level,
                message = e.Message,
                createdAt = e.CreatedAt
            })
        });
    }
}
