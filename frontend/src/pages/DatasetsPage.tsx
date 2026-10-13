import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { deleteDataset, getDatasets } from '../api/client';

export default function DatasetsPage() {
  const [datasets, setDatasets] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
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
    getDatasets().then(data => {
      setDatasets(data);
      setLoading(false);
    });
  }, []);

  if (loading) return <div>Loading...</div>;

  return (
    <div>
      <h2>Datasets</h2>
      {datasets.length === 0 ? (
        <p>No datasets imported yet. <Link to="/">Import one →</Link></p>
      ) : (
        <table border={1} cellPadding={4} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>ID</th>
              <th>Table Name</th>
              <th>Filename</th>
              <th>Status</th>
              <th>Rows (inserted/total)</th>
              <th>Created</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {datasets.map(d => (
              <tr key={d.id}>
                <td>{d.id}</td>
                <td><Link to={`/datasets/${d.id}`}>{d.tableName}</Link></td>
                <td>{d.originalFilename}</td>
                <td>{d.status}</td>
                <td>{d.insertedRows}/{d.totalRows}</td>
                <td>{new Date(d.createdAt).toLocaleString()}</td>
                <td>
                  <button
                    onClick={() => handleDelete(d)}
                    disabled={deletingId === d.id || d.status === 'queued' || d.status === 'processing'}
                    style={{ color: '#c0392b' }}
                  >
                    {deletingId === d.id ? 'Deleting…' : 'Delete'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
