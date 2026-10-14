import { Navigate, Route, Routes } from 'react-router-dom';
import { DashboardPage } from './pages/DashboardPage';
import { PcsPage } from './pages/PcsPage';
import { SessionHistoryPage } from './pages/SessionHistoryPage';
import { MembersPage } from './pages/MembersPage';
import { PWAPrompt } from './components/PWAPrompt';
import { MemberTopupModal } from './components/MemberTopupModal';
import { PushNotificationManager } from './components/PushNotificationManager';

function App() {
  return (
    <>
      <Routes>
        <Route path="/" element={<Navigate to="/pcs" replace />} />
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route path="/pcs" element={<PcsPage />} />
        <Route path="/history" element={<SessionHistoryPage />} />
        <Route path="/members" element={<MembersPage />} />
        <Route path="*" element={<Navigate to="/pcs" replace />} />
      </Routes>
      <MemberTopupModal />
      <PushNotificationManager />
      <PWAPrompt />
    </>
  );
}

export default App;
