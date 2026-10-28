import React from 'react';

const TONES: Record<string, string> = {
  completed: 'badge-ok',
  processing: 'badge-info',
  queued: 'badge-warn',
  failed: 'badge-danger'
};

export default function StatusBadge({ status }: { status: string }) {
  return <span className={`badge ${TONES[status] || ''}`}>{status}</span>;
}
