import { useState, useEffect } from 'react';
import { settingsApi, type BinaryCacheStatus, type TlsCertificateStatus } from '../api';
import type { AppSettings } from '../types';
import { useAuth } from '../context/AuthContext';
import { useUserPreferences } from '../context/UserPreferencesContext';
import { useCapabilities } from '../context/CapabilitiesContext';

type SettingsTab = 'display' | 'authorization' | 'deployment' | 'agent' | 'binary-cache' | 'tls';

const TAB_CONFIG: { id: SettingsTab; label: string; adminOnly: boolean; icon: React.ReactNode }[] = [
  {
    id: 'display',
    label: 'Display Settings',
    adminOnly: false,
    icon: (
      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
      </svg>
    ),
  },
  {
    id: 'authorization',
    label: 'Authorization Settings',
    adminOnly: true,
    icon: (
      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
      </svg>
    ),
  },
  {
    id: 'deployment',
    label: 'Deployment Settings',
    adminOnly: true,
    icon: (
      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
      </svg>
    ),
  },
  {
    id: 'agent',
    label: 'Agent Settings',
    adminOnly: true,
    icon: (
      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 3v2m6-2v2M9 19v2m6-2v2M5 9H3m2 6H3m18-6h-2m2 6h-2M7 19h10a2 2 0 002-2V7a2 2 0 00-2-2H7a2 2 0 00-2 2v10a2 2 0 002 2zM9 9h6v6H9V9z" />
      </svg>
    ),
  },
  {
    id: 'binary-cache',
    label: 'Sysmon Binary Cache',
    adminOnly: true,
    icon: (
      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
      </svg>
    ),
  },
  {
    id: 'tls',
    label: 'TLS Certificate',
    adminOnly: true,
    icon: (
      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
      </svg>
    ),
  },
];

export function SettingsPage() {
  const { isAdmin } = useAuth();
  const { timestampFormat, setTimestampFormat, formatTimestamp } = useUserPreferences();
  const { isAgentOnlyMode, authMode } = useCapabilities();
  const [activeTab, setActiveTab] = useState<SettingsTab>('display');
  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // Binary cache state
  const [allCachedVersions, setAllCachedVersions] = useState<BinaryCacheStatus[]>([]);
  const [downloadingBinary, setDownloadingBinary] = useState(false);
  const [uploadingBinary, setUploadingBinary] = useState(false);
  const [deletingVersion, setDeletingVersion] = useState<string | null>(null);
  const [binaryCacheMessage, setBinaryCacheMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // TLS certificate state
  const [tlsStatus, setTlsStatus] = useState<TlsCertificateStatus | null>(null);

  // Restart state
  const [restarting, setRestarting] = useState(false);
  const [showRestartConfirm, setShowRestartConfirm] = useState(false);

  useEffect(() => {
    loadSettings();
    loadBinaryCacheStatus();
    loadTlsStatus();
  }, []);

  const loadSettings = async () => {
    setLoading(true);
    try {
      const data = await settingsApi.get();
      setSettings(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load settings');
    } finally {
      setLoading(false);
    }
  };

  const loadBinaryCacheStatus = async () => {
    try {
      const versions = await settingsApi.getAllCachedVersions();
      setAllCachedVersions(versions);
    } catch (err) {
      console.error('Failed to load binary cache status:', err);
    }
  };

  const loadTlsStatus = async () => {
    try {
      const status = await settingsApi.getTlsStatus();
      setTlsStatus(status);
    } catch (err) {
      console.error('Failed to load TLS status:', err);
    }
  };

  const downloadBinary = async () => {
    setDownloadingBinary(true);
    setBinaryCacheMessage(null);
    try {
      const result = await settingsApi.updateBinaryCache();
      if (result.success) {
        setBinaryCacheMessage({ type: 'success', text: result.message });
        await loadBinaryCacheStatus();
      } else {
        setBinaryCacheMessage({ type: 'error', text: result.message });
      }
    } catch (err) {
      setBinaryCacheMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to download binary' });
    } finally {
      setDownloadingBinary(false);
    }
  };

  const uploadBinary = async (file: File) => {
    setUploadingBinary(true);
    setBinaryCacheMessage(null);
    try {
      const result = await settingsApi.uploadBinary(file);
      if (result.success) {
        setBinaryCacheMessage({ type: 'success', text: result.message });
        await loadBinaryCacheStatus();
      } else {
        setBinaryCacheMessage({ type: 'error', text: result.message });
      }
    } catch (err) {
      setBinaryCacheMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to upload binary' });
    } finally {
      setUploadingBinary(false);
    }
  };

  const deleteVersion = async (version: string) => {
    setDeletingVersion(version);
    setBinaryCacheMessage(null);
    try {
      await settingsApi.deleteCachedVersion(version);
      setBinaryCacheMessage({ type: 'success', text: `Deleted Sysmon v${version}` });
      await loadBinaryCacheStatus();
    } catch (err) {
      setBinaryCacheMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to delete version' });
    } finally {
      setDeletingVersion(null);
    }
  };

  const handleRestart = async () => {
    setRestarting(true);
    setShowRestartConfirm(false);
    try {
      await settingsApi.restart();
      setSuccess('Service is restarting. Please wait a few seconds and refresh the page.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to restart service');
    } finally {
      setRestarting(false);
    }
  };

  const handleSave = async () => {
    if (!settings) return;

    setSaving(true);
    setError(null);
    setSuccess(null);

    try {
      const result = await settingsApi.update(settings);
      if (result.success) {
        setSuccess(result.message);
      } else {
        setError(result.message);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save settings');
    } finally {
      setSaving(false);
    }
  };

  const updateAuthorization = (field: keyof AppSettings['authorization'], value: string) => {
    if (!settings) return;
    setSettings({
      ...settings,
      authorization: {
        ...settings.authorization,
        [field]: value,
      },
    });
    setSuccess(null);
  };

  const updateSysmonConfigPusher = (
    field: keyof AppSettings['sysmonConfigPusher'],
    value: string | number
  ) => {
    if (!settings) return;
    setSettings({
      ...settings,
      sysmonConfigPusher: {
        ...settings.sysmonConfigPusher,
        [field]: value,
      },
    });
    setSuccess(null);
  };

  // Filter tabs based on admin status
  const availableTabs = TAB_CONFIG.filter((tab) => !tab.adminOnly || isAdmin);

  const renderTabContent = () => {
    switch (activeTab) {
      case 'display':
        return (
          <div className="space-y-4">
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Configure how information is displayed in the application.
            </p>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                Timestamp Format
              </label>
              <div className="flex items-center gap-4">
                <label className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="radio"
                    name="timestampFormat"
                    value="local"
                    checked={timestampFormat === 'local'}
                    onChange={() => setTimestampFormat('local')}
                    className="w-4 h-4 text-slate-600 focus:ring-slate-500"
                  />
                  <span className="text-sm text-gray-700 dark:text-gray-300">Local Time</span>
                </label>
                <label className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="radio"
                    name="timestampFormat"
                    value="utc"
                    checked={timestampFormat === 'utc'}
                    onChange={() => setTimestampFormat('utc')}
                    className="w-4 h-4 text-slate-600 focus:ring-slate-500"
                  />
                  <span className="text-sm text-gray-700 dark:text-gray-300">UTC</span>
                </label>
              </div>
              <p className="mt-2 text-xs text-gray-500 dark:text-gray-400">
                Preview: {formatTimestamp(new Date(), { includeSeconds: true })}
              </p>
            </div>
          </div>
        );
      case 'authorization':
        return (
          <div className="space-y-4">
            {authMode === 'ApiKey' ? (
              <>
                <p className="text-sm text-gray-500 dark:text-gray-400">
                  Authorization is configured via API keys. Each API key has an assigned role.
                </p>
                <div className="bg-purple-50 dark:bg-purple-900/20 border border-purple-200 dark:border-purple-800 rounded-lg p-4">
                  <h4 className="font-medium text-purple-800 dark:text-purple-300 mb-2">API Key Authentication Mode</h4>
                  <p className="text-sm text-purple-700 dark:text-purple-400 mb-3">
                    In API key mode, user roles are configured per API key via environment variables or docker-compose.yml.
                  </p>
                  <div className="bg-purple-100 dark:bg-purple-900/40 rounded p-3 font-mono text-xs text-purple-800 dark:text-purple-300">
                    <div>Authentication__ApiKeys__0__Key=your-admin-key</div>
                    <div>Authentication__ApiKeys__0__Name=Admin</div>
                    <div>Authentication__ApiKeys__0__Role=Admin</div>
                    <div className="mt-2">Authentication__ApiKeys__1__Key=your-viewer-key</div>
                    <div>Authentication__ApiKeys__1__Name=ReadOnly</div>
                    <div>Authentication__ApiKeys__1__Role=Viewer</div>
                  </div>
                  <p className="text-xs text-purple-600 dark:text-purple-500 mt-3">
                    Available roles: Admin, Operator, Viewer
                  </p>
                </div>
              </>
            ) : (
              <>
                <p className="text-sm text-gray-500 dark:text-gray-400">
                  Configure Active Directory security groups for role-based access control.
                </p>
                {settings && (
                  <div className="grid gap-4">
                    <div>
                      <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                        Admin Group
                      </label>
                      <input
                        type="text"
                        value={settings.authorization.adminGroup}
                        onChange={(e) => updateAuthorization('adminGroup', e.target.value)}
                        className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                        placeholder="SysmonPusher-Admins"
                      />
                      <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                        AD group for full administrative access
                      </p>
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                        Operator Group
                      </label>
                      <input
                        type="text"
                        value={settings.authorization.operatorGroup}
                        onChange={(e) => updateAuthorization('operatorGroup', e.target.value)}
                        className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                        placeholder="SysmonPusher-Operators"
                      />
                      <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                        AD group for deployment and config management
                      </p>
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                        Viewer Group
                      </label>
                      <input
                        type="text"
                        value={settings.authorization.viewerGroup}
                        onChange={(e) => updateAuthorization('viewerGroup', e.target.value)}
                        className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                        placeholder="SysmonPusher-Viewers"
                      />
                      <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                        AD group for read-only access
                      </p>
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                        Default Role
                      </label>
                      <select
                        value={settings.authorization.defaultRole}
                        onChange={(e) => updateAuthorization('defaultRole', e.target.value)}
                        className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                      >
                        <option value="Viewer">Viewer</option>
                        <option value="Operator">Operator</option>
                        <option value="Admin">Admin</option>
                        <option value="None">None (Deny access)</option>
                      </select>
                      <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                        Role assigned when user doesn't match any group
                      </p>
                    </div>
                  </div>
                )}
              </>
            )}
          </div>
        );
      case 'deployment':
        return (
          <div className="space-y-4">
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Configure Sysmon deployment behavior and paths.
            </p>
            {settings && (
              <div className="grid gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                    Sysmon Binary URL
                  </label>
                  <input
                    type="url"
                    value={settings.sysmonConfigPusher.sysmonBinaryUrl}
                    onChange={(e) => updateSysmonConfigPusher('sysmonBinaryUrl', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                    placeholder="https://live.sysinternals.com/Sysmon.exe"
                  />
                  <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                    URL to download Sysmon executable from
                  </p>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                    Default Parallelism
                  </label>
                  <input
                    type="number"
                    min="1"
                    max="500"
                    value={settings.sysmonConfigPusher.defaultParallelism}
                    onChange={(e) =>
                      updateSysmonConfigPusher('defaultParallelism', parseInt(e.target.value) || 50)
                    }
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                  />
                  <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                    Maximum concurrent deployment operations (1-500)
                  </p>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                    Remote Directory
                  </label>
                  <input
                    type="text"
                    value={settings.sysmonConfigPusher.remoteDirectory}
                    onChange={(e) => updateSysmonConfigPusher('remoteDirectory', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                    placeholder="C:\SysmonFiles"
                  />
                  <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                    Target directory on remote hosts for Sysmon files
                  </p>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                    Audit Log Path (JSON)
                  </label>
                  <input
                    type="text"
                    value={settings.sysmonConfigPusher.auditLogPath}
                    onChange={(e) => updateSysmonConfigPusher('auditLogPath', e.target.value)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                    placeholder="C:\Logs\sysmon-pusher-audit.json"
                  />
                  <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                    Path to write JSON audit log file. Leave empty to disable file logging.
                  </p>
                </div>
              </div>
            )}
          </div>
        );
      case 'agent':
        return (
          <div className="space-y-4">
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Configure settings for agent-based deployments to cloud-hosted Windows machines that cannot use WMI/SMB.
            </p>
            {settings && (
              <>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                      Agent Registration Token
                    </label>
                    <input
                      type="text"
                      value={settings.agent?.registrationToken || ''}
                      onChange={(e) =>
                        setSettings({
                          ...settings,
                          agent: { ...settings.agent, registrationToken: e.target.value },
                        })
                      }
                      className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 font-mono"
                      placeholder="Enter a secure token for agent registration"
                    />
                    <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                      Agents must provide this token to register. Leave empty to disable agent registration.
                    </p>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                      Poll Interval (seconds)
                    </label>
                    <input
                      type="number"
                      min={10}
                      max={300}
                      value={settings.agent?.pollIntervalSeconds || 30}
                      onChange={(e) =>
                        setSettings({
                          ...settings,
                          agent: { ...settings.agent, pollIntervalSeconds: parseInt(e.target.value) || 30 },
                        })
                      }
                      className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                    />
                    <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                      How often agents poll for commands (10-300 seconds).
                    </p>
                  </div>
                </div>

                <div className="p-4 bg-purple-50 dark:bg-purple-900/20 rounded-lg">
                  <h4 className="font-medium text-purple-800 dark:text-purple-300 mb-2">Agent Setup Instructions</h4>
                  <ol className="list-decimal list-inside text-sm text-purple-700 dark:text-purple-400 space-y-1">
                    <li>Download the agent installer from the Releases page</li>
                    <li>Configure agent.json with this server's URL and the registration token above</li>
                    <li>Install the agent as a Windows service on target machines</li>
                    <li>The agent will register and appear in the Computers list as "Agent" source</li>
                  </ol>
                </div>
              </>
            )}
          </div>
        );
      case 'binary-cache':
        return (
          <div className="space-y-4">
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Download and cache multiple Sysmon versions for deployments.
            </p>

            {binaryCacheMessage && (
              <div className={`p-4 rounded-lg ${
                binaryCacheMessage.type === 'success'
                  ? 'bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-400'
                  : 'bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400'
              }`}>
                {binaryCacheMessage.text}
              </div>
            )}

            {/* Action buttons */}
            <div className="flex gap-2">
              <button
                onClick={downloadBinary}
                disabled={downloadingBinary || uploadingBinary}
                className="px-4 py-2 bg-slate-600 text-white rounded-lg hover:bg-slate-700 disabled:opacity-50 flex items-center gap-2"
              >
                {downloadingBinary ? (
                  <>
                    <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                    </svg>
                    Downloading...
                  </>
                ) : (
                  <>
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                    </svg>
                    Download from URL
                  </>
                )}
              </button>
              <label className={`px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 flex items-center gap-2 cursor-pointer ${(uploadingBinary || downloadingBinary) ? 'opacity-50 pointer-events-none' : ''}`}>
                {uploadingBinary ? (
                  <>
                    <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                    </svg>
                    Uploading...
                  </>
                ) : (
                  <>
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
                    </svg>
                    Upload from File
                  </>
                )}
                <input
                  type="file"
                  accept=".exe"
                  className="hidden"
                  onChange={(e) => {
                    const file = e.target.files?.[0];
                    if (file) {
                      uploadBinary(file);
                      e.target.value = '';
                    }
                  }}
                  disabled={uploadingBinary || downloadingBinary}
                />
              </label>
            </div>

            {/* Cached versions list */}
            <div className="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4">
              {allCachedVersions.length === 0 ? (
                <div className="text-center py-6">
                  <svg className="w-12 h-12 mx-auto text-gray-300 dark:text-gray-600 mb-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                  </svg>
                  <p className="text-sm text-yellow-600 dark:text-yellow-500 font-medium">
                    No Sysmon binaries cached
                  </p>
                  <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                    Download from the configured URL or upload a Sysmon executable file.
                  </p>
                </div>
              ) : (
                <div className="space-y-3">
                  <div className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                    Cached Versions ({allCachedVersions.length})
                  </div>
                  {allCachedVersions.map((v, index) => (
                    <div
                      key={v.version}
                      className={`flex items-center justify-between p-3 rounded-lg border ${
                        index === 0
                          ? 'border-green-200 dark:border-green-800 bg-green-50 dark:bg-green-900/20'
                          : 'border-gray-200 dark:border-gray-600 bg-white dark:bg-gray-600'
                      }`}
                    >
                      <div className="flex items-center gap-3">
                        <div className="flex-shrink-0 w-8 h-8 bg-slate-600 text-white rounded flex items-center justify-center text-xs font-bold">
                          {v.version?.split('.')[0] || '?'}
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="font-medium text-gray-900 dark:text-gray-100">
                              v{v.version || 'Unknown'}
                            </span>
                            {index === 0 && (
                              <span className="px-2 py-0.5 bg-green-100 dark:bg-green-800 text-green-700 dark:text-green-300 text-xs rounded">
                                Latest
                              </span>
                            )}
                          </div>
                          <div className="text-xs text-gray-500 dark:text-gray-400">
                            {v.fileSizeBytes && `${(v.fileSizeBytes / 1024 / 1024).toFixed(2)} MB`}
                            {v.cachedAt && ` • Cached ${new Date(v.cachedAt).toLocaleDateString()}`}
                          </div>
                        </div>
                      </div>
                      <button
                        onClick={() => v.version && deleteVersion(v.version)}
                        disabled={deletingVersion === v.version}
                        className="p-2 text-gray-400 hover:text-red-500 dark:text-gray-500 dark:hover:text-red-400 disabled:opacity-50"
                        title="Delete this version"
                      >
                        {deletingVersion === v.version ? (
                          <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                          </svg>
                        ) : (
                          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                          </svg>
                        )}
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        );
      case 'tls':
        return (
          <div className="space-y-4">
            <p className="text-sm text-gray-500 dark:text-gray-400">
              HTTPS certificate status. {isAgentOnlyMode
                ? 'For Docker deployments, mount a certificate volume or use a reverse proxy.'
                : 'Configure certificates in appsettings.json.'}
            </p>

            <div className="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4">
              {tlsStatus ? (
                <div className="space-y-3">
                  <div className="flex items-center gap-2">
                    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                      tlsStatus.isDevelopmentCertificate
                        ? 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-400'
                        : tlsStatus.isValid
                          ? 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400'
                          : 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400'
                    }`}>
                      {tlsStatus.isDevelopmentCertificate ? 'Development' : tlsStatus.isValid ? 'Valid' : 'Invalid'}
                    </span>
                    <span className="text-sm text-gray-600 dark:text-gray-400">
                      {tlsStatus.configurationType}
                    </span>
                  </div>

                  {tlsStatus.errorMessage ? (
                    <p className="text-sm text-red-600 dark:text-red-400">{tlsStatus.errorMessage}</p>
                  ) : (
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
                      {tlsStatus.subject && (
                        <div>
                          <span className="text-gray-500 dark:text-gray-400">Subject:</span>
                          <span className="ml-2 text-gray-700 dark:text-gray-300 font-mono text-xs">
                            {tlsStatus.subject}
                          </span>
                        </div>
                      )}
                      {tlsStatus.issuer && (
                        <div>
                          <span className="text-gray-500 dark:text-gray-400">Issuer:</span>
                          <span className="ml-2 text-gray-700 dark:text-gray-300 font-mono text-xs">
                            {tlsStatus.issuer}
                          </span>
                        </div>
                      )}
                      {tlsStatus.notAfter && (
                        <div>
                          <span className="text-gray-500 dark:text-gray-400">Expires:</span>
                          <span className={`ml-2 font-medium ${
                            tlsStatus.daysUntilExpiry !== null && tlsStatus.daysUntilExpiry < 30
                              ? 'text-amber-600 dark:text-amber-400'
                              : 'text-gray-700 dark:text-gray-300'
                          }`}>
                            {new Date(tlsStatus.notAfter).toLocaleDateString()}
                            {tlsStatus.daysUntilExpiry !== null && (
                              <span className="text-xs ml-1">
                                ({tlsStatus.daysUntilExpiry} days)
                              </span>
                            )}
                          </span>
                        </div>
                      )}
                      {tlsStatus.thumbprint && (
                        <div>
                          <span className="text-gray-500 dark:text-gray-400">Thumbprint:</span>
                          <span className="ml-2 text-gray-700 dark:text-gray-300 font-mono text-xs">
                            {tlsStatus.thumbprint.substring(0, 20)}...
                          </span>
                        </div>
                      )}
                    </div>
                  )}

                  {tlsStatus.isDevelopmentCertificate && !isAgentOnlyMode && (
                    <p className="text-xs text-amber-600 dark:text-amber-500 mt-2">
                      Using ASP.NET Core development certificate. Configure a production certificate in appsettings.json for deployment.
                    </p>
                  )}
                </div>
              ) : (
                <p className="text-sm text-gray-500 dark:text-gray-400">Loading TLS status...</p>
              )}
            </div>

            {isAgentOnlyMode && (
              <div className="bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-4">
                <h4 className="font-medium text-blue-800 dark:text-blue-300 mb-2">Docker TLS Configuration</h4>
                <p className="text-sm text-blue-700 dark:text-blue-400 mb-3">
                  To use a production certificate with Docker:
                </p>
                <ol className="list-decimal list-inside text-sm text-blue-700 dark:text-blue-400 space-y-1">
                  <li>Mount your certificate as a volume to <code className="bg-blue-100 dark:bg-blue-800 px-1 rounded">/app/certs/</code></li>
                  <li>Set environment variables for the certificate path and password</li>
                  <li>Or use a reverse proxy (nginx, traefik) to handle TLS termination</li>
                </ol>
              </div>
            )}
          </div>
        );
      default:
        return null;
    }
  };

  if (loading && isAdmin) {
    return (
      <div className="glass-card rounded-lg p-6">
        <div className="text-center py-8 text-gray-500 dark:text-gray-400">Loading settings...</div>
      </div>
    );
  }

  return (
    <div className="flex gap-6">
      {/* Tab navigation on the left */}
      <div className="w-64 flex-shrink-0">
        <div className="glass-card rounded-lg p-4 sticky top-4">
          <h3 className="text-sm font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-3">
            Settings
          </h3>
          <nav className="space-y-1">
            {availableTabs.map((tab) => (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id)}
                className={`w-full flex items-center gap-3 px-3 py-2 text-sm rounded-lg transition-colors ${
                  activeTab === tab.id
                    ? 'bg-slate-100 dark:bg-slate-700 text-slate-900 dark:text-slate-100 font-medium'
                    : 'text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700 hover:text-gray-900 dark:hover:text-gray-200'
                }`}
              >
                {tab.icon}
                {tab.label}
              </button>
            ))}
          </nav>

          {/* Show admin-only notice for non-admins */}
          {!isAdmin && (
            <div className="mt-4 pt-4 border-t border-gray-200 dark:border-gray-700">
              <p className="text-xs text-gray-500 dark:text-gray-400">
                Additional settings are available to administrators only.
              </p>
            </div>
          )}
        </div>
      </div>

      {/* Main content area */}
      <div className="flex-1">
        <div className="glass-card rounded-lg p-6">
          {/* Header with action buttons */}
          {isAdmin && (
            <div className="flex justify-between items-center mb-6">
              <h2 className="text-xl font-semibold text-gray-800 dark:text-gray-200">
                {availableTabs.find((t) => t.id === activeTab)?.label}
              </h2>
              <div className="flex gap-2">
                {!isAgentOnlyMode && (
                  <button
                    onClick={() => setShowRestartConfirm(true)}
                    disabled={restarting}
                    className="px-4 py-2 bg-amber-600 text-white rounded-lg hover:bg-amber-700 disabled:opacity-50 flex items-center gap-2"
                  >
                    {restarting ? (
                      <>
                        <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                        </svg>
                        Restarting...
                      </>
                    ) : (
                      <>
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                        </svg>
                        Restart Service
                      </>
                    )}
                  </button>
                )}
                <button
                  onClick={handleSave}
                  disabled={saving}
                  className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 flex items-center gap-2"
                >
                  {saving ? (
                    <>
                      <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                      </svg>
                      Saving...
                    </>
                  ) : (
                    <>
                      <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                      </svg>
                      Save Settings
                    </>
                  )}
                </button>
              </div>
            </div>
          )}

          {!isAdmin && (
            <h2 className="text-xl font-semibold text-gray-800 dark:text-gray-200 mb-6">
              {availableTabs.find((t) => t.id === activeTab)?.label}
            </h2>
          )}

          {/* Restart Confirmation Modal */}
          {showRestartConfirm && (
            <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
              <div className="glass-panel rounded-lg shadow-xl p-6 max-w-md mx-4">
                <h3 className="text-lg font-semibold text-gray-800 dark:text-gray-200 mb-2">
                  Restart Service?
                </h3>
                <p className="text-gray-600 dark:text-gray-400 mb-4">
                  This will restart the SysmonConfigPusher service to apply any pending settings changes.
                  The page will become temporarily unavailable.
                </p>
                <div className="flex justify-end gap-2">
                  <button
                    onClick={() => setShowRestartConfirm(false)}
                    className="px-4 py-2 text-gray-600 dark:text-gray-400 hover:text-gray-800 dark:hover:text-gray-200"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={handleRestart}
                    className="px-4 py-2 bg-amber-600 text-white rounded-lg hover:bg-amber-700"
                  >
                    Restart Now
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* Error/Success messages */}
          {error && (
            <div className="mb-4 p-4 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400 rounded-lg">
              {error}
            </div>
          )}

          {success && (
            <div className="mb-4 p-4 bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-400 rounded-lg flex items-start gap-3">
              <svg className="w-5 h-5 flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
              </svg>
              <div>
                <p className="font-medium">{success}</p>
                <p className="text-sm mt-1 text-green-600 dark:text-green-500">
                  You may need to restart the service for changes to take effect.
                </p>
              </div>
            </div>
          )}

          {/* Tab content */}
          {renderTabContent()}

          {/* Info card - only show for admin users */}
          {isAdmin && (
            <div className="mt-6 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-4">
              <div className="flex items-start gap-3">
                <svg
                  className="w-5 h-5 text-blue-500 flex-shrink-0 mt-0.5"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                  />
                </svg>
                <div className="text-sm text-blue-700 dark:text-blue-300">
                  {isAgentOnlyMode ? (
                    <>
                      <p className="font-medium">Settings are configured via environment variables</p>
                      <p className="mt-1 text-blue-600 dark:text-blue-400">
                        Most settings in Docker deployments are configured via environment variables in
                        docker-compose.yml. Restart the container to apply changes:{' '}
                        <code className="bg-blue-100 dark:bg-blue-800 px-1 rounded">
                          docker-compose restart
                        </code>
                      </p>
                    </>
                  ) : (
                    <>
                      <p className="font-medium">Settings are saved to appsettings.json</p>
                      <p className="mt-1 text-blue-600 dark:text-blue-400">
                        Most changes require a service restart to take effect. The service can be restarted
                        from the Windows Services console or by running{' '}
                        <code className="bg-blue-100 dark:bg-blue-800 px-1 rounded">
                          Restart-Service SysmonConfigPusher
                        </code>
                      </p>
                    </>
                  )}
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
