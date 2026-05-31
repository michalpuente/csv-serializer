import React from 'react';
import { BrowserRouter, Routes, Route, Link } from 'react-router-dom';
import UploadPage from './pages/UploadPage';
import JobPage from './pages/JobPage';
import DatasetsPage from './pages/DatasetsPage';
import DatasetDetailPage from './pages/DatasetDetailPage';
import './App.css';

function App() {
  return (
    <BrowserRouter>
      <nav style={{ padding: '12px 20px', background: '#2c3e50', marginBottom: 20 }}>
        <Link to="/" style={{ color: '#ecf0f1', marginRight: 20, textDecoration: 'none', fontWeight: 'bold' }}>
          Upload
        </Link>
        <Link to="/datasets" style={{ color: '#ecf0f1', textDecoration: 'none', fontWeight: 'bold' }}>
          Datasets
        </Link>
      </nav>
      <div style={{ padding: '0 20px', maxWidth: 1200, margin: '0 auto' }}>
        <Routes>
          <Route path="/" element={<UploadPage />} />
          <Route path="/jobs/:id" element={<JobPage />} />
          <Route path="/datasets" element={<DatasetsPage />} />
          <Route path="/datasets/:id" element={<DatasetDetailPage />} />
        </Routes>
      </div>
    </BrowserRouter>
  );
}

export default App;
