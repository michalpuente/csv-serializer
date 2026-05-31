import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getDatasets } from '../api/client';

export default function DatasetsPage() {
  const [datasets, setDatasets] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);

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
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
