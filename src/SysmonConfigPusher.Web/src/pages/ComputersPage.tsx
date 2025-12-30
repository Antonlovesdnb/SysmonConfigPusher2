import { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { computersApi } from '../api';
import type { Computer } from '../types';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';

export function ComputersPage() {
  const navigate = useNavigate();
  const { canDeploy } = useAuth();
  const { showToast } = useToast();
  const [computers, setComputers] = useState<Computer[]>([]);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [scanProgress, setScanProgress] = useState<{ scanned: number; total: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const scanPollRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const scanStartTimeRef = useRef<string | null>(null);

  const fetchComputers = useCallback(async () => {
    setLoading(true);
    try {
      const data = await computersApi.list(search || undefined);
      setComputers(data);
      setError(null);
      return data;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load computers');
      return null;
    } finally {
      setLoading(false);
    }
  }, [search]);

  useEffect(() => {
    fetchComputers();
  }, [fetchComputers]);

  // Cleanup polling on unmount
  useEffect(() => {
    return () => {
      if (scanPollRef.current) {
        clearInterval(scanPollRef.current);
      }
    };
  }, []);

  const refreshFromAD = async () => {
    setRefreshing(true);
    try {
      const result = await computersApi.refresh();
      setError(null);
      await fetchComputers();
      showToast(result.message, 'success');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to refresh from AD');
    } finally {
      setRefreshing(false);
    }
  };

  const scanInventory = async () => {
    setScanning(true);
    setScanProgress({ scanned: 0, total: computers.length });
    scanStartTimeRef.current = new Date().toISOString();

    try {
      await computersApi.scanAll();
      setError(null);

      // Start polling to track scan progress
      scanPollRef.current = setInterval(async () => {
        try {
          const updatedComputers = await computersApi.list(search || undefined);

          // Count how many computers have been scanned since we started
          const scannedCount = updatedComputers.filter((c) => {
            if (!c.lastInventoryScan) return false;
            return new Date(c.lastInventoryScan) >= new Date(scanStartTimeRef.current!);
          }).length;

          setScanProgress({ scanned: scannedCount, total: updatedComputers.length });

          // If all computers have been scanned, stop polling
          if (scannedCount >= updatedComputers.length) {
            if (scanPollRef.current) {
              clearInterval(scanPollRef.current);
              scanPollRef.current = null;
            }
            setComputers(updatedComputers);
            setScanning(false);
            setScanProgress(null);
            showToast(`Scan complete: ${scannedCount} computers scanned`, 'success');
          }
        } catch {
          // Ignore polling errors
        }
      }, 2000);

      // Fallback timeout after 2 minutes
      setTimeout(() => {
        if (scanPollRef.current) {
          clearInterval(scanPollRef.current);
          scanPollRef.current = null;
          setScanning(false);
          setScanProgress(null);
          fetchComputers();
          showToast('Scan timeout - refreshing results', 'info');
        }
      }, 120000);

    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start inventory scan');
      setScanning(false);
      setScanProgress(null);
    }
  };

  const toggleSelection = (id: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const selectAll = () => {
    if (selectedIds.size === computers.length) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(computers.map((c) => c.id)));
    }
  };

  const deployToSelected = () => {
    if (selectedIds.size === 0) return;
    navigate('/deploy', { state: { computerIds: Array.from(selectedIds) } });
  };

  return (
    <div className="space-y-4">
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-xl font-semibold text-gray-800 dark:text-gray-200">Inventory</h2>
          <div className="flex gap-2">
            <input
              type="text"
              placeholder="Search hostname..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
            />
            <button
              onClick={fetchComputers}
              disabled={loading}
              className="px-4 py-2 bg-gray-200 dark:bg-gray-600 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-300 dark:hover:bg-gray-500 disabled:opacity-50"
            >
              Search
            </button>
            {canDeploy && (
              <button
                onClick={refreshFromAD}
                disabled={refreshing || scanning}
                className="px-4 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800 disabled:opacity-50"
              >
                {refreshing ? 'Populating...' : 'Populate from AD'}
              </button>
            )}
            {canDeploy && (
              <button
                onClick={scanInventory}
                disabled={scanning || refreshing}
                className="px-4 py-2 bg-emerald-600 text-white rounded-lg hover:bg-emerald-700 disabled:opacity-50"
              >
                {scanning ? 'Scanning...' : 'Scan Inventory'}
              </button>
            )}
          </div>
        </div>

        {/* Scan Progress Bar */}
        {scanning && scanProgress && (
          <div className="mb-4">
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm font-medium text-emerald-700 dark:text-emerald-400">
                Scanning inventory...
              </span>
              <span className="text-sm text-gray-600 dark:text-gray-400">
                {scanProgress.scanned} / {scanProgress.total} computers
              </span>
            </div>
            <div className="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-2.5">
              <div
                className="bg-emerald-600 h-2.5 rounded-full transition-all duration-500"
                style={{
                  width: `${scanProgress.total > 0 ? (scanProgress.scanned / scanProgress.total) * 100 : 0}%`,
                }}
              />
            </div>
          </div>
        )}

        {error && (
          <div className="mb-4 p-4 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400 rounded-lg">{error}</div>
        )}

        {selectedIds.size > 0 && (
          <div className="mb-4 p-4 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg flex items-center justify-between">
            <span className="text-slate-800 dark:text-slate-200">
              {selectedIds.size} computer{selectedIds.size !== 1 ? 's' : ''} selected
            </span>
            <div className="flex gap-2">
              <button
                onClick={() => setSelectedIds(new Set())}
                className="px-3 py-1 text-slate-600 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200"
              >
                Clear Selection
              </button>
              {canDeploy && (
                <button
                  onClick={deployToSelected}
                  className="px-4 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800"
                >
                  Deploy to Selected
                </button>
              )}
            </div>
          </div>
        )}

        {loading ? (
          <div className="text-center py-8 text-gray-500 dark:text-gray-400">Loading...</div>
        ) : computers.length === 0 ? (
          <div className="text-center py-8 text-gray-500 dark:text-gray-400">
            No computers found. Click "Populate from AD" to enumerate computers from Active
            Directory.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
              <thead className="bg-gray-50 dark:bg-gray-700">
                <tr>
                  <th className="px-4 py-3 text-left">
                    <input
                      type="checkbox"
                      checked={selectedIds.size === computers.length && computers.length > 0}
                      onChange={selectAll}
                      className="w-4 h-4 rounded border-gray-300 dark:border-gray-600"
                    />
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Hostname
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Operating System
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Sysmon Status
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Config Tag
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Last Sysmon Scan
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white dark:bg-gray-800 divide-y divide-gray-200 dark:divide-gray-700">
                {computers.map((computer) => (
                  <tr
                    key={computer.id}
                    className={`hover:bg-gray-50 dark:hover:bg-gray-700 cursor-pointer ${
                      selectedIds.has(computer.id) ? 'bg-slate-50 dark:bg-slate-800' : ''
                    }`}
                    onClick={() => toggleSelection(computer.id)}
                  >
                    <td className="px-4 py-4">
                      <input
                        type="checkbox"
                        checked={selectedIds.has(computer.id)}
                        onChange={() => toggleSelection(computer.id)}
                        onClick={(e) => e.stopPropagation()}
                        className="w-4 h-4 rounded border-gray-300 dark:border-gray-600"
                      />
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-gray-100">
                      {computer.hostname}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                      {computer.operatingSystem || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      {computer.sysmonPath ? (
                        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 dark:bg-green-900/50 text-green-800 dark:text-green-400">
                          {computer.sysmonVersion || 'Installed'}
                        </span>
                      ) : computer.lastInventoryScan ? (
                        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400">
                          Not installed
                        </span>
                      ) : (
                        <span className="text-gray-400 dark:text-gray-500">Not scanned</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                      {computer.configTag ? (
                        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 dark:bg-blue-900/50 text-blue-800 dark:text-blue-400">
                          {computer.configTag}
                        </span>
                      ) : (
                        '-'
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                      {computer.lastInventoryScan
                        ? new Date(computer.lastInventoryScan).toLocaleString()
                        : '-'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="mt-4 text-sm text-gray-500 dark:text-gray-400">Total: {computers.length} computers</div>
      </div>
    </div>
  );
}
