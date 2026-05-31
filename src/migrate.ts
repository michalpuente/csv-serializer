import { pool } from './lib/db.js';

async function migrate(): Promise<void> {
  await pool.query('CREATE EXTENSION IF NOT EXISTS unaccent');

  await pool.query(`
    CREATE TABLE IF NOT EXISTS imported_datasets (
      id BIGSERIAL PRIMARY KEY,
      table_name TEXT NOT NULL UNIQUE,
      original_filename TEXT NOT NULL,
      status TEXT NOT NULL,
      detected_encoding TEXT,
      delimiter TEXT,
      header_row_json JSONB,
      total_rows INTEGER NOT NULL DEFAULT 0,
      inserted_rows INTEGER NOT NULL DEFAULT 0,
      duplicate_rows INTEGER NOT NULL DEFAULT 0,
      invalid_rows INTEGER NOT NULL DEFAULT 0,
      started_at TIMESTAMPTZ,
      finished_at TIMESTAMPTZ,
      created_at TIMESTAMPTZ NOT NULL DEFAULT now()
    );
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS import_jobs (
      id BIGSERIAL PRIMARY KEY,
      dataset_id BIGINT REFERENCES imported_datasets(id) ON DELETE SET NULL,
      status TEXT NOT NULL,
      message TEXT,
      progress_percent INTEGER NOT NULL DEFAULT 0,
      file_received BOOLEAN NOT NULL DEFAULT FALSE,
      encoding_detected BOOLEAN NOT NULL DEFAULT FALSE,
      structure_detected BOOLEAN NOT NULL DEFAULT FALSE,
      rows_scanned INTEGER NOT NULL DEFAULT 0,
      rows_normalized INTEGER NOT NULL DEFAULT 0,
      rows_inserted INTEGER NOT NULL DEFAULT 0,
      rows_duplicates INTEGER NOT NULL DEFAULT 0,
      rows_invalid INTEGER NOT NULL DEFAULT 0,
      error_message TEXT,
      created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
      updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
      finished_at TIMESTAMPTZ
    );
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS import_job_events (
      id BIGSERIAL PRIMARY KEY,
      job_id BIGINT NOT NULL REFERENCES import_jobs(id) ON DELETE CASCADE,
      level TEXT NOT NULL,
      message TEXT NOT NULL,
      created_at TIMESTAMPTZ NOT NULL DEFAULT now()
    );
  `);

  await pool.query(`
    CREATE OR REPLACE FUNCTION touch_import_jobs_updated_at()
    RETURNS TRIGGER AS $$
    BEGIN
      NEW.updated_at = now();
      RETURN NEW;
    END;
    $$ LANGUAGE plpgsql;
  `);

  await pool.query(`
    DROP TRIGGER IF EXISTS trg_import_jobs_updated_at ON import_jobs;
    CREATE TRIGGER trg_import_jobs_updated_at
    BEFORE UPDATE ON import_jobs
    FOR EACH ROW EXECUTE FUNCTION touch_import_jobs_updated_at();
  `);
}

migrate()
  .then(async () => {
    console.log('Migrations finished');
    await pool.end();
  })
  .catch(async (error) => {
    console.error(error);
    await pool.end();
    process.exit(1);
  });
