# csv-serializer

Dockerized Node.js + PostgreSQL app for importing malformed Polish bank CSV-like files, repairing text, deduplicating full normalized rows, searching data, and downloading summaries.

## Run with Docker

1. Copy environment file:

```bash
cp .env.example .env
```

2. Start services:

```bash
docker compose up --build
```

3. Open `http://localhost:3000`.

## Features

- Upload and preview CSV/TXT imports.
- Detect encoding, delimiter, and true transaction header row.
- Repair common Polish mojibake patterns (`Bankowoœæ`, `£ódŸ`, `Tytu³`, etc.).
- Dynamic destination table creation with safe SQL identifiers.
- Row normalization + SHA-256 full-row deduplication using `row_hash` unique index.
- Persisted import jobs with progress counters and event logs.
- Dataset list and dataset detail page with free-text search.
- Accent-insensitive search using PostgreSQL `unaccent`.
- Summary export as JSON and CSV.

## Local development (without Docker)

Requires PostgreSQL and `DATABASE_URL` set.

```bash
npm install
npm run migrate
npm run dev
```

## Tests

```bash
npm test
```
