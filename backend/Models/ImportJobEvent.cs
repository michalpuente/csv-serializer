namespace CsvSerializer.Api.Models;

public class ImportJobEvent
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public ImportJob? Job { get; set; }
    public string Level { get; set; } = "info";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
