// Sysmon Configuration Schema for XML Editor Auto-complete
// Derived from `sysmon -s` schema output

// Filter condition types
export const SYSMON_CONDITIONS = [
  'is',
  'is not',
  'contains',
  'contains any',
  'is any',
  'contains all',
  'excludes',
  'excludes any',
  'excludes all',
  'begin with',
  'not begin with',
  'end with',
  'not end with',
  'less than',
  'more than',
  'image',
] as const;

// Root-level and structural elements
export const SYSMON_STRUCTURE_ELEMENTS = [
  'Sysmon',
  'EventFiltering',
  'RuleGroup',
  'HashAlgorithms',
  'CheckRevocation',
  'DnsLookup',
  'ArchiveDirectory',
  'CaptureClipboard',
] as const;

// Event filter element names (rulename attribute values)
export const SYSMON_EVENT_ELEMENTS = {
  ProcessCreate: {
    eventId: 1,
    description: 'Process Create',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'Image',
      'FileVersion',
      'Description',
      'Product',
      'Company',
      'OriginalFileName',
      'CommandLine',
      'CurrentDirectory',
      'User',
      'LogonGuid',
      'LogonId',
      'TerminalSessionId',
      'IntegrityLevel',
      'Hashes',
      'ParentProcessGuid',
      'ParentProcessId',
      'ParentImage',
      'ParentCommandLine',
      'ParentUser',
    ],
  },
  FileCreateTime: {
    eventId: 2,
    description: 'File creation time changed',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'Image',
      'TargetFilename',
      'CreationUtcTime',
      'PreviousCreationUtcTime',
      'User',
    ],
  },
  NetworkConnect: {
    eventId: 3,
    description: 'Network connection detected',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'Image',
      'User',
      'Protocol',
      'Initiated',
      'SourceIsIpv6',
      'SourceIp',
      'SourceHostname',
      'SourcePort',
      'SourcePortName',
      'DestinationIsIpv6',
      'DestinationIp',
      'DestinationHostname',
      'DestinationPort',
      'DestinationPortName',
    ],
  },
  ProcessTerminate: {
    eventId: 5,
    description: 'Process terminated',
    fields: ['RuleName', 'UtcTime', 'ProcessGuid', 'ProcessId', 'Image', 'User'],
  },
  DriverLoad: {
    eventId: 6,
    description: 'Driver loaded',
    fields: [
      'RuleName',
      'UtcTime',
      'ImageLoaded',
      'Hashes',
      'Signed',
      'Signature',
      'SignatureStatus',
    ],
  },
  ImageLoad: {
    eventId: 7,
    description: 'Image loaded',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'Image',
      'ImageLoaded',
      'FileVersion',
      'Description',
      'Product',
      'Company',
      'OriginalFileName',
      'Hashes',
      'Signed',
      'Signature',
      'SignatureStatus',
      'User',
    ],
  },
  CreateRemoteThread: {
    eventId: 8,
    description: 'CreateRemoteThread detected',
    fields: [
      'RuleName',
      'UtcTime',
      'SourceProcessGuid',
      'SourceProcessId',
      'SourceImage',
      'TargetProcessGuid',
      'TargetProcessId',
      'TargetImage',
      'NewThreadId',
      'StartAddress',
      'StartModule',
      'StartFunction',
      'SourceUser',
      'TargetUser',
    ],
  },
  RawAccessRead: {
    eventId: 9,
    description: 'RawAccessRead detected',
    fields: ['RuleName', 'UtcTime', 'ProcessGuid', 'ProcessId', 'Image', 'Device', 'User'],
  },
  ProcessAccess: {
    eventId: 10,
    description: 'Process accessed',
    fields: [
      'RuleName',
      'UtcTime',
      'SourceProcessGUID',
      'SourceProcessId',
      'SourceThreadId',
      'SourceImage',
      'TargetProcessGUID',
      'TargetProcessId',
      'TargetImage',
      'GrantedAccess',
      'CallTrace',
      'SourceUser',
      'TargetUser',
    ],
  },
  FileCreate: {
    eventId: 11,
    description: 'File created',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'Image',
      'TargetFilename',
      'CreationUtcTime',
      'User',
    ],
  },
  RegistryEvent: {
    eventId: [12, 13, 14],
    description: 'Registry object added, deleted, or renamed',
    fields: [
      'RuleName',
      'EventType',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'Image',
      'TargetObject',
      'Details',
      'NewName',
      'User',
    ],
  },
  FileCreateStreamHash: {
    eventId: 15,
    description: 'File stream created',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'Image',
      'TargetFilename',
      'CreationUtcTime',
      'Hash',
      'Contents',
      'User',
    ],
  },
  PipeEvent: {
    eventId: [17, 18],
    description: 'Pipe created or connected',
    fields: [
      'RuleName',
      'EventType',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'PipeName',
      'Image',
      'User',
    ],
  },
  WmiEvent: {
    eventId: [19, 20, 21],
    description: 'WMI filter, consumer, or binding activity',
    fields: [
      'RuleName',
      'EventType',
      'UtcTime',
      'Operation',
      'User',
      'EventNamespace',
      'Name',
      'Query',
      'Type',
      'Destination',
      'Consumer',
      'Filter',
    ],
  },
  DnsQuery: {
    eventId: 22,
    description: 'DNS query',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'QueryName',
      'QueryStatus',
      'QueryResults',
      'Image',
      'User',
    ],
  },
  FileDelete: {
    eventId: [23, 26],
    description: 'File deleted',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'User',
      'Image',
      'TargetFilename',
      'Hashes',
      'IsExecutable',
      'Archived',
    ],
  },
  ClipboardChange: {
    eventId: 24,
    description: 'Clipboard changed',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'Image',
      'Session',
      'ClientInfo',
      'Hashes',
      'Archived',
      'User',
    ],
  },
  ProcessTampering: {
    eventId: 25,
    description: 'Process tampering',
    fields: ['RuleName', 'UtcTime', 'ProcessGuid', 'ProcessId', 'Image', 'Type', 'User'],
  },
  FileBlockExecutable: {
    eventId: 27,
    description: 'File block executable',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'User',
      'Image',
      'TargetFilename',
      'Hashes',
    ],
  },
  FileBlockShredding: {
    eventId: 28,
    description: 'File block shredding',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'User',
      'Image',
      'TargetFilename',
      'Hashes',
      'IsExecutable',
    ],
  },
  FileExecutableDetected: {
    eventId: 29,
    description: 'File executable detected',
    fields: [
      'RuleName',
      'UtcTime',
      'ProcessGuid',
      'ProcessId',
      'User',
      'Image',
      'TargetFilename',
      'Hashes',
    ],
  },
} as const;

// Get all event element names
export const EVENT_ELEMENT_NAMES = Object.keys(SYSMON_EVENT_ELEMENTS);

// Get all unique field names across all events
export const ALL_FIELD_NAMES = [
  ...new Set(
    Object.values(SYSMON_EVENT_ELEMENTS).flatMap((event) => event.fields)
  ),
].sort();

// Attributes for elements
export const SYSMON_ATTRIBUTES = {
  Sysmon: ['schemaversion'],
  RuleGroup: ['name', 'groupRelation'],
  // Event filter attributes
  ...Object.fromEntries(
    EVENT_ELEMENT_NAMES.map((name) => [name, ['onmatch']])
  ),
  // Field attributes (all fields can have condition)
  ...Object.fromEntries(ALL_FIELD_NAMES.map((name) => [name, ['condition', 'name']])),
} as const;

// Attribute values
export const ATTRIBUTE_VALUES = {
  onmatch: ['include', 'exclude'],
  groupRelation: ['and', 'or'],
  condition: SYSMON_CONDITIONS,
  schemaversion: ['4.90', '4.80', '4.70', '4.60', '4.50'],
} as const;

// Get fields for a specific event element
export function getFieldsForEvent(eventElement: string): string[] {
  const event = SYSMON_EVENT_ELEMENTS[eventElement as keyof typeof SYSMON_EVENT_ELEMENTS];
  return event ? [...event.fields] : [];
}

// Get description for an event element
export function getEventDescription(eventElement: string): string {
  const event = SYSMON_EVENT_ELEMENTS[eventElement as keyof typeof SYSMON_EVENT_ELEMENTS];
  return event ? event.description : '';
}
