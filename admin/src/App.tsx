import { Navigate, Route, Routes } from 'react-router-dom';
import { PcsPage } from './pages/PcsPage';
import { SessionHistoryPage } from './pages/SessionHistoryPage';
import { MembersPage } from './pages/MembersPage';

function App() {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/pcs" replace />} />
      <Route path="/pcs" element={<PcsPage />} />
      <Route path="/history" element={<SessionHistoryPage />} />
      <Route path="/members" element={<MembersPage />} />
    </Routes>
  );
}

export default App;
