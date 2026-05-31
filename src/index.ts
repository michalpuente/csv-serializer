import fs from 'node:fs/promises';
import path from 'node:path';
import express from 'express';
import multer from 'multer';
import { config } from './lib/config.js';
import { pool, query } from './lib/db.js';
import { layout, escapeHtml } from './lib/html.js';
import { startImport } from './lib/importer.js';
import { parsePreviewFromBuffer } from './lib/parser.js';
import { normalizeTableName, quoteIdentifier } from './lib/sql.js';

type Dataset = {
  id: number;
  table_name: string;
  original_filename: string;
  status: string;
  created_at: string;
  total_rows: number;
  inserted_rows: number;
  duplicate_rows: number;
  invalid_rows: number;
  header_row_json: Array<{ original: string; key: string; isDateLike: boolean; isAmountLike: boolean }>;
};

await fs.mkdir(path.join(process.cwd(), 'src/uploads'), { recursive: true });

const upload = multer({
  storage: multer.diskStorage({
    destination: (_req, _file, cb) => cb(null, path.join(process.cwd(), 'src/uploads')),
    filename: (_req, file, cb) => cb(null, `${Date.now()}-${file.originalname.replace(/[^a-zA-Z0-9_.-]/g, '_')}`),
  }),
  limits: { fileSize: config.maxUploadBytes },
  fileFilter: (_req, file, cb) => {
    const ok = /\.(csv|txt)$/i.test(file.originalname) || /text|csv|octet-stream/i.test(file.mimetype);
    if (ok) cb(null, true);
    else cb(new Error('Only CSV/TXT files are allowed'));
  },
});

const app = express();
app.use(express.urlencoded({ extended: true }));
app.use(express.json());

app.get('/', (_req, res) => {
  res.send(
    layout(
      'CSV Import',
      `<h1>Import CSV</h1>
<div class="card">
  <form id="import-form" method="post" enctype="multipart/form-data" action="/api/imports">
    <div class="row"><label>File <input type="file" name="file" required /></label></div>
    <div class="row"><label>Target table name <input name="tableName" required /></label></div>
    <div class="row"><label><input type="checkbox" name="replaceExisting" value="true" /> Replace existing table</label></div>
    <div class="row">
      <button type="button" id="preview-btn">Preview structure</button>
      <button type="submit">Start import</button>
    </div>
  </form>
  <pre id="preview" class="muted"></pre>
</div>
<script>
const form = document.getElementById('import-form');
const preview = document.getElementById('preview');

form.addEventListener('submit', async (event) => {
  event.preventDefault();
  const fd = new FormData(form);
  const resp = await fetch('/api/imports', { method: 'POST', body: fd });
  const data = await resp.json();
  if (!resp.ok) { alert(data.error || 'Import failed'); return; }
  location.href = '/jobs/' + data.jobId;
});

document.getElementById('preview-btn').addEventListener('click', async () => {
  const fd = new FormData(form);
  const resp = await fetch('/api/uploads', { method: 'POST', body: fd });
  const data = await resp.json();
  preview.textContent = JSON.stringify(data, null, 2);
});
</script>`,
    ),
  );
});

app.get('/datasets', async (_req, res) => {
  const datasets = await query<Dataset>('SELECT * FROM imported_datasets ORDER BY id DESC LIMIT 100');
  res.send(
    layout(
      'Datasets',
      `<h1>Datasets</h1>
<table>
<thead><tr><th>ID</th><th>Table</th><th>File</th><th>Status</th><th>Rows</th><th>Created</th></tr></thead>
<tbody>
${datasets
  .map(
    (d) => `<tr><td><a href="/datasets/${d.id}">${d.id}</a></td><td>${escapeHtml(d.table_name)}</td><td>${escapeHtml(d.original_filename)}</td><td><span class="badge">${escapeHtml(d.status)}</span></td><td>${d.inserted_rows}/${d.total_rows}</td><td>${escapeHtml(d.created_at)}</td></tr>`,
  )
  .join('')}
</tbody>
</table>`,
    ),
  );
});

app.get('/jobs/:id', async (req, res) => {
  const id = Number(req.params.id);
  const rows = await query<{ id: number }>('SELECT id FROM import_jobs WHERE id = $1', [id]);
  if (!rows[0]) {
    res.status(404).send(layout('Not found', '<p>Job not found.</p>'));
    return;
  }

  res.send(
    layout(
      `Import job ${id}`,
      `<h1>Import job #${id}</h1>
<div class="card">
  <p id="status"></p>
  <progress id="progress" max="100" value="0"></progress>
  <p id="counts" class="muted"></p>
  <ul id="events"></ul>
  <p><a id="dataset-link"></a></p>
</div>
<script>
async function tick() {
  const resp = await fetch('/api/import-jobs/${id}');
  const data = await resp.json();
  document.getElementById('status').textContent = 'Status: ' + data.status + (data.message ? ' — ' + data.message : '');
  document.getElementById('progress').value = data.progress_percent;
  document.getElementById('counts').textContent =
    'scanned=' + data.rows_scanned + ', normalized=' + data.rows_normalized + ', inserted=' + data.rows_inserted + ', duplicates=' + data.rows_duplicates + ', invalid=' + data.rows_invalid;
  document.getElementById('events').innerHTML = (data.events || []).map((e) => '<li>' + e.created_at + ': ' + e.message + '</li>').join('');
  if (data.dataset_id) {
    const a = document.getElementById('dataset-link');
    a.href = '/datasets/' + data.dataset_id;
    a.textContent = 'Open dataset #' + data.dataset_id;
  }
  if (data.status === 'completed' || data.status === 'failed') return;
  setTimeout(tick, 1000);
}
tick();
</script>`,
    ),
  );
});

app.get('/datasets/:id', async (req, res) => {
  const datasetId = Number(req.params.id);
  const dataset = (await query<Dataset>('SELECT * FROM imported_datasets WHERE id = $1', [datasetId]))[0];
  if (!dataset) {
    res.status(404).send(layout('Not found', '<p>Dataset not found.</p>'));
    return;
  }

  const headers = dataset.header_row_json ?? [];
  const q = String(req.query.q ?? '').trim();
  const page = Math.max(1, Number(req.query.page ?? 1));
  const pageSize = 30;
  const offset = (page - 1) * pageSize;

  const where: string[] = ['dataset_id = $1'];
  const values: unknown[] = [datasetId];
  let next = 2;

  if (q) {
    const textCols = headers.map((h) => `unaccent(lower(COALESCE(${quoteIdentifier(h.key)}, '')))`);
    if (textCols.length > 0) {
      where.push(`(${textCols.map((c) => `${c} LIKE unaccent(lower($${next}))`).join(' OR ')})`);
      values.push(`%${q}%`);
      next += 1;
    }
  }

  const sortKeyRaw = String(req.query.sort ?? headers[0]?.key ?? 'id');
  const allowedSort = new Set(['id', ...headers.map((h) => h.key)]);
  const sortKey = allowedSort.has(sortKeyRaw) ? sortKeyRaw : 'id';

  const rows = await query<Record<string, unknown>>(
    `SELECT * FROM ${quoteIdentifier(dataset.table_name)} WHERE ${where.join(' AND ')} ORDER BY ${quoteIdentifier(sortKey)} DESC LIMIT ${pageSize} OFFSET ${offset}`,
    values,
  );

  const tableHeader = ['id', ...headers.map((h) => h.key)].map((h) => `<th>${escapeHtml(h)}</th>`).join('');
  const bodyRows = rows
    .map((row) => `<tr>${['id', ...headers.map((h) => h.key)].map((col) => `<td>${escapeHtml(row[col])}</td>`).join('')}</tr>`)
    .join('');

  res.send(
    layout(
      `Dataset ${dataset.id}`,
      `<h1>Dataset #${dataset.id}</h1>
<div class="card">
  <p><strong>Table:</strong> ${escapeHtml(dataset.table_name)} | <strong>File:</strong> ${escapeHtml(dataset.original_filename)}</p>
  <p><strong>Rows:</strong> total ${dataset.total_rows}, inserted ${dataset.inserted_rows}, duplicates ${dataset.duplicate_rows}, invalid ${dataset.invalid_rows}</p>
  <p><a href="/api/datasets/${dataset.id}/summary" target="_blank">Summary JSON</a> | <a href="/api/datasets/${dataset.id}/summary.csv">Summary CSV</a></p>
</div>
<form class="row" method="get">
  <label>Search <input type="text" name="q" value="${escapeHtml(q)}"/></label>
  <label>Sort <select name="sort">${['id', ...headers.map((h) => h.key)]
    .map((key) => `<option value="${escapeHtml(key)}" ${key === sortKey ? 'selected' : ''}>${escapeHtml(key)}</option>`)
    .join('')}</select></label>
  <button type="submit">Apply</button>
</form>
<table><thead><tr>${tableHeader}</tr></thead><tbody>${bodyRows}</tbody></table>
<p><a href="?q=${encodeURIComponent(q)}&sort=${encodeURIComponent(sortKey)}&page=${page + 1}">Next page</a></p>`,
    ),
  );
});

app.post('/api/uploads', upload.single('file'), async (req, res) => {
  try {
    if (!req.file) {
      res.status(400).json({ error: 'No file uploaded' });
      return;
    }

    const buffer = await fs.readFile(req.file.path);
    const preview = parsePreviewFromBuffer(buffer);
    res.json({
      detected_encoding: preview.encoding,
      delimiter: preview.delimiter,
      headers: preview.headers,
      sample_rows: preview.sampleRows,
      header_row_index: preview.headerLineIndex,
    });
  } catch (error) {
    res.status(400).json({ error: error instanceof Error ? error.message : 'Failed to preview file' });
  }
});

app.post('/api/imports', upload.single('file'), async (req, res) => {
  try {
    if (!req.file) {
      res.status(400).json({ error: 'No file uploaded' });
      return;
    }

    const tableName = normalizeTableName(String(req.body.tableName ?? ''));
    const replaceExisting = String(req.body.replaceExisting ?? '') === 'true';

    const created = await query<{ id: number }>(
      `INSERT INTO import_jobs (status, message, progress_percent) VALUES ('queued', 'Waiting to start', 0) RETURNING id`,
    );
    const jobId = created[0]?.id;
    if (!jobId) {
      throw new Error('Could not create job');
    }

    void startImport({
      jobId,
      filename: req.file.originalname,
      filePath: req.file.path,
      requestedTableName: tableName,
      replaceExisting,
    });

    res.status(202).json({ jobId });
  } catch (error) {
    res.status(400).json({ error: error instanceof Error ? error.message : 'Failed to start import' });
  }
});

app.get('/api/import-jobs/:jobId', async (req, res) => {
  const jobId = Number(req.params.jobId);
  const job = (await query<Record<string, unknown>>('SELECT * FROM import_jobs WHERE id = $1', [jobId]))[0];
  if (!job) {
    res.status(404).json({ error: 'Job not found' });
    return;
  }

  const events = await query<Record<string, unknown>>(
    'SELECT created_at, level, message FROM import_job_events WHERE job_id = $1 ORDER BY id DESC LIMIT 20',
    [jobId],
  );
  res.json({ ...job, events: events.reverse() });
});

app.get('/api/datasets', async (_req, res) => {
  const datasets = await query<Dataset>('SELECT * FROM imported_datasets ORDER BY id DESC');
  res.json(datasets);
});

app.get('/api/datasets/:id', async (req, res) => {
  const dataset = (await query<Dataset>('SELECT * FROM imported_datasets WHERE id = $1', [Number(req.params.id)]))[0];
  if (!dataset) {
    res.status(404).json({ error: 'Dataset not found' });
    return;
  }
  res.json(dataset);
});

app.get('/api/datasets/:id/rows', async (req, res) => {
  const dataset = (await query<Dataset>('SELECT * FROM imported_datasets WHERE id = $1', [Number(req.params.id)]))[0];
  if (!dataset) {
    res.status(404).json({ error: 'Dataset not found' });
    return;
  }

  const limit = Math.min(100, Number(req.query.limit ?? 30));
  const offset = Math.max(0, Number(req.query.offset ?? 0));

  const rows = await query<Record<string, unknown>>(
    `SELECT * FROM ${quoteIdentifier(dataset.table_name)} WHERE dataset_id = $1 ORDER BY id DESC LIMIT ${limit} OFFSET ${offset}`,
    [dataset.id],
  );

  res.json(rows);
});

app.get('/api/datasets/:id/summary', async (req, res) => {
  const dataset = (await query<Dataset>('SELECT * FROM imported_datasets WHERE id = $1', [Number(req.params.id)]))[0];
  if (!dataset) {
    res.status(404).json({ error: 'Dataset not found' });
    return;
  }

  const headers = dataset.header_row_json ?? [];
  const amountColumn = headers.find((h) => h.isAmountLike)?.key;
  const counterpartyColumn = headers.find((h) => /nadawca|odbiorca|counterparty/i.test(h.key))?.key;

  const totals = amountColumn
    ? (
        await query<{ sum: string | null }>(
          `SELECT COALESCE(SUM(${quoteIdentifier(`${amountColumn}_num`)}), 0)::text AS sum FROM ${quoteIdentifier(dataset.table_name)} WHERE dataset_id = $1`,
          [dataset.id],
        )
      )[0]
    : { sum: null };

  const dateRange = headers.some((h) => h.isDateLike)
    ? (
        await query<{ min_date: string | null; max_date: string | null }>(
          `SELECT MIN(v)::text AS min_date, MAX(v)::text AS max_date
           FROM (
             ${headers
               .filter((h) => h.isDateLike)
               .map((h) => `SELECT ${quoteIdentifier(`${h.key}_date`)} AS v FROM ${quoteIdentifier(dataset.table_name)} WHERE dataset_id = $1`)
               .join(' UNION ALL ')}
           ) s`,
          [dataset.id],
        )
      )[0]
    : { min_date: null, max_date: null };

  const topCounterparties = counterpartyColumn
    ? await query<{ name: string; count: string }>(
        `SELECT COALESCE(${quoteIdentifier(counterpartyColumn)}, '(brak)') AS name, COUNT(*)::text AS count
         FROM ${quoteIdentifier(dataset.table_name)}
         WHERE dataset_id = $1
         GROUP BY COALESCE(${quoteIdentifier(counterpartyColumn)}, '(brak)')
         ORDER BY COUNT(*) DESC
         LIMIT 10`,
        [dataset.id],
      )
    : [];

  res.json({
    dataset_name: dataset.table_name,
    source_filename: dataset.original_filename,
    import_date: dataset.created_at,
    total_rows_processed: dataset.total_rows,
    inserted_rows: dataset.inserted_rows,
    duplicate_rows: dataset.duplicate_rows,
    invalid_rows: dataset.invalid_rows,
    detected_headers: headers,
    detected_date_range: dateRange,
    amount_totals: totals,
    top_counterparties: topCounterparties,
  });
});

app.get('/api/datasets/:id/summary.csv', async (req, res) => {
  const response = await fetch(`http://127.0.0.1:${config.port}/api/datasets/${req.params.id}/summary`);
  if (!response.ok) {
    res.status(response.status).send(await response.text());
    return;
  }

  const summary = (await response.json()) as Record<string, unknown>;
  const rows = [
    ['metric', 'value'],
    ['dataset_name', summary.dataset_name],
    ['source_filename', summary.source_filename],
    ['import_date', summary.import_date],
    ['total_rows_processed', summary.total_rows_processed],
    ['inserted_rows', summary.inserted_rows],
    ['duplicate_rows', summary.duplicate_rows],
    ['invalid_rows', summary.invalid_rows],
    ['date_range', JSON.stringify(summary.detected_date_range)],
    ['amount_totals', JSON.stringify(summary.amount_totals)],
    ['top_counterparties', JSON.stringify(summary.top_counterparties)],
  ];

  const csv = rows.map((r) => r.map((v) => `"${String(v ?? '').replaceAll('"', '""')}"`).join(',')).join('\n');
  res.setHeader('content-type', 'text/csv; charset=utf-8');
  res.setHeader('content-disposition', `attachment; filename="dataset-${req.params.id}-summary.csv"`);
  res.send(csv);
});

app.use((err: Error, _req: express.Request, res: express.Response, _next: express.NextFunction) => {
  res.status(400).json({ error: err.message });
});

app.listen(config.port, () => {
  console.log(`Server ready on :${config.port}`);
});

process.on('SIGTERM', async () => {
  await pool.end();
  process.exit(0);
});
