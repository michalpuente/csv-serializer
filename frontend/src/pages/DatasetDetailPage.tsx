import React, { useEffect, useMemo, useState, FormEvent } from 'react';
import { useParams } from 'react-router-dom';
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

  const handleSearch = (e: FormEvent) => {
    e.preventDefault();
    setOffset(0);
    fetchData();
  };

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

  if (loading && !dataset) return <div>Loading...</div>;
  if (error) return <div style={{ color: 'red' }}>{error}</div>;
  if (!dataset) return <div>Dataset not found.</div>;

  const headers: string[] = dataset.headerRowJson || [];
  const csvUrl = getFilteredRowsCsvUrl(Number(id), { ...filterParams, limit: undefined, offset: undefined });

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
        {' | '}
        <a href={csvUrl} download>Download Filtered Rows CSV</a>
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

      <form onSubmit={handleApplyFilters} style={{ border: '1px solid #ddd', padding: 12, marginBottom: 12 }}>
        <h3 style={{ marginTop: 0 }}>Filters</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 8 }}>
          <div>
            <label>Text column</label>
            <select value={textColumn} onChange={e => setTextColumn(e.target.value)} style={{ width: '100%' }}>
              <option value="">(disabled)</option>
              {headers.map(h => <option key={h} value={h}>{h}</option>)}
            </select>
          </div>
          <div>
            <label>Text mode</label>
            <select value={textMode} onChange={e => setTextMode(e.target.value as any)} style={{ width: '100%' }}>
              <option value="contains">contains</option>
              <option value="equals">equals</option>
              <option value="startsWith">startsWith</option>
            </select>
          </div>
          <div>
            <label>Text value</label>
            <input value={textValue} onChange={e => setTextValue(e.target.value)} style={{ width: '100%' }} />
          </div>
          <div>
            <label>Date column</label>
            <select value={dateColumn} onChange={e => setDateColumn(e.target.value)} style={{ width: '100%' }}>
              <option value="">(disabled)</option>
              {(analytics?.availableDateColumns || []).map((h: string) => <option key={h} value={h}>{h}</option>)}
            </select>
          </div>
          <div>
            <label>Date from</label>
            <input type="date" value={dateFrom} onChange={e => setDateFrom(e.target.value)} style={{ width: '100%' }} />
          </div>
          <div>
            <label>Date to</label>
            <input type="date" value={dateTo} onChange={e => setDateTo(e.target.value)} style={{ width: '100%' }} />
          </div>
          <div>
            <label>Numeric column</label>
            <select value={numColumn} onChange={e => setNumColumn(e.target.value)} style={{ width: '100%' }}>
              <option value="">(disabled)</option>
              {(analytics?.availableNumericColumns || []).map((h: string) => <option key={h} value={h}>{h}</option>)}
            </select>
          </div>
          <div>
            <label>Min</label>
            <input value={numMin} onChange={e => setNumMin(e.target.value)} placeholder="e.g. -1000" style={{ width: '100%' }} />
          </div>
          <div>
            <label>Max</label>
            <input value={numMax} onChange={e => setNumMax(e.target.value)} placeholder="e.g. 5000" style={{ width: '100%' }} />
          </div>
        </div>
        <div style={{ marginTop: 8 }}>
          <button type="submit">Apply Filters</button>{' '}
          <button type="button" onClick={resetFilters}>Reset Filters</button>
        </div>
      </form>

      <div style={{ marginBottom: 12 }}>
        <label>Sort by: </label>
        <select value={sort} onChange={e => { setSort(e.target.value); setOffset(0); }}>
          <option value="id">id</option>
          {headers.map(h => <option key={h} value={h}>{h}</option>)}
          {(analytics?.availableNumericColumns || []).map((h: string) => <option key={h} value={h}>{h}</option>)}
          {(analytics?.availableDateColumns || []).map((h: string) => <option key={h} value={h}>{h}</option>)}
        </select>
        {' '}
        <select value={direction} onChange={e => setDirection(e.target.value as any)}>
          <option value="asc">asc</option>
          <option value="desc">desc</option>
        </select>
      </div>

      <div style={{ border: '1px solid #ddd', padding: 12, marginBottom: 12 }}>
        <h3 style={{ marginTop: 0 }}>View Stats</h3>
        <p style={{ margin: 0 }}>
          Rows: filtered {filteredRows} / total {analytics?.rowsTotal ?? '-'} | 
          Columns: total {analytics?.columnsTotal ?? '-'} | filled {analytics?.columnsWithAnyValue ?? '-'} | all null {analytics?.columnsAllNull ?? '-'}
        </p>
      </div>

      <div style={{ border: '1px solid #ddd', padding: 12, marginBottom: 12 }}>
        <h3 style={{ marginTop: 0 }}>Operations</h3>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
          <select value={operation} onChange={e => setOperation(e.target.value as any)}>
            <option value="sum">sum</option>
            <option value="avg">avg</option>
            <option value="min">min</option>
            <option value="max">max</option>
            <option value="count">count</option>
            <option value="countdistinct">countdistinct</option>
          </select>
          <select value={operationColumn} onChange={e => setOperationColumn(e.target.value)} disabled={operation === 'count'}>
            {(analytics?.availableNumericColumns || headers).map((h: string) => <option key={h} value={h}>{h}</option>)}
          </select>
          <button type="button" onClick={runOperationAction}>Run Operation</button>
        </div>
        {operationResult && (
          <p style={{ marginBottom: 0 }}>
            Result: <strong>{String(operationResult.value)}</strong>
          </p>
        )}
      </div>

      <div style={{ border: '1px solid #ddd', padding: 12, marginBottom: 12 }}>
        <h3 style={{ marginTop: 0 }}>Repair Text</h3>
        <button type="button" onClick={applyDefaultRepair}>Apply Default Polish Repairs</button>
        <div style={{ marginTop: 10, display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
          <select value={repairColumn} onChange={e => setRepairColumn(e.target.value)}>
            {headers.map(h => <option key={h} value={h}>{h}</option>)}
          </select>
          <input value={repairFind} onChange={e => setRepairFind(e.target.value)} placeholder="Find text" />
          <input value={repairReplace} onChange={e => setRepairReplace(e.target.value)} placeholder="Replace with" />
          <button type="button" onClick={applyCustomRepair} disabled={!repairFind}>Apply Custom Repair</button>
        </div>
        {!!repairInfo?.samples?.length && (
          <div style={{ marginTop: 10 }}>
            <strong>Detected broken samples:</strong>
            <ul>
              {repairInfo.samples.map((s: any, i: number) => (
                <li key={`${s.column}-${i}`}>{s.column}: {s.value} ({s.count})</li>
              ))}
            </ul>
          </div>
        )}
      </div>

      {actionInfo && <div style={{ marginBottom: 10, color: '#2c3e50' }}>{actionInfo}</div>}

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
        <small>Showing {rows.length} rows on this page, {filteredRows} after filters.</small>
        <br />
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
