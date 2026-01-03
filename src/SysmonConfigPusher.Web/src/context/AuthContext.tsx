import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { authApi, getStoredApiKey, setStoredApiKey } from '../api';
import type { UserInfo, AuthenticationMode } from '../types';

interface AuthContextType {
  user: UserInfo | null;
  loading: boolean;
  error: string | null;
  isAdmin: boolean;
  isOperator: boolean;
  canDeploy: boolean;
  canManageConfigs: boolean;
  authMode: AuthenticationMode | null;
  needsApiKey: boolean;
  refresh: () => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [authMode, setAuthMode] = useState<AuthenticationMode | null>(null);
  const [needsApiKey, setNeedsApiKey] = useState(false);

  const refresh = async () => {
    try {
      setLoading(true);

      // First, check the auth mode
      const modeInfo = await authApi.getAuthMode();
      setAuthMode(modeInfo.mode);

      // If API key mode and no key stored, user needs to log in
      if (modeInfo.requiresApiKey && !getStoredApiKey()) {
        setNeedsApiKey(true);
        setUser(null);
        setError(null);
        return;
      }

      // Try to get user info
      const userInfo = await authApi.getCurrentUser();
      setUser(userInfo);
      setNeedsApiKey(false);
      setError(null);
    } catch (err) {
      // If auth failed and we're in API key mode, clear the stored key
      if (authMode === 'ApiKey') {
        setStoredApiKey(null);
        setNeedsApiKey(true);
      }
      setError(err instanceof Error ? err.message : 'Failed to load user info');
      setUser(null);
    } finally {
      setLoading(false);
    }
  };

  const logout = () => {
    setStoredApiKey(null);
    setUser(null);
    if (authMode === 'ApiKey') {
      setNeedsApiKey(true);
    }
  };

  useEffect(() => {
    refresh();
  }, []);

  return (
    <AuthContext.Provider
      value={{
        user,
        loading,
        error,
        isAdmin: user?.isAdmin ?? false,
        isOperator: user?.isOperator ?? false,
        canDeploy: user?.canDeploy ?? false,
        canManageConfigs: user?.canManageConfigs ?? false,
        authMode,
        needsApiKey,
        refresh,
        logout,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
