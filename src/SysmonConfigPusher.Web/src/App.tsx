import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { DeploymentQueueProvider } from './context/DeploymentQueueContext';
import { ThemeProvider } from './context/ThemeContext';
import { Layout } from './components/Layout';
import { ComputersPage } from './pages/ComputersPage';
import { ConfigsPage } from './pages/ConfigsPage';
import { DeployWizardPage } from './pages/DeployWizardPage';
import { DeploymentsPage } from './pages/DeploymentsPage';
import { DeploymentDetailPage } from './pages/DeploymentDetailPage';
import { EventsPage } from './pages/EventsPage';
import { NoiseAnalysisPage } from './pages/NoiseAnalysisPage';

function App() {
  return (
    <ThemeProvider>
      <BrowserRouter>
        <DeploymentQueueProvider>
          <Routes>
            <Route path="/" element={<Layout />}>
              <Route index element={<ComputersPage />} />
              <Route path="configs" element={<ConfigsPage />} />
              <Route path="deploy" element={<DeployWizardPage />} />
              <Route path="deployments" element={<DeploymentsPage />} />
              <Route path="deployments/:id" element={<DeploymentDetailPage />} />
              <Route path="events" element={<EventsPage />} />
              <Route path="analysis" element={<NoiseAnalysisPage />} />
            </Route>
          </Routes>
        </DeploymentQueueProvider>
      </BrowserRouter>
    </ThemeProvider>
  );
}

export default App;
