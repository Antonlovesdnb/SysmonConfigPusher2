import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { authApi } from '../api';
import type { UserInfo } from '../types';

interface AuthContextType {
  user: UserInfo | null;
  loading: boolean;
  error: string | null;
  isAdmin: boolean;
  isOperator: boolean;
  canDeploy: boolean;
  canManageConfigs: boolean;
  refresh: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = async () => {
    try {
      setLoading(true);
      const userInfo = await authApi.getCurrentUser();
      setUser(userInfo);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load user info');
      setUser(null);
    } finally {
      setLoading(false);
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
        refresh,
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
