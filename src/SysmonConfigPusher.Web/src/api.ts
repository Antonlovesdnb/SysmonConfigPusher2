// API client for SysmonConfigPusher
import type {
  Computer,
  ComputerGroup,
  Config,
  ConfigDetail,
  DeploymentJob,
  DeploymentJobDetail,
  RefreshResult,
  ConnectivityResult,
} from './types';

const fetchWithAuth = async (url: string, options: RequestInit = {}): Promise<Response> => {
  const response = await fetch(url, {
    ...options,
    credentials: 'include',
    headers: {
      ...options.headers,
    },
  });
  return response;
};

// Computers API
export const computersApi = {
  async list(search?: string, groupId?: number): Promise<Computer[]> {
    const params = new URLSearchParams();
    if (search) params.set('search', search);
    if (groupId) params.set('groupId', groupId.toString());
    const query = params.toString();
    const response = await fetchWithAuth(`/api/computers${query ? `?${query}` : ''}`);
    if (!response.ok) throw new Error(`Failed to fetch computers: ${response.status}`);
    return response.json();
  },

  async get(id: number): Promise<Computer> {
    const response = await fetchWithAuth(`/api/computers/${id}`);
    if (!response.ok) throw new Error(`Failed to fetch computer: ${response.status}`);
    return response.json();
  },

  async refresh(): Promise<RefreshResult> {
    const response = await fetchWithAuth('/api/computers/refresh', { method: 'POST' });
    if (!response.ok) throw new Error(`Failed to refresh: ${response.status}`);
    return response.json();
  },

  async testConnectivity(computerIds: number[]): Promise<ConnectivityResult[]> {
    const response = await fetchWithAuth('/api/computers/test-connectivity', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(computerIds),
    });
    if (!response.ok) throw new Error(`Failed to test connectivity: ${response.status}`);
    return response.json();
  },

  async getGroups(): Promise<ComputerGroup[]> {
    const response = await fetchWithAuth('/api/computers/groups');
    if (!response.ok) throw new Error(`Failed to fetch groups: ${response.status}`);
    return response.json();
  },

  async createGroup(name: string, computerIds: number[]): Promise<ComputerGroup> {
    const response = await fetchWithAuth('/api/computers/groups', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, computerIds }),
    });
    if (!response.ok) throw new Error(`Failed to create group: ${response.status}`);
    return response.json();
  },

  async scan(computerIds?: number[]): Promise<{ message: string }> {
    const response = await fetchWithAuth('/api/computers/scan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ computerIds }),
    });
    if (!response.ok) throw new Error(`Failed to start scan: ${response.status}`);
    return response.json();
  },

  async scanAll(): Promise<{ message: string }> {
    const response = await fetchWithAuth('/api/computers/scan/all', { method: 'POST' });
    if (!response.ok) throw new Error(`Failed to start scan: ${response.status}`);
    return response.json();
  },
};

// Configs API
export const configsApi = {
  async list(): Promise<Config[]> {
    const response = await fetchWithAuth('/api/configs');
    if (!response.ok) throw new Error(`Failed to fetch configs: ${response.status}`);
    return response.json();
  },

  async get(id: number): Promise<ConfigDetail> {
    const response = await fetchWithAuth(`/api/configs/${id}`);
    if (!response.ok) throw new Error(`Failed to fetch config: ${response.status}`);
    return response.json();
  },

  async upload(file: File): Promise<Config> {
    const formData = new FormData();
    formData.append('file', file);
    const response = await fetchWithAuth('/api/configs', {
      method: 'POST',
      body: formData,
    });
    if (!response.ok) throw new Error(`Failed to upload config: ${response.status}`);
    return response.json();
  },

  async delete(id: number): Promise<void> {
    const response = await fetchWithAuth(`/api/configs/${id}`, { method: 'DELETE' });
    if (!response.ok) throw new Error(`Failed to delete config: ${response.status}`);
  },
};

// Deployments API
export const deploymentsApi = {
  async list(limit?: number): Promise<DeploymentJob[]> {
    const query = limit ? `?limit=${limit}` : '';
    const response = await fetchWithAuth(`/api/deployments${query}`);
    if (!response.ok) throw new Error(`Failed to fetch deployments: ${response.status}`);
    return response.json();
  },

  async get(id: number): Promise<DeploymentJobDetail> {
    const response = await fetchWithAuth(`/api/deployments/${id}`);
    if (!response.ok) throw new Error(`Failed to fetch deployment: ${response.status}`);
    return response.json();
  },

  async start(operation: string, computerIds: number[], configId?: number): Promise<DeploymentJob> {
    const response = await fetchWithAuth('/api/deployments', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ operation, computerIds, configId }),
    });
    if (!response.ok) throw new Error(`Failed to start deployment: ${response.status}`);
    return response.json();
  },

  async cancel(id: number): Promise<void> {
    const response = await fetchWithAuth(`/api/deployments/${id}`, { method: 'DELETE' });
    if (!response.ok) throw new Error(`Failed to cancel deployment: ${response.status}`);
  },
};

// Health check
export const healthApi = {
  async check(): Promise<string> {
    const response = await fetch('/health');
    if (!response.ok) return 'Unhealthy';
    return response.text();
  },
};
