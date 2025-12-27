import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { Layout } from './components/Layout';
import { ComputersPage } from './pages/ComputersPage';
import { ConfigsPage } from './pages/ConfigsPage';
import { DeployWizardPage } from './pages/DeployWizardPage';
import { DeploymentsPage } from './pages/DeploymentsPage';
import { DeploymentDetailPage } from './pages/DeploymentDetailPage';

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Layout />}>
          <Route index element={<ComputersPage />} />
          <Route path="configs" element={<ConfigsPage />} />
          <Route path="deploy" element={<DeployWizardPage />} />
          <Route path="deployments" element={<DeploymentsPage />} />
          <Route path="deployments/:id" element={<DeploymentDetailPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}

export default App;
