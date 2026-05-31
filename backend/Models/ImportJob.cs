namespace CsvSerializer.Api.Models;

public class ImportJob
{
    public long Id { get; set; }
    public long DatasetId { get; set; }
    public ImportedDataset? Dataset { get; set; }
    public string Status { get; set; } = "queued";
    public string? Message { get; set; }
    public int ProgressPercent { get; set; }
    public bool FileReceived { get; set; }
    public bool EncodingDetected { get; set; }
    public bool StructureDetected { get; set; }
    public int RowsScanned { get; set; }
    public int RowsNormalized { get; set; }
    public int RowsInserted { get; set; }
    public int RowsDuplicates { get; set; }
    public int RowsInvalid { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public List<ImportJobEvent> Events { get; set; } = new();
}
