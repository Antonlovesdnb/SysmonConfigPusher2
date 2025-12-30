import { useState, useEffect } from 'react';
import { settingsApi, type BinaryCacheStatus } from '../api';
import type { AppSettings } from '../types';
import { useAuth } from '../context/AuthContext';

export function SettingsPage() {
  const { isAdmin } = useAuth();
  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // Binary cache state
  const [binaryCache, setBinaryCache] = useState<BinaryCacheStatus | null>(null);
  const [downloadingBinary, setDownloadingBinary] = useState(false);
  const [binaryCacheMessage, setBinaryCacheMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  useEffect(() => {
    loadSettings();
    loadBinaryCacheStatus();
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
      const status = await settingsApi.getBinaryCacheStatus();
      setBinaryCache(status);
    } catch (err) {
      console.error('Failed to load binary cache status:', err);
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

  if (!isAdmin) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div className="text-center py-8">
          <svg
            className="w-16 h-16 mx-auto text-gray-400 dark:text-gray-500 mb-4"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
            />
          </svg>
          <h2 className="text-xl font-semibold text-gray-700 dark:text-gray-300 mb-2">
            Access Denied
          </h2>
          <p className="text-gray-500 dark:text-gray-400 mb-4">
            You don't have permission to view application settings.
          </p>
          <p className="text-sm text-gray-400 dark:text-gray-500">
            Only administrators can access this page.
          </p>
        </div>
      </div>
    );
  }

  if (loading) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div className="text-center py-8 text-gray-500 dark:text-gray-400">Loading settings...</div>
      </div>
    );
  }

  if (!settings) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div className="text-center py-8 text-red-500">Failed to load settings</div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-xl font-semibold text-gray-800 dark:text-gray-200">
            Application Settings
          </h2>
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 flex items-center gap-2"
          >
            {saving ? (
              <>
                <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                  <circle
                    className="opacity-25"
                    cx="12"
                    cy="12"
                    r="10"
                    stroke="currentColor"
                    strokeWidth="4"
                  />
                  <path
                    className="opacity-75"
                    fill="currentColor"
                    d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
                  />
                </svg>
                Saving...
              </>
            ) : (
              <>
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M5 13l4 4L19 7"
                  />
                </svg>
                Save Settings
              </>
            )}
          </button>
        </div>

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

        {/* Authorization Settings */}
        <div className="mb-8">
          <h3 className="text-lg font-medium text-gray-700 dark:text-gray-300 mb-4 flex items-center gap-2">
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z"
              />
            </svg>
            Authorization Settings
          </h3>
          <p className="text-sm text-gray-500 dark:text-gray-400 mb-4">
            Configure Active Directory security groups for role-based access control.
          </p>

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
        </div>

        {/* SysmonConfigPusher Settings */}
        <div>
          <h3 className="text-lg font-medium text-gray-700 dark:text-gray-300 mb-4 flex items-center gap-2">
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z"
              />
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
              />
            </svg>
            Deployment Settings
          </h3>
          <p className="text-sm text-gray-500 dark:text-gray-400 mb-4">
            Configure Sysmon deployment behavior and paths.
          </p>

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
        </div>

        {/* Binary Cache Section */}
        <div className="mt-8 pt-8 border-t border-gray-200 dark:border-gray-700">
          <h3 className="text-lg font-medium text-gray-700 dark:text-gray-300 mb-4 flex items-center gap-2">
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"
              />
            </svg>
            Sysmon Binary Cache
          </h3>
          <p className="text-sm text-gray-500 dark:text-gray-400 mb-4">
            Download and cache the Sysmon executable for deployments.
          </p>

          {binaryCacheMessage && (
            <div className={`mb-4 p-4 rounded-lg ${
              binaryCacheMessage.type === 'success'
                ? 'bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-400'
                : 'bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400'
            }`}>
              {binaryCacheMessage.text}
            </div>
          )}

          <div className="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4">
            <div className="flex items-center justify-between">
              <div>
                <div className="flex items-center gap-2 mb-2">
                  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                    binaryCache?.isCached
                      ? 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400'
                      : 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-400'
                  }`}>
                    {binaryCache?.isCached ? 'Cached' : 'Not Cached'}
                  </span>
                  {binaryCache?.version && (
                    <span className="text-sm text-gray-600 dark:text-gray-400">
                      v{binaryCache.version}
                    </span>
                  )}
                </div>
                {binaryCache?.isCached && binaryCache.cachedAt && (
                  <p className="text-xs text-gray-500 dark:text-gray-400">
                    Downloaded: {new Date(binaryCache.cachedAt).toLocaleString()}
                    {binaryCache.fileSizeBytes && (
                      <> ({(binaryCache.fileSizeBytes / 1024 / 1024).toFixed(2)} MB)</>
                    )}
                  </p>
                )}
                {!binaryCache?.isCached && (
                  <p className="text-xs text-yellow-600 dark:text-yellow-500">
                    Sysmon binary must be downloaded before deployments can run.
                  </p>
                )}
              </div>
              <button
                onClick={downloadBinary}
                disabled={downloadingBinary}
                className="px-4 py-2 bg-slate-600 text-white rounded-lg hover:bg-slate-700 disabled:opacity-50 flex items-center gap-2"
              >
                {downloadingBinary ? (
                  <>
                    <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                      <circle
                        className="opacity-25"
                        cx="12"
                        cy="12"
                        r="10"
                        stroke="currentColor"
                        strokeWidth="4"
                      />
                      <path
                        className="opacity-75"
                        fill="currentColor"
                        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
                      />
                    </svg>
                    Downloading...
                  </>
                ) : (
                  <>
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"
                      />
                    </svg>
                    {binaryCache?.isCached ? 'Update Binary' : 'Download Binary'}
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Info card */}
      <div className="bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-4">
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
            <p className="font-medium">Settings are saved to appsettings.json</p>
            <p className="mt-1 text-blue-600 dark:text-blue-400">
              Most changes require a service restart to take effect. The service can be restarted
              from the Windows Services console or by running{' '}
              <code className="bg-blue-100 dark:bg-blue-800 px-1 rounded">
                Restart-Service SysmonConfigPusher
              </code>
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
