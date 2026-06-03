using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CsvSerializer.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "imported_datasets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    table_name = table.Column<string>(type: "text", nullable: false),
                    original_filename = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    detected_encoding = table.Column<string>(type: "text", nullable: true),
                    delimiter = table.Column<string>(type: "text", nullable: true),
                    header_row_json = table.Column<List<string>>(type: "jsonb", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    inserted_rows = table.Column<int>(type: "integer", nullable: false),
                    duplicate_rows = table.Column<int>(type: "integer", nullable: false),
                    invalid_rows = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imported_datasets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dataset_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    progress_percent = table.Column<int>(type: "integer", nullable: false),
                    file_received = table.Column<bool>(type: "boolean", nullable: false),
                    encoding_detected = table.Column<bool>(type: "boolean", nullable: false),
                    structure_detected = table.Column<bool>(type: "boolean", nullable: false),
                    rows_scanned = table.Column<int>(type: "integer", nullable: false),
                    rows_normalized = table.Column<int>(type: "integer", nullable: false),
                    rows_inserted = table.Column<int>(type: "integer", nullable: false),
                    rows_duplicates = table.Column<int>(type: "integer", nullable: false),
                    rows_invalid = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_jobs_imported_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "imported_datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_job_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_job_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_job_events_import_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "import_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_job_events_job_id",
                table: "import_job_events",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_jobs_dataset_id",
                table: "import_jobs",
                column: "dataset_id");

            migrationBuilder.CreateIndex(
                name: "IX_imported_datasets_table_name",
                table: "imported_datasets",
                column: "table_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_job_events");

            migrationBuilder.DropTable(
                name: "import_jobs");

            migrationBuilder.DropTable(
                name: "imported_datasets");
        }
    }
}
