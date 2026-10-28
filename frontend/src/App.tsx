import React from 'react';
import { BrowserRouter, Routes, Route, Link, NavLink } from 'react-router-dom';
import UploadPage from './pages/UploadPage';
import JobPage from './pages/JobPage';
import DatasetsPage from './pages/DatasetsPage';
import DatasetDetailPage from './pages/DatasetDetailPage';
import './App.css';

function App() {
  return (
    <BrowserRouter basename={process.env.PUBLIC_URL}>
      <header className="topbar">
        <div className="topbar-inner">
          <Link to="/" className="brand">
            <span className="brand-mark">CSV</span>
            <span className="brand-name">CSV Serializer</span>
          </Link>
          <nav className="nav">
            <NavLink to="/" end>Import</NavLink>
            <NavLink to="/datasets">Datasets</NavLink>
          </nav>
        </div>
      </header>
      <main className="page">
        <Routes>
          <Route path="/" element={<UploadPage />} />
          <Route path="/jobs/:id" element={<JobPage />} />
          <Route path="/datasets" element={<DatasetsPage />} />
          <Route path="/datasets/:id" element={<DatasetDetailPage />} />
        </Routes>
      </main>
    </BrowserRouter>
  );
}

export default App;
