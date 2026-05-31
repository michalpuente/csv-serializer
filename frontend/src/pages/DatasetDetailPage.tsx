import React, { useEffect, useState, FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import { getDataset, getDatasetRows, getSummaryCsvUrl } from '../api/client';

export default function DatasetDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [dataset, setDataset] = useState<any>(null);
  const [rows, setRows] = useState<any[]>([]);
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState('');
  const [offset, setOffset] = useState(0);
  const [loading, setLoading] = useState(true);
  const limit = 30;

  const fetchData = async () => {
    if (!id) return;
    setLoading(true);
    const ds = await getDataset(Number(id));
    setDataset(ds);
    const result = await getDatasetRows(Number(id), { limit, offset, search, sort });
    setRows(result.rows || []);
    setLoading(false);
  };

  useEffect(() => {
    fetchData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, offset, sort]);

  const handleSearch = (e: FormEvent) => {
    e.preventDefault();
    setOffset(0);
    fetchData();
  };

  if (loading && !dataset) return <div>Loading...</div>;
  if (!dataset) return <div>Dataset not found.</div>;

  const headers: string[] = dataset.headerRowJson || [];

  return (
    <div>
      <h2>Dataset: {dataset.tableName}</h2>
      <p>
        File: {dataset.originalFilename} | Encoding: {dataset.detectedEncoding} |
        Rows: {dataset.insertedRows}/{dataset.totalRows} |
        Duplicates: {dataset.duplicateRows} | Invalid: {dataset.invalidRows}
      </p>
      <p>
        <a href={getSummaryCsvUrl(Number(id))} download>Download Summary CSV</a>
      </p>

      <form onSubmit={handleSearch} style={{ marginBottom: 12 }}>
        <input
          type="text"
          value={search}
          onChange={e => setSearch(e.target.value)}
          placeholder="Search across all columns..."
          style={{ width: 300, marginRight: 8 }}
        />
        <button type="submit">Search</button>
      </form>

      <div style={{ marginBottom: 12 }}>
        <label>Sort by: </label>
        <select value={sort} onChange={e => { setSort(e.target.value); setOffset(0); }}>
          <option value="">ID</option>
          {headers.map(h => <option key={h} value={h}>{h}</option>)}
        </select>
      </div>

      <div style={{ overflowX: 'auto' }}>
        <table border={1} cellPadding={4} style={{ borderCollapse: 'collapse', fontSize: 13 }}>
          <thead>
            <tr>
              <th>id</th>
              {headers.map(h => <th key={h}>{h}</th>)}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => (
              <tr key={row.id || i}>
                <td>{row.id}</td>
                {headers.map(h => <td key={h}>{row[h] ?? ''}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div style={{ marginTop: 12 }}>
        {offset > 0 && (
          <button onClick={() => setOffset(Math.max(0, offset - limit))}>← Previous</button>
        )}{' '}
        {rows.length === limit && (
          <button onClick={() => setOffset(offset + limit)}>Next →</button>
        )}
      </div>
    </div>
  );
}
