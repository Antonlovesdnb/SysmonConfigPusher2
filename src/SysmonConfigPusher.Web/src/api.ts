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
  EventQueryRequest,
  EventQueryResponse,
  EventStatsResponse,
  NoiseAnalysisRun,
  NoiseAnalysisResponse,
  CrossHostAnalysisResponse,
  ExclusionXmlResponse,
  NoiseThresholds,
  UserInfo,
  AppSettings,
  UpdateSettingsResult,
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
    // Request all computers (up to 2000) for backward compatibility
    params.set('take', '2000');
    const query = params.toString();
    const response = await fetchWithAuth(`/api/computers?${query}`);
    if (!response.ok) throw new Error(`Failed to fetch computers: ${response.status}`);
    const data = await response.json();
    // API now returns { items, totalCount, skip, take } - extract items for compatibility
    return data.items ?? data;
  },

  async listPaged(skip: number = 0, take: number = 100, search?: string): Promise<{ items: Computer[]; totalCount: number }> {
    const params = new URLSearchParams();
    params.set('skip', skip.toString());
    params.set('take', take.toString());
    if (search) params.set('search', search);
    const response = await fetchWithAuth(`/api/computers?${params.toString()}`);
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

  async update(id: number, content: string): Promise<ConfigDetail> {
    const response = await fetchWithAuth(`/api/configs/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content }),
    });
    if (!response.ok) throw new Error(`Failed to update config: ${response.status}`);
    return response.json();
  },

  async addExclusion(
    configId: number,
    eventId: number,
    fieldName: string,
    value: string,
    condition: string = 'is'
  ): Promise<{ success: boolean; updatedContent: string | null; message: string | null }> {
    const response = await fetchWithAuth(`/api/configs/${configId}/exclusions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ eventId, fieldName, value, condition }),
    });
    if (!response.ok) throw new Error(`Failed to add exclusion: ${response.status}`);
    return response.json();
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

  async purge(olderThanDays: number = 30): Promise<{ jobsDeleted: number; resultsDeleted: number; message: string }> {
    const response = await fetchWithAuth(`/api/deployments?olderThanDays=${olderThanDays}`, { method: 'DELETE' });
    if (!response.ok) throw new Error(`Failed to purge history: ${response.status}`);
    return response.json();
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

// Events API
export const eventsApi = {
  async query(request: EventQueryRequest): Promise<EventQueryResponse> {
    const response = await fetchWithAuth('/api/events/query', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    if (!response.ok) throw new Error(`Failed to query events: ${response.status}`);
    return response.json();
  },

  async getStats(computerId: number, hours: number = 24): Promise<EventStatsResponse> {
    const response = await fetchWithAuth(`/api/events/stats/${computerId}?hours=${hours}`);
    if (!response.ok) throw new Error(`Failed to get event stats: ${response.status}`);
    return response.json();
  },

  async testAccess(computerId: number): Promise<{ accessible: boolean; errorMessage: string | null }> {
    const response = await fetchWithAuth(`/api/events/test/${computerId}`);
    if (!response.ok) throw new Error(`Failed to test event log access: ${response.status}`);
    return response.json();
  },
};

// Analysis API
export const analysisApi = {
  async startNoiseAnalysis(computerId: number, timeRangeHours: number = 24): Promise<NoiseAnalysisResponse> {
    const response = await fetchWithAuth('/api/analysis/noise', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ computerId, timeRangeHours }),
    });
    if (!response.ok) throw new Error(`Failed to start noise analysis: ${response.status}`);
    return response.json();
  },

  async getNoiseAnalysis(runId: number): Promise<NoiseAnalysisResponse> {
    const response = await fetchWithAuth(`/api/analysis/noise/${runId}`);
    if (!response.ok) throw new Error(`Failed to get noise analysis: ${response.status}`);
    return response.json();
  },

  async getNoiseHistory(computerId?: number, limit: number = 20): Promise<NoiseAnalysisRun[]> {
    const params = new URLSearchParams();
    if (computerId) params.set('computerId', computerId.toString());
    params.set('limit', limit.toString());
    const query = params.toString();
    const response = await fetchWithAuth(`/api/analysis/noise/history?${query}`);
    if (!response.ok) throw new Error(`Failed to get noise history: ${response.status}`);
    return response.json();
  },

  async compareHosts(computerIds: number[], timeRangeHours: number = 24): Promise<CrossHostAnalysisResponse> {
    const response = await fetchWithAuth('/api/analysis/compare', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ computerIds, timeRangeHours }),
    });
    if (!response.ok) throw new Error(`Failed to compare hosts: ${response.status}`);
    return response.json();
  },

  async getExclusionXml(runId: number, minimumNoiseScore: number = 0.5): Promise<ExclusionXmlResponse> {
    const response = await fetchWithAuth(`/api/analysis/exclusions/${runId}?minimumNoiseScore=${minimumNoiseScore}`);
    if (!response.ok) throw new Error(`Failed to get exclusion XML: ${response.status}`);
    return response.json();
  },

  async getThresholds(role: string): Promise<NoiseThresholds> {
    const response = await fetchWithAuth(`/api/analysis/thresholds/${role}`);
    if (!response.ok) throw new Error(`Failed to get thresholds: ${response.status}`);
    return response.json();
  },
};

// Auth API
export const authApi = {
  async getCurrentUser(): Promise<UserInfo> {
    const response = await fetchWithAuth('/api/auth/me');
    if (!response.ok) throw new Error(`Failed to get user info: ${response.status}`);
    return response.json();
  },
};

// Settings API
export const settingsApi = {
  async get(): Promise<AppSettings> {
    const response = await fetchWithAuth('/api/settings');
    if (!response.ok) throw new Error(`Failed to get settings: ${response.status}`);
    return response.json();
  },

  async update(settings: AppSettings): Promise<UpdateSettingsResult> {
    const response = await fetchWithAuth('/api/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    });
    if (!response.ok) throw new Error(`Failed to update settings: ${response.status}`);
    return response.json();
  },

  async getBinaryCacheStatus(): Promise<BinaryCacheStatus> {
    const response = await fetchWithAuth('/api/settings/binary-cache');
    if (!response.ok) throw new Error(`Failed to get binary cache status: ${response.status}`);
    return response.json();
  },

  async getTlsStatus(): Promise<TlsCertificateStatus> {
    const response = await fetchWithAuth('/api/settings/tls-status');
    if (!response.ok) throw new Error(`Failed to get TLS status: ${response.status}`);
    return response.json();
  },

  async updateBinaryCache(): Promise<BinaryCacheUpdateResult> {
    const response = await fetchWithAuth('/api/settings/binary-cache/update', { method: 'POST' });
    if (!response.ok) throw new Error(`Failed to update binary cache: ${response.status}`);
    return response.json();
  },

  async restart(): Promise<RestartResult> {
    const response = await fetchWithAuth('/api/settings/restart', { method: 'POST' });
    if (!response.ok) throw new Error(`Failed to restart service: ${response.status}`);
    return response.json();
  },
};

export interface BinaryCacheStatus {
  isCached: boolean;
  filePath: string | null;
  version: string | null;
  fileSizeBytes: number | null;
  cachedAt: string | null;
}

export interface TlsCertificateStatus {
  configurationType: string;
  configuredPath: string | null;
  isDevelopmentCertificate: boolean;
  subject: string | null;
  issuer: string | null;
  thumbprint: string | null;
  notBefore: string | null;
  notAfter: string | null;
  isValid: boolean;
  daysUntilExpiry: number | null;
  errorMessage: string | null;
}

export interface BinaryCacheUpdateResult {
  success: boolean;
  message: string;
  version: string | null;
  fileSizeBytes: number | null;
  cachedAt: string | null;
}

export interface RestartResult {
  success: boolean;
  message: string;
}
