import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { computersApi } from '../api';
import type { Computer } from '../types';

export function ComputersPage() {
  const navigate = useNavigate();
  const [computers, setComputers] = useState<Computer[]>([]);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());

  const fetchComputers = useCallback(async () => {
    setLoading(true);
    try {
      const data = await computersApi.list(search || undefined);
      setComputers(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load computers');
    } finally {
      setLoading(false);
    }
  }, [search]);

  useEffect(() => {
    fetchComputers();
  }, [fetchComputers]);

  const refreshFromAD = async () => {
    setRefreshing(true);
    try {
      const result = await computersApi.refresh();
      setError(null);
      await fetchComputers();
      alert(result.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to refresh from AD');
    } finally {
      setRefreshing(false);
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
      <div className="bg-white rounded-lg shadow p-6">
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-xl font-semibold text-gray-800">Inventory</h2>
          <div className="flex gap-2">
            <input
              type="text"
              placeholder="Search hostname..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="px-3 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-slate-500"
            />
            <button
              onClick={fetchComputers}
              disabled={loading}
              className="px-4 py-2 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 disabled:opacity-50"
            >
              Search
            </button>
            <button
              onClick={refreshFromAD}
              disabled={refreshing}
              className="px-4 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800 disabled:opacity-50"
            >
              {refreshing ? 'Refreshing...' : 'Refresh from AD'}
            </button>
          </div>
        </div>

        {error && (
          <div className="mb-4 p-4 bg-red-100 text-red-700 rounded-lg">{error}</div>
        )}

        {selectedIds.size > 0 && (
          <div className="mb-4 p-4 bg-slate-50 border border-slate-200 rounded-lg flex items-center justify-between">
            <span className="text-slate-800">
              {selectedIds.size} computer{selectedIds.size !== 1 ? 's' : ''} selected
            </span>
            <div className="flex gap-2">
              <button
                onClick={() => setSelectedIds(new Set())}
                className="px-3 py-1 text-slate-600 hover:text-slate-800"
              >
                Clear Selection
              </button>
              <button
                onClick={deployToSelected}
                className="px-4 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800"
              >
                Deploy to Selected
              </button>
            </div>
          </div>
        )}

        {loading ? (
          <div className="text-center py-8 text-gray-500">Loading...</div>
        ) : computers.length === 0 ? (
          <div className="text-center py-8 text-gray-500">
            No computers found. Click "Refresh from AD" to enumerate computers from Active
            Directory.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-3 text-left">
                    <input
                      type="checkbox"
                      checked={selectedIds.size === computers.length && computers.length > 0}
                      onChange={selectAll}
                      className="w-4 h-4 rounded border-gray-300"
                    />
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Hostname
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Operating System
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Sysmon Version
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Last Deployment
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {computers.map((computer) => (
                  <tr
                    key={computer.id}
                    className={`hover:bg-gray-50 cursor-pointer ${
                      selectedIds.has(computer.id) ? 'bg-slate-50' : ''
                    }`}
                    onClick={() => toggleSelection(computer.id)}
                  >
                    <td className="px-4 py-4">
                      <input
                        type="checkbox"
                        checked={selectedIds.has(computer.id)}
                        onChange={() => toggleSelection(computer.id)}
                        onClick={(e) => e.stopPropagation()}
                        className="w-4 h-4 rounded border-gray-300"
                      />
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                      {computer.hostname}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {computer.operatingSystem || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {computer.sysmonVersion || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {computer.lastDeployment
                        ? new Date(computer.lastDeployment).toLocaleString()
                        : '-'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="mt-4 text-sm text-gray-500">Total: {computers.length} computers</div>
      </div>
    </div>
  );
}
