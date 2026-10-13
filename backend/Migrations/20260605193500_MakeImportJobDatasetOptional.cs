using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CsvSerializer.Api.Migrations
{
    public partial class MakeImportJobDatasetOptional : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_import_jobs_imported_datasets_dataset_id",
                table: "import_jobs");

            migrationBuilder.AlterColumn<long>(
                name: "dataset_id",
                table: "import_jobs",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "FK_import_jobs_imported_datasets_dataset_id",
                table: "import_jobs",
                column: "dataset_id",
                principalTable: "imported_datasets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_import_jobs_imported_datasets_dataset_id",
                table: "import_jobs");

            migrationBuilder.Sql("DELETE FROM import_jobs WHERE dataset_id IS NULL;");

            migrationBuilder.AlterColumn<long>(
                name: "dataset_id",
                table: "import_jobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_import_jobs_imported_datasets_dataset_id",
                table: "import_jobs",
                column: "dataset_id",
                principalTable: "imported_datasets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
