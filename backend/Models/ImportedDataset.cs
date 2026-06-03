namespace CsvSerializer.Api.Models;

public class ImportedDataset
{
    public long Id { get; set; }
    public string TableName { get; set; } = "";
    public string OriginalFilename { get; set; } = "";
    public string Status { get; set; } = "processing";
    public string? DetectedEncoding { get; set; }
    public string? Delimiter { get; set; }
    public List<string> HeaderRowJson { get; set; } = new();
    public int TotalRows { get; set; }
    public int InsertedRows { get; set; }
    public int DuplicateRows { get; set; }
    public int InvalidRows { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
