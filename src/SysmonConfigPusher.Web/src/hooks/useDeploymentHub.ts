import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';

export interface DeploymentProgress {
  jobId: number;
  computerId: number;
  hostname: string;
  status: string;
  success: boolean;
  message: string | null;
  completed: number;
  total: number;
}

export interface DeploymentCompleted {
  jobId: number;
  success: boolean;
  message: string;
}

export function useDeploymentHub(jobId: number | null) {
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const [connected, setConnected] = useState(false);
  const [progress, setProgress] = useState<DeploymentProgress[]>([]);
  const [completed, setCompleted] = useState<DeploymentCompleted | null>(null);

  const connect = useCallback(async () => {
    if (!jobId || connectionRef.current) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/deployment')
      .withAutomaticReconnect()
      .build();

    connection.on('DeploymentProgress', (msg: DeploymentProgress) => {
      if (msg.jobId === jobId) {
        setProgress((prev) => {
          const existing = prev.findIndex((p) => p.computerId === msg.computerId);
          if (existing >= 0) {
            const next = [...prev];
            next[existing] = msg;
            return next;
          }
          return [...prev, msg];
        });
      }
    });

    connection.on('DeploymentCompleted', (id: number, success: boolean, message: string) => {
      if (id === jobId) {
        setCompleted({ jobId: id, success, message });
      }
    });

    try {
      await connection.start();
      await connection.invoke('JoinDeploymentGroup', jobId);
      connectionRef.current = connection;
      setConnected(true);
    } catch (err) {
      console.error('Failed to connect to SignalR hub:', err);
    }
  }, [jobId]);

  const disconnect = useCallback(async () => {
    if (connectionRef.current) {
      try {
        if (jobId) {
          await connectionRef.current.invoke('LeaveDeploymentGroup', jobId);
        }
        await connectionRef.current.stop();
      } catch (err) {
        console.error('Error disconnecting:', err);
      }
      connectionRef.current = null;
      setConnected(false);
    }
  }, [jobId]);

  useEffect(() => {
    connect();
    return () => {
      disconnect();
    };
  }, [connect, disconnect]);

  const reset = useCallback(() => {
    setProgress([]);
    setCompleted(null);
  }, []);

  return { connected, progress, completed, reset };
}
