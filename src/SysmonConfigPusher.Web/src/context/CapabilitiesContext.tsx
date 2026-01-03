import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { capabilitiesApi } from '../api';
import type { ServerCapabilities, FeatureFlags, ServerMode, AuthenticationMode } from '../types';

interface CapabilitiesContextType {
  capabilities: ServerCapabilities | null;
  loading: boolean;
  error: string | null;
  // Convenience properties
  serverMode: ServerMode;
  authMode: AuthenticationMode;
  features: FeatureFlags;
  isAgentOnlyMode: boolean;
  hasWmiDeployment: boolean;
  hasSmbFileTransfer: boolean;
  hasActiveDirectory: boolean;
  hasAgentDeployment: boolean;
  refresh: () => Promise<void>;
}

const defaultFeatures: FeatureFlags = {
  wmiDeployment: true,
  smbFileTransfer: true,
  activeDirectory: true,
  agentDeployment: true,
};

const CapabilitiesContext = createContext<CapabilitiesContextType | null>(null);

export function CapabilitiesProvider({ children }: { children: ReactNode }) {
  const [capabilities, setCapabilities] = useState<ServerCapabilities | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = async () => {
    try {
      setLoading(true);
      const caps = await capabilitiesApi.get();
      setCapabilities(caps);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load capabilities');
      // Set default capabilities on error (assume Full mode)
      setCapabilities(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    refresh();
  }, []);

  const features = capabilities?.features ?? defaultFeatures;
  const serverMode = capabilities?.serverMode ?? 'Full';
  const authMode = capabilities?.authenticationMode ?? 'Windows';

  return (
    <CapabilitiesContext.Provider
      value={{
        capabilities,
        loading,
        error,
        serverMode,
        authMode,
        features,
        isAgentOnlyMode: serverMode === 'AgentOnly',
        hasWmiDeployment: features.wmiDeployment,
        hasSmbFileTransfer: features.smbFileTransfer,
        hasActiveDirectory: features.activeDirectory,
        hasAgentDeployment: features.agentDeployment,
        refresh,
      }}
    >
      {children}
    </CapabilitiesContext.Provider>
  );
}

export function useCapabilities() {
  const context = useContext(CapabilitiesContext);
  if (!context) {
    throw new Error('useCapabilities must be used within a CapabilitiesProvider');
  }
  return context;
}
