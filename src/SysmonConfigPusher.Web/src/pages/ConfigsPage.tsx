import { useState, useEffect, useRef } from 'react';
import { configsApi } from '../api';
import type { Config, ConfigDetail } from '../types';
import { XmlEditor } from '../components/XmlEditor';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';

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

  const fetchConfigs = async () => {
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
  };

  useEffect(() => {
    fetchConfigs();
  }, []);

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

  const deleteConfig = async (id: number) => {
    if (!confirm('Are you sure you want to delete this config?')) return;

    try {
      await configsApi.delete(id);
      await fetchConfigs();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete config');
    }
  };

  const closeUploadResults = () => {
    setUploadResultsOpen(false);
    setUploadProgress(null);
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
      if (!confirm('You have unsaved changes. Are you sure you want to close?')) {
        return;
      }
    }
    setEditorOpen(false);
    setSelectedConfig(null);
    setEditedContent('');
    setHasChanges(false);
  };

  return (
    <div className="space-y-4">
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6 transition-colors">
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
              </>
            )}
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
              <tbody className="bg-white dark:bg-gray-800 divide-y divide-gray-200 dark:divide-gray-700">
                {configs.map((config) => (
                  <tr key={config.id} className="hover:bg-gray-50 dark:hover:bg-gray-700/50">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-gray-100">
                      {config.filename}
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
                          onClick={() => deleteConfig(config.id)}
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
          <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl flex flex-col m-4" style={{ width: 'calc(100vw - 64px)', height: 'calc(100vh - 64px)' }}>
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
          <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl flex flex-col m-4" style={{ width: 'calc(100vw - 64px)', height: 'calc(100vh - 64px)' }}>
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
          <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-lg w-full m-4">
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
    </div>
  );
}
