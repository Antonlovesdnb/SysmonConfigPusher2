import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { DeploymentQueueProvider } from './context/DeploymentQueueContext';
import { ThemeProvider } from './context/ThemeContext';
import { ToastProvider } from './context/ToastContext';
import { Layout } from './components/Layout';
import { ToastContainer } from './components/ToastContainer';
import { DashboardPage } from './pages/DashboardPage';
import { ComputersPage } from './pages/ComputersPage';
import { ConfigsPage } from './pages/ConfigsPage';
import { DeployWizardPage } from './pages/DeployWizardPage';
import { DeploymentsPage } from './pages/DeploymentsPage';
import { DeploymentDetailPage } from './pages/DeploymentDetailPage';
import { EventsPage } from './pages/EventsPage';
import { NoiseAnalysisPage } from './pages/NoiseAnalysisPage';
import { SettingsPage } from './pages/SettingsPage';

function App() {
  return (
    <ThemeProvider>
      <AuthProvider>
        <ToastProvider>
        <BrowserRouter>
          <DeploymentQueueProvider>
          <Routes>
            <Route path="/" element={<Layout />}>
              <Route index element={<DashboardPage />} />
              <Route path="inventory" element={<ComputersPage />} />
              <Route path="configs" element={<ConfigsPage />} />
              <Route path="deploy" element={<DeployWizardPage />} />
              <Route path="deployments" element={<DeploymentsPage />} />
              <Route path="deployments/:id" element={<DeploymentDetailPage />} />
              <Route path="events" element={<EventsPage />} />
              <Route path="analysis" element={<NoiseAnalysisPage />} />
              <Route path="settings" element={<SettingsPage />} />
            </Route>
          </Routes>
          </DeploymentQueueProvider>
          <ToastContainer />
        </BrowserRouter>
        </ToastProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

export default App;
