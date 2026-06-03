using CsvSerializer.Api.Data;
using CsvSerializer.Api.Models;
using CsvSerializer.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CsvSerializer.Api.Controllers;

[ApiController]
[Route("api/uploads")]
public class UploadsController : ControllerBase
{
    private const long MaxFileSize = 50 * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Preview(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        if (file.Length > MaxFileSize)
            return BadRequest(new { error = "File too large (max 50MB)." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".csv" && ext != ".txt")
            return BadRequest(new { error = "Only .csv and .txt files are accepted." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var result = CsvParserService.Parse(bytes);

        return Ok(new
        {
            encoding = result.Encoding,
            delimiter = result.Delimiter,
            headerLineIndex = result.HeaderLineIndex,
            rawHeaders = result.RawHeaders,
            normalizedHeaders = result.NormalizedHeaders,
            sampleRows = result.SampleRows
        });
    }
}
