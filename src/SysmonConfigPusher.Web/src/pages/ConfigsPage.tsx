import { useState, useEffect, useRef } from 'react';
import { configsApi } from '../api';
import type { Config, ConfigDetail } from '../types';

export function ConfigsPage() {
  const [configs, setConfigs] = useState<Config[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedConfig, setSelectedConfig] = useState<ConfigDetail | null>(null);
  const [viewerOpen, setViewerOpen] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

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
    const file = e.target.files?.[0];
    if (!file) return;

    setUploading(true);
    try {
      await configsApi.upload(file);
      await fetchConfigs();
      setError(null);
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to upload config');
    } finally {
      setUploading(false);
    }
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

  return (
    <div className="space-y-4">
      <div className="bg-white rounded-lg shadow p-6">
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-xl font-semibold text-gray-800">Sysmon Configurations</h2>
          <div className="flex gap-2">
            <input
              ref={fileInputRef}
              type="file"
              accept=".xml"
              onChange={handleUpload}
              className="hidden"
              id="config-upload"
            />
            <label
              htmlFor="config-upload"
              className={`px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 cursor-pointer ${
                uploading ? 'opacity-50 pointer-events-none' : ''
              }`}
            >
              {uploading ? 'Uploading...' : 'Upload Config'}
            </label>
            <button
              onClick={fetchConfigs}
              disabled={loading}
              className="px-4 py-2 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 disabled:opacity-50"
            >
              Refresh
            </button>
          </div>
        </div>

        {error && (
          <div className="mb-4 p-4 bg-red-100 text-red-700 rounded-lg">{error}</div>
        )}

        {loading ? (
          <div className="text-center py-8 text-gray-500">Loading...</div>
        ) : configs.length === 0 ? (
          <div className="text-center py-8 text-gray-500">
            No configs uploaded yet. Click "Upload Config" to add a Sysmon configuration file.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Filename
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Tag
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Hash (SHA256)
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Uploaded By
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Uploaded At
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {configs.map((config) => (
                  <tr key={config.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                      {config.filename}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {config.tag ? (
                        <span className="px-2 py-1 bg-slate-100 text-slate-700 rounded text-xs">
                          {config.tag}
                        </span>
                      ) : (
                        '-'
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 font-mono">
                      {config.hash.substring(0, 16)}...
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {config.uploadedBy || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {new Date(config.uploadedAt).toLocaleString()}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm">
                      <button
                        onClick={() => viewConfig(config.id)}
                        className="text-slate-600 hover:text-slate-800 mr-4"
                      >
                        View
                      </button>
                      <button
                        onClick={() => deleteConfig(config.id)}
                        className="text-red-600 hover:text-red-800"
                      >
                        Delete
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="mt-4 text-sm text-gray-500">Total: {configs.length} configs</div>
      </div>

      {/* Config Viewer Modal */}
      {viewerOpen && selectedConfig && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl max-w-4xl w-full max-h-[90vh] flex flex-col m-4">
            <div className="flex justify-between items-center p-4 border-b">
              <div>
                <h3 className="text-lg font-semibold">{selectedConfig.filename}</h3>
                {selectedConfig.tag && (
                  <span className="text-sm text-gray-500">Tag: {selectedConfig.tag}</span>
                )}
              </div>
              <button
                onClick={() => setViewerOpen(false)}
                className="text-gray-500 hover:text-gray-700 text-2xl leading-none"
              >
                &times;
              </button>
            </div>
            <div className="flex-1 overflow-auto p-4">
              <pre className="text-sm font-mono bg-gray-50 p-4 rounded overflow-x-auto whitespace-pre">
                {selectedConfig.content}
              </pre>
            </div>
            <div className="p-4 border-t flex justify-between items-center bg-gray-50">
              <span className="text-sm text-gray-500 font-mono">
                SHA256: {selectedConfig.hash}
              </span>
              <button
                onClick={() => setViewerOpen(false)}
                className="px-4 py-2 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
