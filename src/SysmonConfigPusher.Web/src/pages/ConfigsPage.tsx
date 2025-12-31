import { useState, useEffect, useRef, useMemo, useCallback } from 'react';
import { configsApi } from '../api';
import type { Config, ConfigDetail, ConfigDiff } from '../types';
import { XmlEditor } from '../components/XmlEditor';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import { useToast } from '../context/ToastContext';

interface UploadResult {
  filename: string;
  success: boolean;
  error?: string;
}

interface UploadProgress {
  total: number;
  completed: number;
  current: string;
  results: UploadResult[];
}

export function ConfigsPage() {
  const [configs, setConfigs] = useState<Config[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState<UploadProgress | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedConfig, setSelectedConfig] = useState<ConfigDetail | null>(null);
  const [viewerOpen, setViewerOpen] = useState(false);
  const [editorOpen, setEditorOpen] = useState(false);
  const [editedContent, setEditedContent] = useState<string>('');
  const [saving, setSaving] = useState(false);
  const [hasChanges, setHasChanges] = useState(false);
  const [fontSize, setFontSize] = useState(14);
  const [uploadResultsOpen, setUploadResultsOpen] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const { canManageConfigs } = useAuth();
  const { darkMode } = useTheme();
  const { showToast } = useToast();

  // URL import state
  const [urlImportOpen, setUrlImportOpen] = useState(false);
  const [importUrl, setImportUrl] = useState('');
  const [importing, setImporting] = useState(false);

  // Diff state
  const [diffOpen, setDiffOpen] = useState(false);
  const [diffConfig1Id, setDiffConfig1Id] = useState<number | null>(null);
  const [diffConfig2Id, setDiffConfig2Id] = useState<number | null>(null);
  const [diffData, setDiffData] = useState<ConfigDiff | null>(null);
  const [loadingDiff, setLoadingDiff] = useState(false);

  // Confirmation modal state - simplified to avoid function in state
  const [deleteTarget, setDeleteTarget] = useState<Config | null>(null);
  const [discardChangesTarget, setDiscardChangesTarget] = useState(false);

  const fetchConfigs = useCallback(async () => {
    setLoading(true);
    try {
      const data = await configsApi.list();
      setConfigs(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load configs');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchConfigs();
  }, [fetchConfigs]);

  const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0) return;

    const fileList = Array.from(files);
    setUploading(true);
    setUploadProgress({
      total: fileList.length,
      completed: 0,
      current: fileList[0].name,
      results: [],
    });

    const results: UploadResult[] = [];

    for (let i = 0; i < fileList.length; i++) {
      const file = fileList[i];
      setUploadProgress((prev) => prev ? {
        ...prev,
        current: file.name,
        completed: i,
      } : null);

      try {
        await configsApi.upload(file);
        results.push({ filename: file.name, success: true });
      } catch (err) {
        results.push({
          filename: file.name,
          success: false,
          error: err instanceof Error ? err.message : 'Upload failed',
        });
      }

      setUploadProgress((prev) => prev ? {
        ...prev,
        completed: i + 1,
        results: [...results],
      } : null);
    }

    // Reset file input
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }

    setUploading(false);

    // Show results modal if there were multiple files or any failures
    const hasFailures = results.some((r) => !r.success);
    if (fileList.length > 1 || hasFailures) {
      setUploadResultsOpen(true);
    } else {
      setUploadProgress(null);
    }

    // Refresh the list
    await fetchConfigs();
  };

  const viewConfig = async (id: number) => {
    try {
      const detail = await configsApi.get(id);
      setSelectedConfig(detail);
      setViewerOpen(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load config');
    }
  };

  const deleteConfig = useCallback((config: Config) => {
    setDeleteTarget(config);
  }, []);

  const confirmDelete = useCallback(async () => {
    if (!deleteTarget) return;
    const configId = deleteTarget.id;
    setDeleteTarget(null);
    try {
      await configsApi.delete(configId);
      await fetchConfigs();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete config');
    }
  }, [deleteTarget, fetchConfigs]);

  const closeUploadResults = () => {
    setUploadResultsOpen(false);
    setUploadProgress(null);
  };

  const handleImportFromUrl = async () => {
    if (!importUrl.trim()) {
      showToast('Please enter a URL', 'error');
      return;
    }

    setImporting(true);
    try {
      await configsApi.importFromUrl(importUrl.trim());
      showToast('Configuration imported successfully', 'success');
      setUrlImportOpen(false);
      setImportUrl('');
      await fetchConfigs();
    } catch (err) {
      showToast(err instanceof Error ? err.message : 'Failed to import config', 'error');
    } finally {
      setImporting(false);
    }
  };

  const exportConfig = async (config: Config) => {
    try {
      const detail = await configsApi.get(config.id);
      const blob = new Blob([detail.content], { type: 'application/xml' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = config.filename;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to export config');
    }
  };

  const editConfig = async (id: number) => {
    try {
      const detail = await configsApi.get(id);
      setSelectedConfig(detail);
      setEditedContent(detail.content);
      setHasChanges(false);
      setEditorOpen(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load config');
    }
  };

  const saveConfig = async () => {
    if (!selectedConfig) return;

    setSaving(true);
    try {
      const updated = await configsApi.update(selectedConfig.id, editedContent);
      setSelectedConfig(updated);
      setHasChanges(false);
      setError(null);
      // Refresh the list to show updated hash
      await fetchConfigs();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save config');
    } finally {
      setSaving(false);
    }
  };

  const handleEditorChange = (value: string) => {
    setEditedContent(value);
    setHasChanges(value !== selectedConfig?.content);
  };

  const closeEditor = () => {
    if (hasChanges) {
      setDiscardChangesTarget(true);
      return;
    }
    setEditorOpen(false);
    setSelectedConfig(null);
    setEditedContent('');
    setHasChanges(false);
  };

  const confirmDiscardChanges = useCallback(() => {
    setDiscardChangesTarget(false);
    setEditorOpen(false);
    setSelectedConfig(null);
    setEditedContent('');
    setHasChanges(false);
  }, []);

  const openDiffModal = () => {
    if (configs.length >= 2) {
      setDiffConfig1Id(configs[0].id);
      setDiffConfig2Id(configs[1].id);
    }
    setDiffData(null);
    setDiffOpen(true);
  };

  const loadDiff = async () => {
    if (!diffConfig1Id || !diffConfig2Id || diffConfig1Id === diffConfig2Id) return;

    setLoadingDiff(true);
    try {
      const data = await configsApi.diff(diffConfig1Id, diffConfig2Id);
      setDiffData(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load diff');
    } finally {
      setLoadingDiff(false);
    }
  };

  const closeDiff = () => {
    setDiffOpen(false);
    setDiffData(null);
    setDiffConfig1Id(null);
    setDiffConfig2Id(null);
  };

  // Compute line-by-line diff
  const diffLines = useMemo(() => {
    if (!diffData) return [];

    const lines1 = diffData.lines1;
    const lines2 = diffData.lines2;
    const maxLines = Math.max(lines1.length, lines2.length);
    const result: { lineNum: number; left: string; right: string; status: 'same' | 'changed' | 'added' | 'removed' }[] = [];

    for (let i = 0; i < maxLines; i++) {
      const left = lines1[i] ?? '';
      const right = lines2[i] ?? '';
      let status: 'same' | 'changed' | 'added' | 'removed' = 'same';

      if (left === right) {
        status = 'same';
      } else if (!left && right) {
        status = 'added';
      } else if (left && !right) {
        status = 'removed';
      } else {
        status = 'changed';
      }

      result.push({ lineNum: i + 1, left, right, status });
    }

    return result;
  }, [diffData]);

  return (
    <div className="space-y-4">
      <div className="glass-card rounded-lg p-6 transition-all">
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-xl font-semibold text-gray-800 dark:text-gray-100">Sysmon Configurations</h2>
          <div className="flex gap-2">
            {canManageConfigs && (
              <>
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".xml"
                  multiple
                  onChange={handleUpload}
                  className="hidden"
                  id="config-upload"
                />
                <label
                  htmlFor="config-upload"
                  className={`px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 cursor-pointer flex items-center gap-2 ${
                    uploading ? 'opacity-50 pointer-events-none' : ''
                  }`}
                >
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
                  </svg>
                  {uploading ? 'Uploading...' : 'Upload Configs'}
                </label>
                <button
                  onClick={() => setUrlImportOpen(true)}
                  className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 flex items-center gap-2"
                >
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
                  </svg>
                  From URL
                </button>
              </>
            )}
            <button
              onClick={openDiffModal}
              disabled={configs.length < 2}
              className="px-4 py-2 bg-purple-600 text-white rounded-lg hover:bg-purple-700 disabled:opacity-50 flex items-center gap-2"
              title={configs.length < 2 ? 'Need at least 2 configs to compare' : 'Compare two configs'}
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
              </svg>
              Compare
            </button>
            <button
              onClick={fetchConfigs}
              disabled={loading}
              className="px-4 py-2 bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-300 dark:hover:bg-gray-600 disabled:opacity-50"
            >
              Refresh
            </button>
          </div>
        </div>

        {/* Upload Progress */}
        {uploading && uploadProgress && (
          <div className="mb-4 p-4 bg-blue-50 border border-blue-200 rounded-lg">
            <div className="flex items-center gap-3 mb-2">
              <svg className="w-5 h-5 text-blue-600 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
              <span className="text-blue-800 font-medium">
                Uploading: {uploadProgress.current}
              </span>
            </div>
            <div className="text-sm text-blue-600 mb-2">
              {uploadProgress.completed} of {uploadProgress.total} files completed
            </div>
            <div className="bg-blue-200 rounded-full h-2">
              <div
                className="bg-blue-600 h-2 rounded-full transition-all"
                style={{ width: `${(uploadProgress.completed / uploadProgress.total) * 100}%` }}
              />
            </div>
          </div>
        )}

        {error && (
          <div className="mb-4 p-4 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400 rounded-lg">{error}</div>
        )}

        {loading ? (
          <div className="text-center py-8 text-gray-500 dark:text-gray-400">Loading...</div>
        ) : configs.length === 0 ? (
          <div className="text-center py-8 text-gray-500 dark:text-gray-400">
            <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-gray-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
            <p>No configs uploaded yet.</p>
            <p className="text-sm mt-1">Click "Upload Configs" to add Sysmon configuration files.</p>
            <p className="text-sm text-gray-400 dark:text-gray-500 mt-2">You can select multiple .xml files at once.</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
              <thead className="bg-gray-50 dark:bg-gray-700/50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Filename
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Tag
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Hash (SHA256)
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Uploaded By
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Uploaded At
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200/50 dark:divide-gray-700/50">
                {configs.map((config) => (
                  <tr key={config.id} className="hover:bg-gray-50 dark:hover:bg-gray-700/50">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-gray-100">
                      <span className="flex items-center gap-2">
                        {config.filename}
                        {config.isValid ? (
                          <span
                            title={config.validationMessage || 'Valid Sysmon configuration'}
                            className="text-green-500 cursor-help"
                          >
                            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                              <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                            </svg>
                          </span>
                        ) : (
                          <span
                            title={config.validationMessage || 'Invalid configuration'}
                            className="text-red-500 cursor-help"
                          >
                            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                              <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
                            </svg>
                          </span>
                        )}
                        {config.sourceUrl && (
                          <span
                            title={`Imported from: ${config.sourceUrl}`}
                            className="text-blue-500 cursor-help"
                          >
                            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                              <path d="M5.5 16a3.5 3.5 0 01-.369-6.98 4 4 0 117.753-1.977A4.5 4.5 0 1113.5 16h-8z" />
                            </svg>
                          </span>
                        )}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                      {config.tag ? (
                        <span className="px-2 py-1 bg-slate-100 dark:bg-slate-700 text-slate-700 dark:text-slate-300 rounded text-xs">
                          {config.tag}
                        </span>
                      ) : (
                        '-'
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400 font-mono">
                      {config.hash.substring(0, 16)}...
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                      {config.uploadedBy || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                      {new Date(config.uploadedAt).toLocaleString()}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm">
                      <button
                        onClick={() => viewConfig(config.id)}
                        className="text-slate-600 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200 mr-4"
                      >
                        View
                      </button>
                      {canManageConfigs && (
                        <button
                          onClick={() => editConfig(config.id)}
                          className="text-blue-600 hover:text-blue-800 mr-4"
                        >
                          Edit
                        </button>
                      )}
                      <button
                        onClick={() => exportConfig(config)}
                        className="text-green-600 hover:text-green-800 mr-4"
                      >
                        Export
                      </button>
                      {canManageConfigs && (
                        <button
                          onClick={() => deleteConfig(config)}
                          className="text-red-600 hover:text-red-800"
                        >
                          Delete
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="mt-4 text-sm text-gray-500 dark:text-gray-400">Total: {configs.length} configs</div>
      </div>

      {/* Config Viewer Modal */}
      {viewerOpen && selectedConfig && (
        <div className="fixed inset-0 bg-black/50 dark:bg-black/70 flex items-center justify-center z-50">
          <div className="glass-panel rounded-lg shadow-xl flex flex-col m-4" style={{ width: 'calc(100vw - 64px)', height: 'calc(100vh - 64px)' }}>
            <div className="flex justify-between items-center p-4 border-b dark:border-gray-700">
              <div>
                <h3 className="text-lg font-semibold dark:text-gray-100">{selectedConfig.filename}</h3>
                {selectedConfig.tag && (
                  <span className="text-sm text-gray-500 dark:text-gray-400">Tag: {selectedConfig.tag}</span>
                )}
              </div>
              <div className="flex items-center gap-4">
                {/* Font Size Controls */}
                <div className="flex items-center gap-2 text-sm text-gray-600 dark:text-gray-400">
                  <span>Font:</span>
                  <button
                    onClick={() => setFontSize((s) => Math.max(10, s - 2))}
                    className="w-7 h-7 flex items-center justify-center bg-gray-200 dark:bg-gray-700 hover:bg-gray-300 dark:hover:bg-gray-600 rounded"
                    title="Decrease font size"
                  >
                    −
                  </button>
                  <span className="w-8 text-center">{fontSize}</span>
                  <button
                    onClick={() => setFontSize((s) => Math.min(24, s + 2))}
                    className="w-7 h-7 flex items-center justify-center bg-gray-200 dark:bg-gray-700 hover:bg-gray-300 dark:hover:bg-gray-600 rounded"
                    title="Increase font size"
                  >
                    +
                  </button>
                </div>
                <button
                  onClick={() => setViewerOpen(false)}
                  className="text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 text-2xl leading-none"
                >
                  &times;
                </button>
              </div>
            </div>
            <div className="flex-1 overflow-hidden p-4">
              <XmlEditor value={selectedConfig.content} readOnly height="calc(100vh - 180px)" fontSize={fontSize} darkMode={darkMode} />
            </div>
            <div className="p-4 border-t dark:border-gray-700 flex justify-between items-center bg-gray-50 dark:bg-gray-900">
              <span className="text-sm text-gray-500 dark:text-gray-400 font-mono">
                SHA256: {selectedConfig.hash}
              </span>
              <div className="flex gap-2">
                <button
                  onClick={() => {
                    const blob = new Blob([selectedConfig.content], { type: 'application/xml' });
                    const url = URL.createObjectURL(blob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = selectedConfig.filename;
                    document.body.appendChild(a);
                    a.click();
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);
                  }}
                  className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 flex items-center gap-2"
                >
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                  </svg>
                  Export
                </button>
                <button
                  onClick={() => setViewerOpen(false)}
                  className="px-4 py-2 bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-300 dark:hover:bg-gray-600"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Config Editor Modal */}
      {editorOpen && selectedConfig && (
        <div className="fixed inset-0 bg-black/50 dark:bg-black/70 flex items-center justify-center z-50">
          <div className="glass-panel rounded-lg shadow-xl flex flex-col m-4" style={{ width: 'calc(100vw - 64px)', height: 'calc(100vh - 64px)' }}>
            <div className="flex justify-between items-center p-4 border-b dark:border-gray-700">
              <div>
                <h3 className="text-lg font-semibold dark:text-gray-100">
                  {selectedConfig.filename}
                  {hasChanges && <span className="text-orange-500 ml-2">*</span>}
                </h3>
                <div className="flex items-center gap-4 text-sm text-gray-500 dark:text-gray-400">
                  {selectedConfig.tag && <span>Tag: {selectedConfig.tag}</span>}
                  <span className="text-xs">Use Ctrl+Space for auto-complete</span>
                </div>
              </div>
              <div className="flex items-center gap-4">
                {/* Font Size Controls */}
                <div className="flex items-center gap-2 text-sm text-gray-600 dark:text-gray-400">
                  <span>Font:</span>
                  <button
                    onClick={() => setFontSize((s) => Math.max(10, s - 2))}
                    className="w-7 h-7 flex items-center justify-center bg-gray-200 dark:bg-gray-700 hover:bg-gray-300 dark:hover:bg-gray-600 rounded"
                    title="Decrease font size"
                  >
                    −
                  </button>
                  <span className="w-8 text-center">{fontSize}</span>
                  <button
                    onClick={() => setFontSize((s) => Math.min(24, s + 2))}
                    className="w-7 h-7 flex items-center justify-center bg-gray-200 dark:bg-gray-700 hover:bg-gray-300 dark:hover:bg-gray-600 rounded"
                    title="Increase font size"
                  >
                    +
                  </button>
                </div>
                <button
                  onClick={closeEditor}
                  className="text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 text-2xl leading-none"
                >
                  &times;
                </button>
              </div>
            </div>
            <div className="flex-1 overflow-hidden p-4">
              <XmlEditor
                value={editedContent}
                onChange={handleEditorChange}
                height="calc(100vh - 180px)"
                fontSize={fontSize}
                darkMode={darkMode}
              />
            </div>
            <div className="p-4 border-t dark:border-gray-700 flex justify-between items-center bg-gray-50 dark:bg-gray-900">
              <div className="text-sm text-gray-500 dark:text-gray-400">
                {hasChanges ? (
                  <span className="text-orange-600 dark:text-orange-400 font-medium">Unsaved changes</span>
                ) : (
                  <span className="font-mono">SHA256: {selectedConfig.hash}</span>
                )}
              </div>
              <div className="flex gap-2">
                <button
                  onClick={saveConfig}
                  disabled={!hasChanges || saving}
                  className={`px-4 py-2 rounded-lg flex items-center gap-2 ${
                    hasChanges && !saving
                      ? 'bg-blue-600 text-white hover:bg-blue-700'
                      : 'bg-gray-300 dark:bg-gray-600 text-gray-500 dark:text-gray-400 cursor-not-allowed'
                  }`}
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
                      Save
                    </>
                  )}
                </button>
                <button
                  onClick={() => {
                    const blob = new Blob([editedContent], { type: 'application/xml' });
                    const url = URL.createObjectURL(blob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = selectedConfig.filename;
                    document.body.appendChild(a);
                    a.click();
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);
                  }}
                  className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 flex items-center gap-2"
                >
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                  </svg>
                  Export
                </button>
                <button
                  onClick={closeEditor}
                  className="px-4 py-2 bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-300 dark:hover:bg-gray-600"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Upload Results Modal */}
      {uploadResultsOpen && uploadProgress && (
        <div className="fixed inset-0 bg-black/50 dark:bg-black/70 flex items-center justify-center z-50">
          <div className="glass-panel rounded-lg shadow-xl max-w-lg w-full m-4">
            <div className="flex justify-between items-center p-4 border-b dark:border-gray-700">
              <h3 className="text-lg font-semibold dark:text-gray-100">Upload Results</h3>
              <button
                onClick={closeUploadResults}
                className="text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 text-2xl leading-none"
              >
                &times;
              </button>
            </div>
            <div className="p-4 max-h-96 overflow-y-auto">
              <div className="space-y-2">
                {uploadProgress.results.map((result, index) => (
                  <div
                    key={index}
                    className={`flex items-center gap-3 p-3 rounded-lg ${
                      result.success ? 'bg-green-50' : 'bg-red-50'
                    }`}
                  >
                    {result.success ? (
                      <svg className="w-5 h-5 text-green-600 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                      </svg>
                    ) : (
                      <svg className="w-5 h-5 text-red-600 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    )}
                    <div className="flex-1 min-w-0">
                      <div className={`text-sm font-medium truncate ${result.success ? 'text-green-800' : 'text-red-800'}`}>
                        {result.filename}
                      </div>
                      {result.error && (
                        <div className="text-xs text-red-600 truncate">{result.error}</div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
            <div className="p-4 border-t bg-gray-50 flex justify-between items-center">
              <div className="text-sm text-gray-600">
                <span className="text-green-600 font-medium">
                  {uploadProgress.results.filter((r) => r.success).length} succeeded
                </span>
                {uploadProgress.results.some((r) => !r.success) && (
                  <>
                    {' / '}
                    <span className="text-red-600 font-medium">
                      {uploadProgress.results.filter((r) => !r.success).length} failed
                    </span>
                  </>
                )}
              </div>
              <button
                onClick={closeUploadResults}
                className="px-4 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800"
              >
                Done
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Config Diff Modal */}
      {diffOpen && (
        <div className="fixed inset-0 bg-black/50 dark:bg-black/70 flex items-center justify-center z-50">
          <div className="glass-panel rounded-lg shadow-xl flex flex-col m-4" style={{ width: 'calc(100vw - 64px)', height: 'calc(100vh - 64px)' }}>
            <div className="flex justify-between items-center p-4 border-b dark:border-gray-700">
              <h3 className="text-lg font-semibold dark:text-gray-100">Compare Configurations</h3>
              <button
                onClick={closeDiff}
                className="text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 text-2xl leading-none"
              >
                &times;
              </button>
            </div>

            {/* Config Selectors */}
            <div className="p-4 border-b dark:border-gray-700 bg-gray-50 dark:bg-gray-900">
              <div className="flex items-center gap-4">
                <div className="flex-1">
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Left Config</label>
                  <select
                    value={diffConfig1Id || ''}
                    onChange={(e) => setDiffConfig1Id(e.target.value ? parseInt(e.target.value) : null)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                  >
                    <option value="">Select config...</option>
                    {configs.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.filename} {c.tag && `(${c.tag})`}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="flex-1">
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Right Config</label>
                  <select
                    value={diffConfig2Id || ''}
                    onChange={(e) => setDiffConfig2Id(e.target.value ? parseInt(e.target.value) : null)}
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                  >
                    <option value="">Select config...</option>
                    {configs.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.filename} {c.tag && `(${c.tag})`}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="flex items-end">
                  <button
                    onClick={loadDiff}
                    disabled={!diffConfig1Id || !diffConfig2Id || diffConfig1Id === diffConfig2Id || loadingDiff}
                    className="px-4 py-2 bg-purple-600 text-white rounded-lg hover:bg-purple-700 disabled:opacity-50"
                  >
                    {loadingDiff ? 'Loading...' : 'Compare'}
                  </button>
                </div>
              </div>
              {diffConfig1Id === diffConfig2Id && diffConfig1Id && (
                <p className="text-sm text-orange-600 dark:text-orange-400 mt-2">Please select two different configs to compare.</p>
              )}
            </div>

            {/* Diff Content */}
            <div className="flex-1 overflow-auto p-4">
              {!diffData ? (
                <div className="flex items-center justify-center h-full text-gray-500 dark:text-gray-400">
                  Select two configs and click Compare to see the diff
                </div>
              ) : (
                <div className="flex gap-4 h-full">
                  {/* Left side */}
                  <div className="flex-1 flex flex-col min-w-0">
                    <div className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2 truncate">
                      {diffData.config1.filename} {diffData.config1.tag && <span className="text-gray-500">({diffData.config1.tag})</span>}
                    </div>
                    <div className="flex-1 overflow-auto bg-gray-50 dark:bg-gray-900 rounded border dark:border-gray-700 font-mono text-sm">
                      {diffLines.map((line) => (
                        <div
                          key={`left-${line.lineNum}`}
                          className={`flex ${
                            line.status === 'removed' ? 'bg-red-100 dark:bg-red-900/30' :
                            line.status === 'changed' ? 'bg-yellow-100 dark:bg-yellow-900/30' :
                            line.status === 'added' ? 'bg-gray-200 dark:bg-gray-700' :
                            ''
                          }`}
                        >
                          <span className="w-12 flex-shrink-0 text-right pr-2 text-gray-400 dark:text-gray-500 select-none border-r dark:border-gray-700">
                            {line.lineNum}
                          </span>
                          <pre className="pl-2 overflow-hidden whitespace-pre text-gray-800 dark:text-gray-200">{line.left || ' '}</pre>
                        </div>
                      ))}
                    </div>
                  </div>

                  {/* Right side */}
                  <div className="flex-1 flex flex-col min-w-0">
                    <div className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2 truncate">
                      {diffData.config2.filename} {diffData.config2.tag && <span className="text-gray-500">({diffData.config2.tag})</span>}
                    </div>
                    <div className="flex-1 overflow-auto bg-gray-50 dark:bg-gray-900 rounded border dark:border-gray-700 font-mono text-sm">
                      {diffLines.map((line) => (
                        <div
                          key={`right-${line.lineNum}`}
                          className={`flex ${
                            line.status === 'added' ? 'bg-green-100 dark:bg-green-900/30' :
                            line.status === 'changed' ? 'bg-yellow-100 dark:bg-yellow-900/30' :
                            line.status === 'removed' ? 'bg-gray-200 dark:bg-gray-700' :
                            ''
                          }`}
                        >
                          <span className="w-12 flex-shrink-0 text-right pr-2 text-gray-400 dark:text-gray-500 select-none border-r dark:border-gray-700">
                            {line.lineNum}
                          </span>
                          <pre className="pl-2 overflow-hidden whitespace-pre text-gray-800 dark:text-gray-200">{line.right || ' '}</pre>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              )}
            </div>

            {/* Footer with legend */}
            {diffData && (
              <div className="p-4 border-t dark:border-gray-700 bg-gray-50 dark:bg-gray-900 flex justify-between items-center">
                <div className="flex items-center gap-4 text-sm">
                  <span className="flex items-center gap-1">
                    <span className="w-4 h-4 bg-red-100 dark:bg-red-900/30 border rounded"></span>
                    <span className="text-gray-600 dark:text-gray-400">Removed</span>
                  </span>
                  <span className="flex items-center gap-1">
                    <span className="w-4 h-4 bg-green-100 dark:bg-green-900/30 border rounded"></span>
                    <span className="text-gray-600 dark:text-gray-400">Added</span>
                  </span>
                  <span className="flex items-center gap-1">
                    <span className="w-4 h-4 bg-yellow-100 dark:bg-yellow-900/30 border rounded"></span>
                    <span className="text-gray-600 dark:text-gray-400">Changed</span>
                  </span>
                </div>
                <div className="text-sm text-gray-500 dark:text-gray-400">
                  {diffLines.filter(l => l.status !== 'same').length} differences found
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* URL Import Modal */}
      {urlImportOpen && (
        <div className="fixed inset-0 bg-black/50 dark:bg-black/70 flex items-center justify-center z-50">
          <div className="glass-panel rounded-lg shadow-xl max-w-lg w-full m-4">
            <div className="flex justify-between items-center p-4 border-b dark:border-gray-700">
              <h3 className="text-lg font-semibold dark:text-gray-100">Import Config from URL</h3>
              <button
                onClick={() => {
                  setUrlImportOpen(false);
                  setImportUrl('');
                }}
                className="text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 text-2xl leading-none"
              >
                &times;
              </button>
            </div>
            <div className="p-4">
              <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
                Enter the URL of a Sysmon configuration file. The file must be a valid Sysmon XML configuration.
              </p>
              <input
                type="url"
                value={importUrl}
                onChange={(e) => setImportUrl(e.target.value)}
                placeholder="https://example.com/sysmon-config.xml"
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && !importing) {
                    handleImportFromUrl();
                  }
                }}
                disabled={importing}
                autoFocus
              />
              <p className="text-xs text-gray-500 dark:text-gray-500 mt-2">
                Supported: HTTP/HTTPS URLs. Private/local addresses are blocked for security.
              </p>
            </div>
            <div className="px-4 py-3 bg-gray-50 dark:bg-gray-900 rounded-b-lg flex justify-end gap-3">
              <button
                onClick={() => {
                  setUrlImportOpen(false);
                  setImportUrl('');
                }}
                disabled={importing}
                className="px-4 py-2 text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-600 disabled:opacity-50"
              >
                Cancel
              </button>
              <button
                onClick={handleImportFromUrl}
                disabled={importing || !importUrl.trim()}
                className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2"
              >
                {importing ? (
                  <>
                    <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                    </svg>
                    Importing...
                  </>
                ) : (
                  <>
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                    </svg>
                    Import
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {deleteTarget && (
        <div className="fixed inset-0 bg-black/50 dark:bg-black/70 flex items-center justify-center z-[60]">
          <div className="glass-panel rounded-lg shadow-xl max-w-md w-full m-4">
            <div className="p-6">
              <div className="flex items-center gap-4">
                <div className="flex-shrink-0 w-12 h-12 rounded-full bg-red-100 dark:bg-red-900/30 flex items-center justify-center">
                  <svg className="w-6 h-6 text-red-600 dark:text-red-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                  </svg>
                </div>
                <div>
                  <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">Delete Configuration</h3>
                  <p className="mt-1 text-sm text-gray-600 dark:text-gray-400">
                    Are you sure you want to delete "{deleteTarget.filename}"? This action cannot be undone.
                  </p>
                </div>
              </div>
            </div>
            <div className="px-6 py-4 bg-gray-50 dark:bg-gray-900 rounded-b-lg flex justify-end gap-3">
              <button
                onClick={() => setDeleteTarget(null)}
                className="px-4 py-2 text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-600"
              >
                Cancel
              </button>
              <button
                onClick={confirmDelete}
                className="px-4 py-2 text-white rounded-lg bg-red-600 hover:bg-red-700"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Discard Changes Confirmation Modal */}
      {discardChangesTarget && (
        <div className="fixed inset-0 bg-black/50 dark:bg-black/70 flex items-center justify-center z-[60]">
          <div className="glass-panel rounded-lg shadow-xl max-w-md w-full m-4">
            <div className="p-6">
              <div className="flex items-center gap-4">
                <div className="flex-shrink-0 w-12 h-12 rounded-full bg-yellow-100 dark:bg-yellow-900/30 flex items-center justify-center">
                  <svg className="w-6 h-6 text-yellow-600 dark:text-yellow-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                  </svg>
                </div>
                <div>
                  <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">Unsaved Changes</h3>
                  <p className="mt-1 text-sm text-gray-600 dark:text-gray-400">
                    You have unsaved changes. Are you sure you want to close without saving?
                  </p>
                </div>
              </div>
            </div>
            <div className="px-6 py-4 bg-gray-50 dark:bg-gray-900 rounded-b-lg flex justify-end gap-3">
              <button
                onClick={() => setDiscardChangesTarget(false)}
                className="px-4 py-2 text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-600"
              >
                Cancel
              </button>
              <button
                onClick={confirmDiscardChanges}
                className="px-4 py-2 text-white rounded-lg bg-yellow-600 hover:bg-yellow-700"
              >
                Discard Changes
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
