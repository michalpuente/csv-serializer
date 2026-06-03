using Microsoft.EntityFrameworkCore;
using CsvSerializer.Api.Models;

namespace CsvSerializer.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ImportedDataset> ImportedDatasets => Set<ImportedDataset>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportJobEvent> ImportJobEvents => Set<ImportJobEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportedDataset>(e =>
        {
            e.ToTable("imported_datasets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TableName).HasColumnName("table_name");
            e.Property(x => x.OriginalFilename).HasColumnName("original_filename");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.DetectedEncoding).HasColumnName("detected_encoding");
            e.Property(x => x.Delimiter).HasColumnName("delimiter");
            e.Property(x => x.HeaderRowJson).HasColumnName("header_row_json")
                .HasColumnType("jsonb");
            e.Property(x => x.TotalRows).HasColumnName("total_rows");
            e.Property(x => x.InsertedRows).HasColumnName("inserted_rows");
            e.Property(x => x.DuplicateRows).HasColumnName("duplicate_rows");
            e.Property(x => x.InvalidRows).HasColumnName("invalid_rows");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.FinishedAt).HasColumnName("finished_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.TableName).IsUnique();
        });

        modelBuilder.Entity<ImportJob>(e =>
        {
            e.ToTable("import_jobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DatasetId).HasColumnName("dataset_id");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.ProgressPercent).HasColumnName("progress_percent");
            e.Property(x => x.FileReceived).HasColumnName("file_received");
            e.Property(x => x.EncodingDetected).HasColumnName("encoding_detected");
            e.Property(x => x.StructureDetected).HasColumnName("structure_detected");
            e.Property(x => x.RowsScanned).HasColumnName("rows_scanned");
            e.Property(x => x.RowsNormalized).HasColumnName("rows_normalized");
            e.Property(x => x.RowsInserted).HasColumnName("rows_inserted");
            e.Property(x => x.RowsDuplicates).HasColumnName("rows_duplicates");
            e.Property(x => x.RowsInvalid).HasColumnName("rows_invalid");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.FinishedAt).HasColumnName("finished_at");
            e.HasOne(x => x.Dataset)
                .WithMany()
                .HasForeignKey(x => x.DatasetId);
        });

        modelBuilder.Entity<ImportJobEvent>(e =>
        {
            e.ToTable("import_job_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.JobId).HasColumnName("job_id");
            e.Property(x => x.Level).HasColumnName("level");
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Job)
                .WithMany(j => j.Events)
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
