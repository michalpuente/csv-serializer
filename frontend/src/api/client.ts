const API_BASE = process.env.REACT_APP_API_URL || `${process.env.PUBLIC_URL}/api`;

export type DatasetFilterParams = {
  limit?: number;
  offset?: number;
  search?: string;
  sort?: string;
  direction?: 'asc' | 'desc';
  textColumn?: string;
  textMode?: 'contains' | 'equals' | 'startsWith';
  textValue?: string;
  dateColumn?: string;
  dateFrom?: string;
  dateTo?: string;
  numColumn?: string;
  numMin?: string;
  numMax?: string;
};

function toQuery(params: DatasetFilterParams): URLSearchParams {
  const qs = new URLSearchParams();
  if (params.limit !== undefined) qs.set('limit', String(params.limit));
  if (params.offset !== undefined) qs.set('offset', String(params.offset));
  if (params.search) qs.set('search', params.search);
  if (params.sort) qs.set('sort', params.sort);
  if (params.direction) qs.set('direction', params.direction);
  if (params.textColumn) qs.set('textColumn', params.textColumn);
  if (params.textMode) qs.set('textMode', params.textMode);
  if (params.textValue) qs.set('textValue', params.textValue);
  if (params.dateColumn) qs.set('dateColumn', params.dateColumn);
  if (params.dateFrom) qs.set('dateFrom', params.dateFrom);
  if (params.dateTo) qs.set('dateTo', params.dateTo);
  if (params.numColumn) qs.set('numColumn', params.numColumn);
  if (params.numMin) qs.set('numMin', params.numMin);
  if (params.numMax) qs.set('numMax', params.numMax);
  return qs;
}

async function readJsonSafe(res: Response): Promise<any | null> {
  const text = await res.text();
  if (!text) return null;

  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

async function throwApiError(res: Response, fallbackMessage: string): Promise<never> {
  const payload = await readJsonSafe(res);
  const message = payload?.error || payload?.message || fallbackMessage;
  throw new Error(message);
}

export async function previewFile(file: File): Promise<any> {
  const form = new FormData();
  form.append('file', file);
  const res = await fetch(`${API_BASE}/uploads`, { method: 'POST', body: form });
  if (!res.ok) return throwApiError(res, 'Upload failed');
  return (await readJsonSafe(res)) || {};
}

export async function startImport(file: File, tableName: string, replaceExisting: boolean): Promise<any> {
  const form = new FormData();
  form.append('file', file);
  form.append('tableName', tableName);
  form.append('replaceExisting', String(replaceExisting));
  const res = await fetch(`${API_BASE}/imports`, { method: 'POST', body: form });
  if (!res.ok) return throwApiError(res, 'Import failed');
  return (await readJsonSafe(res)) || {};
}

export async function getJob(jobId: number): Promise<any> {
  const res = await fetch(`${API_BASE}/import-jobs/${jobId}`);
  if (!res.ok) throw new Error('Job not found');
  return res.json();
}

export async function getDatasets(): Promise<any[]> {
  const res = await fetch(`${API_BASE}/datasets`);
  return res.json();
}

export async function getDataset(id: number): Promise<any> {
  const res = await fetch(`${API_BASE}/datasets/${id}`);
  return res.json();
}

export async function getDatasetRows(
  id: number,
  params: DatasetFilterParams
): Promise<any> {
  const qs = toQuery(params);
  const res = await fetch(`${API_BASE}/datasets/${id}/rows?${qs}`);
  if (!res.ok) return throwApiError(res, 'Failed to fetch dataset rows');
  return res.json();
}

export async function getDatasetAnalytics(id: number, params: DatasetFilterParams): Promise<any> {
  const qs = toQuery(params);
  const res = await fetch(`${API_BASE}/datasets/${id}/analytics?${qs}`);
  if (!res.ok) return throwApiError(res, 'Failed to fetch analytics');
  return res.json();
}

export async function runDatasetOperation(
  id: number,
  operation: 'sum' | 'avg' | 'min' | 'max' | 'count' | 'countdistinct',
  column: string | null,
  params: DatasetFilterParams
): Promise<any> {
  const qs = toQuery(params);
  qs.set('operation', operation);
  if (column) qs.set('column', column);
  const res = await fetch(`${API_BASE}/datasets/${id}/operation?${qs}`);
  if (!res.ok) return throwApiError(res, 'Failed to run operation');
  return res.json();
}

export function getFilteredRowsCsvUrl(id: number, params: DatasetFilterParams): string {
  const qs = toQuery(params);
  return `${API_BASE}/datasets/${id}/rows.csv?${qs}`;
}

export async function getRepairSuggestions(id: number): Promise<any> {
  const res = await fetch(`${API_BASE}/datasets/${id}/repair-suggestions`);
  if (!res.ok) return throwApiError(res, 'Failed to load repair suggestions');
  return res.json();
}

export async function applyRepairDefaults(id: number): Promise<any> {
  const res = await fetch(`${API_BASE}/datasets/${id}/repair-apply-defaults`, { method: 'POST' });
  if (!res.ok) return throwApiError(res, 'Failed to apply default repairs');
  return res.json();
}

export async function applyRepairRule(id: number, column: string, find: string, replaceWith: string): Promise<any> {
  const res = await fetch(`${API_BASE}/datasets/${id}/repair-apply`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ column, find, replaceWith })
  });
  if (!res.ok) return throwApiError(res, 'Failed to apply repair rule');
  return res.json();
}

export async function getDatasetSummary(id: number): Promise<any> {
  const res = await fetch(`${API_BASE}/datasets/${id}/summary`);
  return res.json();
}

export function getSummaryCsvUrl(id: number): string {
  return `${API_BASE}/datasets/${id}/summary.csv`;
}
