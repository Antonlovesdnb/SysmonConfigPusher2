// API Types - matching backend DTOs

// User and Auth Types
export interface UserInfo {
  username: string;
  displayName: string;
  roles: string[];
  highestRole: 'Admin' | 'Operator' | 'Viewer' | 'None';
  isAdmin: boolean;
  isOperator: boolean;
  canDeploy: boolean;
  canManageConfigs: boolean;
}

export interface Computer {
  id: number;
  hostname: string;
  distinguishedName: string | null;
  operatingSystem: string | null;
  lastSeen: string | null;
  sysmonVersion: string | null;
  sysmonPath: string | null;
  configHash: string | null;
  configTag: string | null;
  lastDeployment: string | null;
  lastInventoryScan: string | null;
  lastScanStatus: 'Online' | 'Offline' | null;
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
  isValid: boolean;
  validationMessage: string | null;
  sourceUrl: string | null;
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

// Scheduled Deployment Types
export interface ScheduledDeployment {
  id: number;
  operation: string;
  configId: number | null;
  configFilename: string | null;
  configTag: string | null;
  scheduledAt: string;
  createdBy: string | null;
  createdAt: string;
  status: string;
  deploymentJobId: number | null;
  computers: { computerId: number; hostname: string }[];
}

// Deployment operations
export type DeploymentOperation = 'install' | 'update' | 'uninstall' | 'test';

export const DEPLOYMENT_OPERATIONS: { value: DeploymentOperation; label: string; description: string; requiresConfig: boolean }[] = [
  { value: 'install', label: 'Install Sysmon', description: 'Install Sysmon with optional config', requiresConfig: false },
  { value: 'update', label: 'Update Config', description: 'Push new configuration to existing Sysmon', requiresConfig: true },
  { value: 'uninstall', label: 'Uninstall Sysmon', description: 'Remove Sysmon from target hosts', requiresConfig: false },
  { value: 'test', label: 'Test Connectivity', description: 'Test WMI connectivity to hosts', requiresConfig: false },
];

// Sysmon Event Types
export const SYSMON_EVENT_TYPES = [
  { id: 1, name: 'Process Create' },
  { id: 2, name: 'File Creation Time Changed' },
  { id: 3, name: 'Network Connection' },
  { id: 4, name: 'Sysmon Service State Changed' },
  { id: 5, name: 'Process Terminated' },
  { id: 6, name: 'Driver Loaded' },
  { id: 7, name: 'Image Loaded' },
  { id: 8, name: 'CreateRemoteThread' },
  { id: 9, name: 'RawAccessRead' },
  { id: 10, name: 'Process Access' },
  { id: 11, name: 'File Create' },
  { id: 12, name: 'Registry Object Create/Delete' },
  { id: 13, name: 'Registry Value Set' },
  { id: 14, name: 'Registry Key/Value Rename' },
  { id: 15, name: 'File Create Stream Hash' },
  { id: 16, name: 'Sysmon Config Change' },
  { id: 17, name: 'Pipe Created' },
  { id: 18, name: 'Pipe Connected' },
  { id: 19, name: 'WMI Filter Activity' },
  { id: 20, name: 'WMI Consumer Activity' },
  { id: 21, name: 'WMI Consumer-Filter Binding' },
  { id: 22, name: 'DNS Query' },
  { id: 23, name: 'File Delete Archived' },
  { id: 24, name: 'Clipboard Change' },
  { id: 25, name: 'Process Tampering' },
  { id: 26, name: 'File Delete Logged' },
  { id: 27, name: 'File Block Executable' },
  { id: 28, name: 'File Block Shredding' },
  { id: 29, name: 'File Executable Detected' },
] as const;

// Event Viewer Types
export interface SysmonEvent {
  computerId: number;
  hostname: string;
  eventId: number;
  eventType: string;
  timeCreated: string;
  processName: string | null;
  processId: number | null;
  image: string | null;
  commandLine: string | null;
  user: string | null;
  parentProcessName: string | null;
  parentProcessId: number | null;
  parentImage: string | null;
  parentCommandLine: string | null;
  destinationIp: string | null;
  destinationPort: number | null;
  destinationHostname: string | null;
  sourceIp: string | null;
  sourcePort: number | null;
  protocol: string | null;
  targetFilename: string | null;
  queryName: string | null;
  queryResults: string | null;
  imageLoaded: string | null;
  signature: string | null;
  rawXml: string | null;
}

export interface EventQueryRequest {
  computerIds: number[];
  eventId?: number;
  startTime?: string;
  endTime?: string;
  processName?: string;
  imagePath?: string;
  destinationIp?: string;
  dnsQueryName?: string;
  maxResults?: number;
}

export interface EventQueryResponse {
  success: boolean;
  events: SysmonEvent[];
  totalCount: number;
  errorMessage: string | null;
}

export interface EventTypeStat {
  eventId: number;
  eventType: string;
  count: number;
}

export interface EventStatsResponse {
  success: boolean;
  computerId: number;
  hostname: string;
  totalEvents: number;
  eventTypeCounts: EventTypeStat[];
  errorMessage: string | null;
}

// Noise Analysis Types
export type NoiseLevel = 'Normal' | 'Noisy' | 'VeryNoisy';

export interface NoiseAnalysisRun {
  id: number;
  computerId: number;
  hostname: string;
  timeRangeHours: number;
  totalEvents: number;
  analyzedAt: string;
}

export interface NoiseResult {
  id: number;
  eventId: number;
  eventType: string;
  groupingKey: string;
  eventCount: number;
  eventsPerHour: number;
  noiseScore: number;
  noiseLevel: NoiseLevel;
  suggestedExclusion: string | null;
  availableFields: Record<string, string>;
}

export interface EventTypeSummary {
  eventId: number;
  eventType: string;
  totalCount: number;
  eventsPerHour: number;
  patternCount: number;
  topPatterns: NoiseResult[];
}

export interface NoiseAnalysisResponse {
  success: boolean;
  run: NoiseAnalysisRun | null;
  results: NoiseResult[];
  eventSummaries: EventTypeSummary[];
  errorMessage: string | null;
}

export interface HostComparison {
  computerId: number;
  hostname: string;
  totalEvents: number;
  noisyPatterns: number;
  veryNoisyPatterns: number;
  overallNoiseScore: number;
  topNoisePatterns: NoiseResult[];
}

export interface CrossHostAnalysisResponse {
  success: boolean;
  comparisons: HostComparison[];
  commonNoisePatterns: string[];
  errorMessage: string | null;
}

export interface ExclusionXmlResponse {
  success: boolean;
  xml: string | null;
  errorMessage: string | null;
}

export interface NoiseThresholds {
  role: string;
  processCreatePerHour: number;
  networkConnectionPerHour: number;
  imageLoadedPerHour: number;
  fileCreatePerHour: number;
  dnsQueryPerHour: number;
}

// Settings Types
export interface AuthorizationSettings {
  adminGroup: string;
  operatorGroup: string;
  viewerGroup: string;
  defaultRole: string;
}

export interface SysmonConfigPusherSettings {
  sysmonBinaryUrl: string;
  defaultParallelism: number;
  remoteDirectory: string;
  auditLogPath: string;
}

export interface AppSettings {
  authorization: AuthorizationSettings;
  sysmonConfigPusher: SysmonConfigPusherSettings;
}

export interface UpdateSettingsResult {
  success: boolean;
  message: string;
  restartRequired: boolean;
}

// Config Diff Types
export interface ConfigDiff {
  config1: Config;
  config2: Config;
  lines1: string[];
  lines2: string[];
}

// Dashboard Types
export interface DashboardStats {
  computers: {
    total: number;
    withSysmon: number;
    withoutSysmon: number;
  };
  totalConfigs: number;
  deployments: {
    last24Hours: number;
    last7Days: number;
    successRate: number;
  };
  recentDeployments: {
    id: number;
    operation: string;
    startedBy: string;
    startedAt: string;
    completedAt: string | null;
    status: string;
  }[];
  noiseAnalysis: {
    last7Days: number;
  };
  recentNoiseAnalyses: {
    id: number;
    hostname: string;
    totalEvents: number;
    analyzedAt: string;
  }[];
  sysmonVersions: {
    version: string;
    count: number;
  }[];
}
