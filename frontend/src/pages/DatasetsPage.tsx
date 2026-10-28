import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { deleteDataset, getDatasets } from '../api/client';
import StatusBadge from '../components/StatusBadge';

export default function DatasetsPage() {
  const [datasets, setDatasets] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [deletingId, setDeletingId] = useState<number | null>(null);

  const handleDelete = async (d: any) => {
    if (!window.confirm(`Delete dataset "${d.tableName}" and its table? This cannot be undone.`)) return;
    setDeletingId(d.id);
    try {
      await deleteDataset(d.id);
      setDatasets(prev => prev.filter(x => x.id !== d.id));
    } catch (e: any) {
      alert(e.message);
    } finally {
      setDeletingId(null);
    }
  };

  useEffect(() => {
    getDatasets()
      .then(data => setDatasets(data || []))
      .catch((e: any) => setError(e.message || 'Unable to load datasets'))
      .finally(() => setLoading(false));
  }, []);

  return (
    <div className="stack">
      <div className="page-header">
        <div>
          <h2>Datasets</h2>
          <p>{loading ? 'Loading…' : `${datasets.length} imported table${datasets.length === 1 ? '' : 's'}`}</p>
        </div>
        <Link to="/" className="btn btn-primary">New import</Link>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      {!loading && (
        <div className="card flush">
          {datasets.length === 0 ? (
            <div className="empty">No datasets imported yet. <Link to="/">Import one →</Link></div>
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Table</th>
                    <th>File</th>
                    <th>Status</th>
                    <th className="num">Rows</th>
                    <th>Created</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {datasets.map(d => (
                    <tr key={d.id}>
                      <td><Link to={`/datasets/${d.id}`}><strong>{d.tableName}</strong></Link></td>
                      <td className="muted">{d.originalFilename}</td>
                      <td><StatusBadge status={d.status} /></td>
                      <td className="num">{d.insertedRows.toLocaleString()} / {d.totalRows.toLocaleString()}</td>
                      <td className="muted">{new Date(d.createdAt).toLocaleString()}</td>
                      <td className="num">
                        <button
                          className="btn btn-sm btn-danger"
                          onClick={() => handleDelete(d)}
                          disabled={deletingId === d.id || d.status === 'queued' || d.status === 'processing'}
                        >
                          {deletingId === d.id ? 'Deleting…' : 'Delete'}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
