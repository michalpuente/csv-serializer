import fs from 'node:fs/promises';
import { query } from './db.js';
import { parsePreviewFromBuffer } from './parser.js';
import { buildRowHash, normalizeTextValue, parseAmount, parseDate } from './normalization.js';
import { normalizeTableName, quoteIdentifier } from './sql.js';

type StartImportInput = {
  jobId: number;
  filename: string;
  filePath: string;
  requestedTableName: string;
  replaceExisting: boolean;
};

type DatasetRow = {
  id: number;
};

async function logEvent(jobId: number, level: string, message: string): Promise<void> {
  await query('INSERT INTO import_job_events (job_id, level, message) VALUES ($1, $2, $3)', [jobId, level, message]);
}

async function updateJob(jobId: number, patch: Record<string, unknown>): Promise<void> {
  const keys = Object.keys(patch);
  if (keys.length === 0) return;

  const assignments = keys.map((key, index) => `${key} = $${index + 2}`).join(', ');
  const values = keys.map((key) => patch[key]);
  await query(`UPDATE import_jobs SET ${assignments} WHERE id = $1`, [jobId, ...values]);
}

async function createDynamicTable(tableName: string, headers: ReturnType<typeof parsePreviewFromBuffer>['headers']): Promise<void> {
  const columnDefinitions: string[] = ['id BIGSERIAL PRIMARY KEY', 'dataset_id BIGINT NOT NULL'];

  for (const header of headers) {
    columnDefinitions.push(`${quoteIdentifier(header.key)} TEXT`);
    if (header.isDateLike) {
      columnDefinitions.push(`${quoteIdentifier(`${header.key}_date`)} DATE NULL`);
    }
    if (header.isAmountLike) {
      columnDefinitions.push(`${quoteIdentifier(`${header.key}_num`)} NUMERIC(18,2) NULL`);
    }
  }

  columnDefinitions.push('row_hash TEXT NOT NULL');
  columnDefinitions.push('created_at TIMESTAMPTZ NOT NULL DEFAULT now()');

  await query(`CREATE TABLE IF NOT EXISTS ${quoteIdentifier(tableName)} (${columnDefinitions.join(', ')})`);
  await query(`CREATE UNIQUE INDEX IF NOT EXISTS ${quoteIdentifier(`${tableName}_row_hash_idx`)} ON ${quoteIdentifier(tableName)} (row_hash)`);
}

export async function startImport(input: StartImportInput): Promise<void> {
  try {
    await updateJob(input.jobId, { status: 'processing', file_received: true, progress_percent: 5 });
    await logEvent(input.jobId, 'info', 'File received');

    const tableName = normalizeTableName(input.requestedTableName);
    const buffer = await fs.readFile(input.filePath);
    const preview = parsePreviewFromBuffer(buffer);

    await updateJob(input.jobId, { encoding_detected: true, structure_detected: true, progress_percent: 20, message: 'Structure detected' });
    await logEvent(input.jobId, 'info', `Detected encoding: ${preview.encoding}`);

    if (input.replaceExisting) {
      await query(`DROP TABLE IF EXISTS ${quoteIdentifier(tableName)}`);
      await query('DELETE FROM imported_datasets WHERE table_name = $1', [tableName]);
    } else {
      const existing = await query<{ id: number }>('SELECT id FROM imported_datasets WHERE table_name = $1 LIMIT 1', [tableName]);
      if (existing.length > 0) {
        throw new Error('Target table already exists. Use replace existing option.');
      }
    }

    await createDynamicTable(tableName, preview.headers);

    const insertedDataset = await query<DatasetRow>(
      `INSERT INTO imported_datasets (
        table_name, original_filename, status, detected_encoding, delimiter, header_row_json, started_at
      ) VALUES ($1, $2, 'processing', $3, $4, $5::jsonb, now()) RETURNING id`,
      [tableName, input.filename, preview.encoding, preview.delimiter, JSON.stringify(preview.headers)],
    );

    const datasetId = insertedDataset[0]?.id;
    if (!datasetId) {
      throw new Error('Failed to create dataset');
    }

    await updateJob(input.jobId, { dataset_id: datasetId });

    const lines = preview.normalizedText.split(/\r?\n/).slice(preview.headerLineIndex);
    const csvRows = lines
      .join('\n')
      .split(/\r?\n/)
      .filter((line) => line.trim() && !line.trimStart().startsWith('#Podsumowanie'));

    const dataRows = csvRows.slice(1);
    const total = dataRows.length;
    let scanned = 0;
    let normalized = 0;
    let inserted = 0;
    let duplicates = 0;
    let invalid = 0;

    for (const row of dataRows) {
      scanned += 1;
      const columns = row.split(preview.delimiter);
      if (columns.length < preview.headers.length) {
        invalid += 1;
        continue;
      }

      const values = preview.headers.map((_, idx) => normalizeTextValue(columns[idx] ?? ''));
      const rowHash = buildRowHash(values);
      normalized += 1;

      const insertColumns: string[] = ['dataset_id'];
      const insertValues: unknown[] = [datasetId];
      const placeholders: string[] = ['$1'];
      let valueIndex = 2;

      preview.headers.forEach((header, idx) => {
        const value = values[idx];
        insertColumns.push(quoteIdentifier(header.key));
        insertValues.push(value);
        placeholders.push(`$${valueIndex++}`);

        if (header.isDateLike) {
          insertColumns.push(quoteIdentifier(`${header.key}_date`));
          insertValues.push(parseDate(value));
          placeholders.push(`$${valueIndex++}`);
        }

        if (header.isAmountLike) {
          insertColumns.push(quoteIdentifier(`${header.key}_num`));
          insertValues.push(parseAmount(value));
          placeholders.push(`$${valueIndex++}`);
        }
      });

      insertColumns.push('row_hash');
      insertValues.push(rowHash);
      placeholders.push(`$${valueIndex}`);

      const rows = await query<{ inserted: number }>(
        `INSERT INTO ${quoteIdentifier(tableName)} (${insertColumns.join(', ')})
         VALUES (${placeholders.join(', ')})
         ON CONFLICT (row_hash) DO NOTHING
         RETURNING 1 AS inserted`,
        insertValues,
      );

      if (rows.length === 1) inserted += 1;
      else duplicates += 1;

      if (scanned % 50 === 0 || scanned === total) {
        const percent = total === 0 ? 95 : Math.min(95, Math.floor((scanned / total) * 75) + 20);
        await updateJob(input.jobId, {
          rows_scanned: scanned,
          rows_normalized: normalized,
          rows_inserted: inserted,
          rows_duplicates: duplicates,
          rows_invalid: invalid,
          progress_percent: percent,
        });
      }
    }

    await query(
      `UPDATE imported_datasets SET
       status = 'completed', total_rows = $2, inserted_rows = $3, duplicate_rows = $4, invalid_rows = $5,
       finished_at = now()
       WHERE id = $1`,
      [datasetId, total, inserted, duplicates, invalid],
    );

    await updateJob(input.jobId, {
      status: 'completed',
      message: 'Import finished',
      progress_percent: 100,
      rows_scanned: scanned,
      rows_normalized: normalized,
      rows_inserted: inserted,
      rows_duplicates: duplicates,
      rows_invalid: invalid,
      finished_at: new Date().toISOString(),
    });
    await logEvent(input.jobId, 'info', 'Import finished successfully');
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Unknown error';
    await updateJob(input.jobId, { status: 'failed', error_message: message, progress_percent: 100, finished_at: new Date().toISOString() });
    await logEvent(input.jobId, 'error', message);
  }
}
