// API Types - matching backend DTOs

export interface Computer {
  id: number;
  hostname: string;
  distinguishedName: string | null;
  operatingSystem: string | null;
  lastSeen: string | null;
  sysmonVersion: string | null;
  configHash: string | null;
  lastDeployment: string | null;
}

export interface ComputerGroup {
  id: number;
  name: string;
  createdBy: string | null;
  createdAt: string;
  memberCount: number;
}

export interface Config {
  id: number;
  filename: string;
  tag: string | null;
  hash: string;
  uploadedBy: string | null;
  uploadedAt: string;
}

export interface ConfigDetail extends Config {
  content: string;
  isActive: boolean;
}

export interface DeploymentJob {
  id: number;
  operation: string;
  configId: number | null;
  configFilename: string | null;
  startedBy: string | null;
  startedAt: string;
  completedAt: string | null;
  status: string;
  successCount: number;
  failureCount: number;
  totalCount: number;
}

export interface DeploymentResult {
  id: number;
  computerId: number;
  hostname: string;
  success: boolean;
  message: string | null;
  completedAt: string | null;
}

export interface DeploymentJobDetail extends Omit<DeploymentJob, 'successCount' | 'failureCount' | 'totalCount'> {
  results: DeploymentResult[];
}

export interface RefreshResult {
  added: number;
  updated: number;
  message: string;
}

export interface ConnectivityResult {
  computerId: number;
  reachable: boolean;
  message: string | null;
}

// Deployment operations
export type DeploymentOperation = 'install' | 'update' | 'uninstall' | 'test';

export const DEPLOYMENT_OPERATIONS: { value: DeploymentOperation; label: string; description: string; requiresConfig: boolean }[] = [
  { value: 'install', label: 'Install Sysmon', description: 'Install Sysmon with optional config', requiresConfig: false },
  { value: 'update', label: 'Update Config', description: 'Push new configuration to existing Sysmon', requiresConfig: true },
  { value: 'uninstall', label: 'Uninstall Sysmon', description: 'Remove Sysmon from target hosts', requiresConfig: false },
  { value: 'test', label: 'Test Connectivity', description: 'Test WMI connectivity to hosts', requiresConfig: false },
];
