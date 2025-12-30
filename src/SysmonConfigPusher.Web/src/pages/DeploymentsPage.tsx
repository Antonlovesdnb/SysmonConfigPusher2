import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { deploymentsApi } from '../api';
import type { DeploymentJob } from '../types';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';

export function DeploymentsPage() {
  const { isAdmin } = useAuth();
  const { showToast } = useToast();
  const [jobs, setJobs] = useState<DeploymentJob[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [purging, setPurging] = useState(false);
  const [showPurgeConfirm, setShowPurgeConfirm] = useState(false);
  const [purgeDays, setPurgeDays] = useState(30);

  const fetchJobs = async () => {
    setLoading(true);
    try {
      const data = await deploymentsApi.list(100);
      setJobs(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load deployments');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchJobs();
    const interval = setInterval(fetchJobs, 10000); // Refresh every 10s
    return () => clearInterval(interval);
  }, []);

  const handlePurge = async () => {
    setPurging(true);
    try {
      const result = await deploymentsApi.purge(purgeDays);
      showToast(result.message, 'success');
      setShowPurgeConfirm(false);
      fetchJobs();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to purge history');
    } finally {
      setPurging(false);
    }
  };

  const getStatusBadge = (status: string) => {
    const styles: Record<string, string> = {
      Pending: 'bg-gray-100 text-gray-800',
      Running: 'bg-blue-100 text-blue-800',
      Completed: 'bg-green-100 text-green-800',
      CompletedWithErrors: 'bg-yellow-100 text-yellow-800',
      Cancelled: 'bg-red-100 text-red-800',
    };
    return styles[status] || 'bg-gray-100 text-gray-800';
  };

  const getOperationLabel = (operation: string) => {
    const labels: Record<string, string> = {
      install: 'Install Sysmon',
      update: 'Update Config',
      pushconfig: 'Update Config',
      uninstall: 'Uninstall Sysmon',
      test: 'Test Connectivity',
    };
    return labels[operation.toLowerCase()] || operation;
  };

  return (
    <div className="space-y-4">
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-xl font-semibold text-gray-800 dark:text-gray-200">Deployment History</h2>
          <div className="flex gap-2">
            <Link
              to="/deploy"
              className="px-4 py-2 bg-slate-700 text-white rounded-lg hover:bg-slate-800"
            >
              New Deployment
            </Link>
            <button
              onClick={fetchJobs}
              disabled={loading}
              className="px-4 py-2 bg-gray-200 dark:bg-gray-600 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-300 dark:hover:bg-gray-500 disabled:opacity-50"
            >
              Refresh
            </button>
            {isAdmin && (
              <button
                onClick={() => setShowPurgeConfirm(true)}
                disabled={purging || jobs.length === 0}
                className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50"
              >
                Purge Old
              </button>
            )}
          </div>
        </div>

        {/* Purge Confirmation Dialog */}
        {showPurgeConfirm && (
          <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
            <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl p-6 max-w-md mx-4">
              <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
                Purge Deployment History
              </h3>
              <p className="text-gray-600 dark:text-gray-400 mb-4">
                This will permanently delete completed deployment jobs and their results older than the specified number of days.
              </p>
              <div className="mb-4">
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                  Delete jobs older than:
                </label>
                <select
                  value={purgeDays}
                  onChange={(e) => setPurgeDays(Number(e.target.value))}
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                >
                  <option value={7}>7 days</option>
                  <option value={14}>14 days</option>
                  <option value={30}>30 days</option>
                  <option value={60}>60 days</option>
                  <option value={90}>90 days</option>
                  <option value={0}>All completed jobs</option>
                </select>
              </div>
              <div className="flex gap-3 justify-end">
                <button
                  onClick={() => setShowPurgeConfirm(false)}
                  disabled={purging}
                  className="px-4 py-2 text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg"
                >
                  Cancel
                </button>
                <button
                  onClick={handlePurge}
                  disabled={purging}
                  className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50"
                >
                  {purging ? 'Purging...' : 'Purge'}
                </button>
              </div>
            </div>
          </div>
        )}

        {error && (
          <div className="mb-4 p-4 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400 rounded-lg">{error}</div>
        )}

        {loading && jobs.length === 0 ? (
          <div className="text-center py-8 text-gray-500 dark:text-gray-400">Loading...</div>
        ) : jobs.length === 0 ? (
          <div className="text-center py-8 text-gray-500 dark:text-gray-400">
            No deployments yet. Click "New Deployment" to start one.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
              <thead className="bg-gray-50 dark:bg-gray-700">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    ID
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Operation
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Config
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Status
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Progress
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Started By
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Started At
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white dark:bg-gray-800 divide-y divide-gray-200 dark:divide-gray-700">
                {jobs.map((job) => {
                  const completed = job.successCount + job.failureCount;
                  const progressPct = job.totalCount > 0 ? (completed / job.totalCount) * 100 : 0;

                  return (
                    <tr key={job.id} className="hover:bg-gray-50 dark:hover:bg-gray-700">
                      <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-gray-100">
                        #{job.id}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                        {getOperationLabel(job.operation)}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                        {job.configFilename || '-'}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <span
                          className={`px-2 py-1 rounded text-xs font-medium ${getStatusBadge(
                            job.status
                          )}`}
                        >
                          {job.status}
                        </span>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="flex items-center gap-2">
                          <div className="w-24 h-2 bg-gray-200 dark:bg-gray-600 rounded-full overflow-hidden">
                            <div
                              className="h-full bg-blue-500 transition-all"
                              style={{ width: `${progressPct}%` }}
                            />
                          </div>
                          <span className="text-xs text-gray-500 dark:text-gray-400">
                            {job.successCount}/{job.totalCount}
                            {job.failureCount > 0 && (
                              <span className="text-red-500 dark:text-red-400 ml-1">
                                ({job.failureCount} failed)
                              </span>
                            )}
                          </span>
                        </div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                        {job.startedBy || '-'}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                        {new Date(job.startedAt).toLocaleString()}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-right text-sm">
                        <Link
                          to={`/deployments/${job.id}`}
                          className="text-slate-600 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-300"
                        >
                          View Details
                        </Link>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
