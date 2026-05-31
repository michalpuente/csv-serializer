import React, { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { getJob } from '../api/client';

export default function JobPage() {
  const { id } = useParams<{ id: string }>();
  const [job, setJob] = useState<any>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!id) return;
    const fetchJob = async () => {
      try {
        const data = await getJob(Number(id));
        setJob(data);
      } catch (e: any) {
        setError(e.message);
      }
    };
    fetchJob();
    const interval = setInterval(fetchJob, 2000);
    return () => clearInterval(interval);
  }, [id]);

  if (error) return <div style={{ color: 'red' }}>{error}</div>;
  if (!job) return <div>Loading...</div>;

  const isFinished = job.status === 'completed' || job.status === 'failed';

  return (
    <div>
      <h2>Import Job #{job.id}</h2>
      <p>
        Status: <strong>{job.status}</strong>
        {job.errorMessage && <span style={{ color: 'red' }}> — {job.errorMessage}</span>}
      </p>

      <div style={{ marginBottom: 16 }}>
        <div style={{ background: '#eee', borderRadius: 4, overflow: 'hidden', height: 24 }}>
          <div
            style={{
              width: `${job.progressPercent}%`,
              background: job.status === 'failed' ? '#e74c3c' : '#2ecc71',
              height: '100%',
              transition: 'width 0.3s'
            }}
          />
        </div>
        <small>{job.progressPercent}%</small>
      </div>

      <table border={1} cellPadding={4} style={{ borderCollapse: 'collapse', marginBottom: 16 }}>
        <tbody>
          <tr><td>Rows scanned</td><td>{job.rowsScanned}</td></tr>
          <tr><td>Rows normalized</td><td>{job.rowsNormalized}</td></tr>
          <tr><td>Rows inserted</td><td>{job.rowsInserted}</td></tr>
          <tr><td>Duplicates</td><td>{job.rowsDuplicates}</td></tr>
          <tr><td>Invalid</td><td>{job.rowsInvalid}</td></tr>
        </tbody>
      </table>

      {isFinished && job.datasetId > 0 && (
        <p><Link to={`/datasets/${job.datasetId}`}>View Dataset →</Link></p>
      )}

      <h3>Events</h3>
      <ul>
        {job.events?.map((evt: any) => (
          <li key={evt.id}>
            <small>[{evt.level}]</small> {evt.message}
            <small style={{ color: '#888' }}> — {new Date(evt.createdAt).toLocaleTimeString()}</small>
          </li>
        ))}
      </ul>
    </div>
  );
}
