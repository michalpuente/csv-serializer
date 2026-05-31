const API_BASE = process.env.REACT_APP_API_URL || '/api';

export async function previewFile(file: File): Promise<any> {
  const form = new FormData();
  form.append('file', file);
  const res = await fetch(`${API_BASE}/uploads`, { method: 'POST', body: form });
  if (!res.ok) throw new Error((await res.json()).error || 'Upload failed');
  return res.json();
}

export async function startImport(file: File, tableName: string, replaceExisting: boolean): Promise<any> {
  const form = new FormData();
  form.append('file', file);
  form.append('tableName', tableName);
  form.append('replaceExisting', String(replaceExisting));
  const res = await fetch(`${API_BASE}/imports`, { method: 'POST', body: form });
  if (!res.ok) throw new Error((await res.json()).error || 'Import failed');
  return res.json();
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
  params: { limit?: number; offset?: number; search?: string; sort?: string }
): Promise<any> {
  const qs = new URLSearchParams();
  if (params.limit) qs.set('limit', String(params.limit));
  if (params.offset) qs.set('offset', String(params.offset));
  if (params.search) qs.set('search', params.search);
  if (params.sort) qs.set('sort', params.sort);
  const res = await fetch(`${API_BASE}/datasets/${id}/rows?${qs}`);
  return res.json();
}

export async function getDatasetSummary(id: number): Promise<any> {
  const res = await fetch(`${API_BASE}/datasets/${id}/summary`);
  return res.json();
}

export function getSummaryCsvUrl(id: number): string {
  return `${API_BASE}/datasets/${id}/summary.csv`;
}
