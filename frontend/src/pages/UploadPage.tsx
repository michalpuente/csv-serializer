import React, { useState, FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { previewFile, startImport } from '../api/client';

export default function UploadPage() {
  const navigate = useNavigate();
  const [file, setFile] = useState<File | null>(null);
  const [tableName, setTableName] = useState('');
  const [replaceExisting, setReplaceExisting] = useState(false);
  const [preview, setPreview] = useState<any>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handlePreview = async () => {
    if (!file) return;
    setLoading(true);
    setError('');
    try {
      const result = await previewFile(file);
      setPreview(result);
    } catch (e: any) {
      setError(e.message);
    }
    setLoading(false);
  };

  const handleImport = async (e: FormEvent) => {
    e.preventDefault();
    if (!file || !tableName) return;
    setLoading(true);
    setError('');
    try {
      const result = await startImport(file, tableName, replaceExisting);
      navigate(`/jobs/${result.jobId}`);
    } catch (e: any) {
      setError(e.message);
    }
    setLoading(false);
  };

  return (
    <div>
      <h2>Import CSV / TXT File</h2>
      <form onSubmit={handleImport}>
        <div style={{ marginBottom: 12 }}>
          <label>File (CSV or TXT): </label>
          <input
            type="file"
            accept=".csv,.txt"
            onChange={e => setFile(e.target.files?.[0] || null)}
            required
          />
        </div>
        <div style={{ marginBottom: 12 }}>
          <label>Target table name: </label>
          <input
            type="text"
            value={tableName}
            onChange={e => setTableName(e.target.value)}
            required
            placeholder="e.g. bank_transactions_2026"
          />
        </div>
        <div style={{ marginBottom: 12 }}>
          <label>
            <input
              type="checkbox"
              checked={replaceExisting}
              onChange={e => setReplaceExisting(e.target.checked)}
            />
            {' '}Replace existing table
          </label>
        </div>
        <div style={{ marginBottom: 12 }}>
          <button type="button" onClick={handlePreview} disabled={!file || loading}>
            Preview
          </button>{' '}
          <button type="submit" disabled={!file || !tableName || loading}>
            Start Import
          </button>
        </div>
      </form>

      {error && <div style={{ color: 'red', marginBottom: 12 }}>{error}</div>}

      {preview && (
        <div>
          <h3>Preview</h3>
          <p>Encoding: {preview.encoding} | Delimiter: "{preview.delimiter}" | Header line: {preview.headerLineIndex}</p>
          <table border={1} cellPadding={4} style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                {preview.normalizedHeaders.map((h: string) => <th key={h}>{h}</th>)}
              </tr>
            </thead>
            <tbody>
              {preview.sampleRows.map((row: any[], i: number) => (
                <tr key={i}>
                  {row.map((cell: any, j: number) => <td key={j}>{cell ?? ''}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
