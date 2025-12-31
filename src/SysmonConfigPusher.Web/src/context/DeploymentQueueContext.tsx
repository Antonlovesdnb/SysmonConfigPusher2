import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import type { DeploymentOperation } from '../types';

export interface QueuedDeployment {
  id: string;
  operation: DeploymentOperation;
  operationLabel: string;
  config: { id: number; filename: string; tag: string | null } | null;
  sysmonVersion: string | null;
  computers: { id: number; hostname: string }[];
  addedAt: string;
  scheduledAt: string | null;
}

interface DeploymentQueueContextType {
  queue: QueuedDeployment[];
  addToQueue: (deployment: Omit<QueuedDeployment, 'id' | 'addedAt' | 'scheduledAt'>) => void;
  removeFromQueue: (id: string) => void;
  updateScheduledAt: (id: string, scheduledAt: string | null) => void;
  clearQueue: () => void;
  isQueuePanelOpen: boolean;
  setQueuePanelOpen: (open: boolean) => void;
  toggleQueuePanel: () => void;
}

const DeploymentQueueContext = createContext<DeploymentQueueContextType | null>(null);

const STORAGE_KEY = 'sysmon-deployment-queue';

export function DeploymentQueueProvider({ children }: { children: ReactNode }) {
  const [queue, setQueue] = useState<QueuedDeployment[]>(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored ? JSON.parse(stored) : [];
    } catch {
      return [];
    }
  });
  const [isQueuePanelOpen, setQueuePanelOpen] = useState(false);

  // Persist to localStorage
  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(queue));
  }, [queue]);

  const addToQueue = useCallback((deployment: Omit<QueuedDeployment, 'id' | 'addedAt' | 'scheduledAt'>) => {
    const newItem: QueuedDeployment = {
      ...deployment,
      id: crypto.randomUUID(),
      addedAt: new Date().toISOString(),
      scheduledAt: null,
    };
    setQueue((prev) => [...prev, newItem]);
  }, []);

  const removeFromQueue = useCallback((id: string) => {
    setQueue((prev) => prev.filter((item) => item.id !== id));
  }, []);

  const updateScheduledAt = useCallback((id: string, scheduledAt: string | null) => {
    setQueue((prev) =>
      prev.map((item) =>
        item.id === id ? { ...item, scheduledAt } : item
      )
    );
  }, []);

  const clearQueue = useCallback(() => {
    setQueue([]);
  }, []);

  const toggleQueuePanel = useCallback(() => {
    setQueuePanelOpen((prev) => !prev);
  }, []);

  return (
    <DeploymentQueueContext.Provider
      value={{
        queue,
        addToQueue,
        removeFromQueue,
        updateScheduledAt,
        clearQueue,
        isQueuePanelOpen,
        setQueuePanelOpen,
        toggleQueuePanel,
      }}
    >
      {children}
    </DeploymentQueueContext.Provider>
  );
}

export function useDeploymentQueue() {
  const context = useContext(DeploymentQueueContext);
  if (!context) {
    throw new Error('useDeploymentQueue must be used within a DeploymentQueueProvider');
  }
  return context;
}
