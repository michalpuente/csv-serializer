import React, { useEffect, useMemo, useState, FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  DatasetFilterParams,
  applyRepairDefaults,
  applyRepairRule,
  getDataset,
  getDatasetAnalytics,
  getDatasetRows,
  getFilteredRowsCsvUrl,
  getRepairSuggestions,
  getSummaryCsvUrl,
  runDatasetOperation
} from '../api/client';

export default function DatasetDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [dataset, setDataset] = useState<any>(null);
  const [rows, setRows] = useState<any[]>([]);
  const [filteredRows, setFilteredRows] = useState(0);
  const [analytics, setAnalytics] = useState<any>(null);
  const [operationResult, setOperationResult] = useState<any>(null);
  const [repairInfo, setRepairInfo] = useState<any>(null);
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState('id');
  const [direction, setDirection] = useState<'asc' | 'desc'>('asc');
  const [offset, setOffset] = useState(0);
  const [textColumn, setTextColumn] = useState('');
  const [textMode, setTextMode] = useState<'contains' | 'equals' | 'startsWith'>('contains');
  const [textValue, setTextValue] = useState('');
  const [dateColumn, setDateColumn] = useState('');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [numColumn, setNumColumn] = useState('');
  const [numMin, setNumMin] = useState('');
  const [numMax, setNumMax] = useState('');
  const [operation, setOperation] = useState<'sum' | 'avg' | 'min' | 'max' | 'count' | 'countdistinct'>('sum');
  const [operationColumn, setOperationColumn] = useState('');
  const [repairColumn, setRepairColumn] = useState('');
  const [repairFind, setRepairFind] = useState('');
  const [repairReplace, setRepairReplace] = useState('');
  const [loading, setLoading] = useState(true);
  const [actionInfo, setActionInfo] = useState('');
  const [error, setError] = useState('');
  const limit = 30;

  const filterParams: DatasetFilterParams = useMemo(() => ({
    limit,
    offset,
    search,
    sort,
    direction,
    textColumn,
    textMode,
    textValue,
    dateColumn,
    dateFrom,
    dateTo,
    numColumn,
    numMin,
    numMax
  }), [offset, search, sort, direction, textColumn, textMode, textValue, dateColumn, dateFrom, dateTo, numColumn, numMin, numMax]);

  const fetchData = async () => {
    if (!id) return;
    try {
      setLoading(true);
      setError('');
      const ds = await getDataset(Number(id));
      setDataset(ds);

      const [result, analyticsResult] = await Promise.all([
        getDatasetRows(Number(id), filterParams),
        getDatasetAnalytics(Number(id), filterParams)
      ]);

      setRows(result.rows || []);
      setFilteredRows(result.filteredRows || 0);
      setAnalytics(analyticsResult);

      if (!dateColumn && analyticsResult?.availableDateColumns?.length > 0) {
        setDateColumn(analyticsResult.availableDateColumns[0]);
      }
      if (!numColumn && analyticsResult?.availableNumericColumns?.length > 0) {
        setNumColumn(analyticsResult.availableNumericColumns[0]);
      }
      if (!operationColumn && analyticsResult?.availableNumericColumns?.length > 0) {
        setOperationColumn(analyticsResult.availableNumericColumns[0]);
      }
      if (!repairColumn && ds?.headerRowJson?.length > 0) {
        setRepairColumn(ds.headerRowJson[0]);
      }
    } catch (e: any) {
      setError(e.message || 'Unable to load dataset view');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, offset, sort, direction]);

  useEffect(() => {
    if (!id) return;
    getRepairSuggestions(Number(id)).then(setRepairInfo).catch(() => setRepairInfo(null));
  }, [id]);

  const handleApplyFilters = async (e: FormEvent) => {
    e.preventDefault();
    setOffset(0);
    await fetchData();
  };

  const resetFilters = async () => {
    setTextColumn('');
    setTextMode('contains');
    setTextValue('');
    setDateFrom('');
    setDateTo('');
    setNumMin('');
    setNumMax('');
    setSearch('');
    setOffset(0);
    setActionInfo('Filters reset');
    await fetchData();
  };

  const runOperationAction = async () => {
    if (!id) return;
    const col = operation === 'count' ? null : operationColumn;
    try {
      setActionInfo('Running operation...');
      const result = await runDatasetOperation(Number(id), operation, col, filterParams);
      setOperationResult(result);
      setActionInfo('Operation completed');
    } catch (e: any) {
      setActionInfo(e.message || 'Operation failed');
    }
  };

  const applyDefaultRepair = async () => {
    if (!id) return;
    try {
      const result = await applyRepairDefaults(Number(id));
      setActionInfo(`Default repairs applied. Updated rows: ${result.affectedRows}`);
      await fetchData();
      setRepairInfo(await getRepairSuggestions(Number(id)));
    } catch (e: any) {
      setActionInfo(e.message || 'Repair failed');
    }
  };

  const applyCustomRepair = async () => {
    if (!id || !repairColumn || !repairFind) return;
    try {
      const result = await applyRepairRule(Number(id), repairColumn, repairFind, repairReplace);
      setActionInfo(`Custom repair applied. Updated rows: ${result.affectedRows}`);
      await fetchData();
      setRepairInfo(await getRepairSuggestions(Number(id)));
    } catch (e: any) {
      setActionInfo(e.message || 'Repair failed');
    }
  };

  if (loading && !dataset) return <div className="muted">Loading…</div>;
  if (error) return <div className="alert alert-error">{error}</div>;
  if (!dataset) return <div className="empty">Dataset not found.</div>;

  const headers: string[] = dataset.headerRowJson || [];
  const csvUrl = getFilteredRowsCsvUrl(Number(id), { ...filterParams, limit: undefined, offset: undefined });
  const numericColumns: string[] = analytics?.availableNumericColumns || [];
  const dateColumns: string[] = analytics?.availableDateColumns || [];

  const stats: [string, React.ReactNode][] = [
    ['Rows inserted', `${dataset.insertedRows.toLocaleString()} / ${dataset.totalRows.toLocaleString()}`],
    ['After filters', filteredRows.toLocaleString()],
    ['Duplicates', dataset.duplicateRows.toLocaleString()],
    ['Invalid', dataset.invalidRows.toLocaleString()],
    ['Columns filled', `${analytics?.columnsWithAnyValue ?? '-'} / ${analytics?.columnsTotal ?? '-'}`]
  ];

  return (
    <div className="stack">
      <div className="page-header">
        <div>
          <div className="small muted"><Link to="/datasets">Datasets</Link> /</div>
          <h2 className="mono">{dataset.tableName}</h2>
          <p>{dataset.originalFilename} · {dataset.detectedEncoding}</p>
        </div>
        <div className="row">
          <a className="btn" href={getSummaryCsvUrl(Number(id))} download>Summary CSV</a>
          <a className="btn btn-primary" href={csvUrl} download>Export filtered rows</a>
        </div>
      </div>

      <div className="stats">
        {stats.map(([label, value]) => (
          <div className="stat" key={label}>
            <div className="stat-label">{label}</div>
            <div className="stat-value">{value}</div>
          </div>
        ))}
      </div>

      <form className="card" onSubmit={handleApplyFilters}>
        <div className="card-header">
          <h3>Search &amp; filters</h3>
          <div className="row">
            <button className="btn btn-sm" type="button" onClick={resetFilters}>Reset</button>
          </div>
        </div>
        <div className="stack">
          <input
            type="text"
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Search across all columns…"
          />
          <div className="form-grid">
            <div className="field">
              <label>Text column</label>
              <select value={textColumn} onChange={e => setTextColumn(e.target.value)}>
                <option value="">(disabled)</option>
                {headers.map(h => <option key={h} value={h}>{h}</option>)}
              </select>
            </div>
            <div className="field">
              <label>Match</label>
              <select value={textMode} onChange={e => setTextMode(e.target.value as any)}>
                <option value="contains">contains</option>
                <option value="equals">equals</option>
                <option value="startsWith">starts with</option>
              </select>
            </div>
            <div className="field">
              <label>Text value</label>
              <input value={textValue} onChange={e => setTextValue(e.target.value)} disabled={!textColumn} />
            </div>
            <div className="field">
              <label>Date column</label>
              <select value={dateColumn} onChange={e => setDateColumn(e.target.value)}>
                <option value="">(disabled)</option>
                {dateColumns.map(h => <option key={h} value={h}>{h}</option>)}
              </select>
            </div>
            <div className="field">
              <label>Date from</label>
              <input type="date" value={dateFrom} onChange={e => setDateFrom(e.target.value)} disabled={!dateColumn} />
            </div>
            <div className="field">
              <label>Date to</label>
              <input type="date" value={dateTo} onChange={e => setDateTo(e.target.value)} disabled={!dateColumn} />
            </div>
            <div className="field">
              <label>Numeric column</label>
              <select value={numColumn} onChange={e => setNumColumn(e.target.value)}>
                <option value="">(disabled)</option>
                {numericColumns.map(h => <option key={h} value={h}>{h}</option>)}
              </select>
            </div>
            <div className="field">
              <label>Min</label>
              <input value={numMin} onChange={e => setNumMin(e.target.value)} placeholder="e.g. -1000" disabled={!numColumn} />
            </div>
            <div className="field">
              <label>Max</label>
              <input value={numMax} onChange={e => setNumMax(e.target.value)} placeholder="e.g. 5000" disabled={!numColumn} />
            </div>
          </div>
          <div className="row">
            <button className="btn btn-primary" type="submit">Apply</button>
          </div>
        </div>
      </form>

      <div className="grid-2">
        <div className="card">
          <div className="card-header"><h3>Aggregate</h3></div>
          <div className="row">
            <select value={operation} onChange={e => setOperation(e.target.value as any)}>
              <option value="sum">sum</option>
              <option value="avg">avg</option>
              <option value="min">min</option>
              <option value="max">max</option>
              <option value="count">count</option>
              <option value="countdistinct">count distinct</option>
            </select>
            <select value={operationColumn} onChange={e => setOperationColumn(e.target.value)} disabled={operation === 'count'}>
              {(numericColumns.length ? numericColumns : headers).map(h => <option key={h} value={h}>{h}</option>)}
            </select>
            <button className="btn" type="button" onClick={runOperationAction}>Run</button>
          </div>
          {operationResult && (
            <div className="stat" style={{ marginTop: 12 }}>
              <div className="stat-label">{operation}{operation !== 'count' ? ` of ${operationColumn}` : ''} (filtered rows)</div>
              <div className="stat-value">{String(operationResult.value)}</div>
            </div>
          )}
        </div>

        <div className="card">
          <div className="card-header">
            <h3>Repair text</h3>
            <button className="btn btn-sm" type="button" onClick={applyDefaultRepair}>Apply Polish defaults</button>
          </div>
          <div className="row">
            <select value={repairColumn} onChange={e => setRepairColumn(e.target.value)}>
              {headers.map(h => <option key={h} value={h}>{h}</option>)}
            </select>
            <input value={repairFind} onChange={e => setRepairFind(e.target.value)} placeholder="Find" />
            <input value={repairReplace} onChange={e => setRepairReplace(e.target.value)} placeholder="Replace with" />
            <button className="btn" type="button" onClick={applyCustomRepair} disabled={!repairFind}>Apply</button>
          </div>
          {!!repairInfo?.samples?.length && (
            <div style={{ marginTop: 12 }}>
              <div className="label" style={{ marginBottom: 6 }}>Detected broken values</div>
              <ul className="events">
                {repairInfo.samples.map((s: any, i: number) => (
                  <li key={`${s.column}-${i}`}>
                    <span className="badge">{s.column}</span>
                    <span className="mono">{s.value}</span>
                    <span className="time">×{s.count}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      </div>

      {actionInfo && <div className="alert alert-info">{actionInfo}</div>}

      <div className="card flush">
        <div className="card-header">
          <h3>Rows</h3>
          <div className="row">
            <span className="label">Sort</span>
            <select value={sort} onChange={e => { setSort(e.target.value); setOffset(0); }}>
              <option value="id">id</option>
              {Array.from(new Set([...headers, ...numericColumns, ...dateColumns])).map(h => <option key={h} value={h}>{h}</option>)}
            </select>
            <select value={direction} onChange={e => setDirection(e.target.value as any)}>
              <option value="asc">ascending</option>
              <option value="desc">descending</option>
            </select>
          </div>
        </div>
        <div className="table-wrap" style={{ marginTop: 14 }}>
          <table className="table dense">
            <thead>
              <tr>
                <th className="num">id</th>
                {headers.map(h => <th key={h}>{h}</th>)}
              </tr>
            </thead>
            <tbody>
              {rows.map((row, i) => (
                <tr key={row.id || i}>
                  <td className="num muted">{row.id}</td>
                  {headers.map(h => <td key={h} title={row[h] ?? ''}>{row[h] ?? ''}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
          {rows.length === 0 && <div className="empty">No rows match the current filters.</div>}
        </div>
        <div className="pager">
          <span className="small muted">
            {rows.length ? `${offset + 1}–${offset + rows.length}` : '0'} of {filteredRows.toLocaleString()}
          </span>
          <div className="row">
            <button className="btn btn-sm" onClick={() => setOffset(Math.max(0, offset - limit))} disabled={offset === 0}>← Previous</button>
            <button className="btn btn-sm" onClick={() => setOffset(offset + limit)} disabled={rows.length < limit}>Next →</button>
          </div>
        </div>
      </div>
    </div>
  );
}
