# CSV Serializer

A web application for importing malformed bank-style CSV/TXT files into PostgreSQL with robust Polish encoding/mojibake recovery, dynamic table creation, progress tracking, search, and downloadable summaries.

## Architecture

- **Backend**: .NET 10 / ASP.NET Core Web API (C#)
- **Frontend**: React 19 with TypeScript
- **Database**: PostgreSQL 16
- **Deployment**: Docker Compose

## Features

- Upload and preview CSV/TXT files with automatic encoding detection
- Polish text mojibake repair (Windows-1250 → UTF-8 character recovery)
- Automatic delimiter detection (`;`, `,`, `\t`)
- Header row detection for Polish bank transaction formats
- Dynamic SQL table creation with safe identifier normalization
- Full-row SHA-256 deduplication via `ON CONFLICT (row_hash) DO NOTHING`
- Date and amount parsing with typed derived columns
- Real-time import job progress tracking
- Accent-insensitive search across all imported columns
- Dataset summaries exportable as JSON and CSV

## Quick Start

```bash
docker compose up --build
```

The app will be available at [http://localhost:5000](http://localhost:5000).

## Development

### Backend (.NET)

```bash
cd backend
dotnet run
```

### Frontend (React)

```bash
cd frontend
npm install
npm start
```

### Tests

```bash
cd backend.tests
dotnet test
```

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/uploads` | Preview a CSV/TXT file |
| `POST` | `/api/imports` | Start an import job |
| `GET` | `/api/import-jobs/:id` | Get import job status |
| `GET` | `/api/datasets` | List all datasets |
| `GET` | `/api/datasets/:id` | Get dataset details |
| `GET` | `/api/datasets/:id/rows` | Query rows (paginated, searchable) |
| `GET` | `/api/datasets/:id/summary` | Get dataset summary (JSON) |
| `GET` | `/api/datasets/:id/summary.csv` | Download dataset summary (CSV) |
| `GET` | `/health` | Health check |

## Project Structure

```
├── backend/                  # .NET Web API
│   ├── Controllers/          # API controllers
│   ├── Data/                 # EF Core DbContext
│   ├── Models/               # Entity models
│   ├── Services/             # Business logic
│   └── Migrations/           # EF Core migrations
├── backend.tests/            # xUnit tests
├── frontend/                 # React SPA
│   └── src/
│       ├── api/              # API client
│       └── pages/            # React pages
├── Dockerfile                # Multi-stage build
└── docker-compose.yml        # Docker Compose config
```
