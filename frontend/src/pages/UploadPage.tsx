import React, { useState, FormEvent, DragEvent } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { previewFile, startImport, getDatasets } from '../api/client';
import StatusBadge from '../components/StatusBadge';

export default function UploadPage() {
  const navigate = useNavigate();
  const [file, setFile] = useState<File | null>(null);
  const [tableName, setTableName] = useState('');
  const [replaceExisting, setReplaceExisting] = useState(false);
  const [preview, setPreview] = useState<any>(null);
  const [loading, setLoading] = useState(false);
  const [dragging, setDragging] = useState(false);
  const [error, setError] = useState('');
  const [datasets, setDatasets] = useState<any[]>([]);

  React.useEffect(() => {
    getDatasets()
      .then(data => setDatasets(data || []))
      .catch(() => setDatasets([]));
  }, []);

  const chooseFile = (f: File | null) => {
    setFile(f);
    setPreview(null);
    if (f && !tableName) {
      // Suggest a table name from the file name
      setTableName(f.name.replace(/\.[^.]+$/, '').toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, ''));
    }
  };

  const handleDrop = (e: DragEvent<HTMLLabelElement>) => {
    e.preventDefault();
    setDragging(false);
    chooseFile(e.dataTransfer.files?.[0] || null);
  };

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
    <div className="stack">
      <div className="page-header">
        <div>
          <h2>Import a file</h2>
          <p>Upload a CSV or TXT export – encoding, delimiter and header row are detected automatically.</p>
        </div>
      </div>

      <form className="card stack" onSubmit={handleImport}>
        <label
          className={`dropzone${dragging ? ' dragging' : ''}`}
          onDragOver={e => { e.preventDefault(); setDragging(true); }}
          onDragLeave={() => setDragging(false)}
          onDrop={handleDrop}
        >
          <input type="file" accept=".csv,.txt" onChange={e => chooseFile(e.target.files?.[0] || null)} />
          <div className="dropzone-title">Drop a file here or click to browse</div>
          <div className="muted small">.csv or .txt</div>
          {file && <div className="dropzone-file">{file.name} · {(file.size / 1024).toFixed(1)} KB</div>}
        </label>

        <div className="form-grid">
          <div className="field">
            <label htmlFor="tableName">Target table name</label>
            <input
              id="tableName"
              type="text"
              value={tableName}
              onChange={e => setTableName(e.target.value)}
              required
              placeholder="e.g. bank_transactions_2026"
            />
          </div>
          <div className="field" style={{ justifyContent: 'flex-end' }}>
            <label className="checkbox" style={{ minHeight: 36 }}>
              <input
                type="checkbox"
                checked={replaceExisting}
                onChange={e => setReplaceExisting(e.target.checked)}
              />
              Replace existing table
            </label>
          </div>
        </div>

        {error && <div className="alert alert-error">{error}</div>}

        <div className="row">
          <button className="btn btn-primary" type="submit" disabled={!file || !tableName || loading}>
            {loading ? 'Working…' : 'Start import'}
          </button>
          <button className="btn" type="button" onClick={handlePreview} disabled={!file || loading}>
            Preview
          </button>
        </div>
      </form>

      {preview && (
        <div className="card flush">
          <div className="card-header">
            <h3>Preview</h3>
            <div className="row small muted">
              <span>Encoding <strong>{preview.encoding}</strong></span>
              <span>· Delimiter <code>{preview.delimiter}</code></span>
              <span>· Header line <strong>{preview.headerLineIndex}</strong></span>
            </div>
          </div>
          <div className="table-wrap" style={{ marginTop: 14 }}>
            <table className="table dense">
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
        </div>
      )}

      <div className="card flush">
        <div className="card-header">
          <h3>Recent datasets</h3>
          <Link to="/datasets" className="small">View all →</Link>
        </div>
        {datasets.length === 0 ? (
          <div className="empty">No datasets yet.</div>
        ) : (
          <div className="table-wrap" style={{ marginTop: 14 }}>
            <table className="table">
              <thead>
                <tr>
                  <th>Table</th>
                  <th>Status</th>
                  <th className="num">Rows</th>
                </tr>
              </thead>
              <tbody>
                {datasets.slice(0, 10).map(d => (
                  <tr key={d.id}>
                    <td><Link to={`/datasets/${d.id}`}>{d.tableName}</Link></td>
                    <td><StatusBadge status={d.status} /></td>
                    <td className="num">{d.insertedRows.toLocaleString()} / {d.totalRows.toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
