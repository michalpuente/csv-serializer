import React, { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { getJob } from '../api/client';
import StatusBadge from '../components/StatusBadge';

const LEVEL_TONES: Record<string, string> = { error: 'badge-danger', warning: 'badge-warn', warn: 'badge-warn' };

export default function JobPage() {
  const { id } = useParams<{ id: string }>();
  const [job, setJob] = useState<any>(null);
  const [error, setError] = useState('');

  const isFinished = job?.status === 'completed' || job?.status === 'failed';

  useEffect(() => {
    if (!id || isFinished) return;
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
  }, [id, isFinished]);

  if (error) return <div className="alert alert-error">{error}</div>;
  if (!job) return <div className="muted">Loading…</div>;

  const stats: [string, number][] = [
    ['Scanned', job.rowsScanned],
    ['Normalized', job.rowsNormalized],
    ['Inserted', job.rowsInserted],
    ['Duplicates', job.rowsDuplicates],
    ['Invalid', job.rowsInvalid]
  ];

  return (
    <div className="stack">
      <div className="page-header">
        <div>
          <h2>Import job #{job.id}</h2>
          <p className="row"><StatusBadge status={job.status} /></p>
        </div>
        {isFinished && job.datasetId > 0 && (
          <Link to={`/datasets/${job.datasetId}`} className="btn btn-primary">Open dataset →</Link>
        )}
      </div>

      {job.errorMessage && <div className="alert alert-error">{job.errorMessage}</div>}

      <div className="card">
        <div className="card-header" style={{ marginBottom: 8 }}>
          <span className="label">Progress</span>
          <strong>{job.progressPercent}%</strong>
        </div>
        <div className="progress">
          <div className={`progress-bar${job.status === 'failed' ? ' failed' : ''}`} style={{ width: `${job.progressPercent}%` }} />
        </div>
      </div>

      <div className="stats">
        {stats.map(([label, value]) => (
          <div className="stat" key={label}>
            <div className="stat-label">{label}</div>
            <div className="stat-value">{(value ?? 0).toLocaleString()}</div>
          </div>
        ))}
      </div>

      <div className="card">
        <div className="card-header"><h3>Events</h3></div>
        {job.events?.length ? (
          <ul className="events">
            {job.events.map((evt: any) => (
              <li key={evt.id}>
                <span className={`badge ${LEVEL_TONES[String(evt.level).toLowerCase()] || ''}`}>{evt.level}</span>
                <span>{evt.message}</span>
                <span className="time">{new Date(evt.createdAt).toLocaleTimeString()}</span>
              </li>
            ))}
          </ul>
        ) : (
          <div className="muted">No events yet.</div>
        )}
      </div>
    </div>
  );
}
