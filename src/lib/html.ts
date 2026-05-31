import escape from 'escape-html';

export function escapeHtml(value: unknown): string {
  return escape(String(value ?? ''));
}

export function layout(title: string, body: string): string {
  return `<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>${escapeHtml(title)}</title>
  <style>
    body{font-family:system-ui,-apple-system,sans-serif;max-width:1080px;margin:1.5rem auto;padding:0 1rem;line-height:1.4}
    table{border-collapse:collapse;width:100%;font-size:0.9rem}
    th,td{border:1px solid #ddd;padding:.35rem;vertical-align:top}
    th{background:#f5f5f5}
    .muted{color:#666}
    .row{display:flex;gap:.75rem;align-items:center;flex-wrap:wrap}
    .card{border:1px solid #ddd;border-radius:8px;padding:1rem;margin:1rem 0}
    .badge{display:inline-block;padding:.15rem .4rem;border-radius:4px;background:#eee}
    progress{width:260px}
    input,select,button{padding:.4rem}
    nav a{margin-right:.8rem}
  </style>
</head>
<body>
  <nav><a href="/">Upload</a><a href="/datasets">Datasets</a></nav>
  ${body}
</body>
</html>`;
}
