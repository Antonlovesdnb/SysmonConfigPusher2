import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { deploymentsApi } from '../api';
import { useDeploymentHub } from '../hooks/useDeploymentHub';
import { useUserPreferences } from '../context/UserPreferencesContext';
import type { DeploymentJobDetail, DeploymentResult } from '../types';

export function DeploymentDetailPage() {
  const { formatTimestamp } = useUserPreferences();
  const { id } = useParams<{ id: string }>();
  const jobId = id ? parseInt(id, 10) : null;

  const [job, setJob] = useState<DeploymentJobDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState(false);

  const { connected, progress, completed } = useDeploymentHub(
    job?.status === 'Running' ? jobId : null
  );

  const fetchJob = async () => {
    if (!jobId) return;
    setLoading(true);
    try {
      const data = await deploymentsApi.get(jobId);
      setJob(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load deployment');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchJob();
  }, [jobId]);

  // Refresh when deployment completes
  useEffect(() => {
    if (completed) {
      fetchJob();
    }
  }, [completed]);

  const cancelJob = async () => {
    if (!jobId) return;
    setCancelling(true);
    try {
      await deploymentsApi.cancel(jobId);
      await fetchJob();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to cancel deployment');
    } finally {
      setCancelling(false);
    }
  };

  const getStatusBadge = (status: string) => {
    const styles: Record<string, string> = {
      Pending: 'bg-gray-100 text-gray-800',
      Running: 'bg-slate-100 text-slate-800',
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

  // Merge API results with real-time progress
  const getMergedResults = (): DeploymentResult[] => {
    if (!job) return [];

    const resultsMap = new Map<number, DeploymentResult>();

    // Start with API results
    for (const r of job.results) {
      resultsMap.set(r.computerId, r);
    }

    // Overlay real-time progress
    for (const p of progress) {
      const existing = resultsMap.get(p.computerId);
      if (existing) {
        resultsMap.set(p.computerId, {
          ...existing,
          success: p.success,
          message: p.message,
          completedAt: new Date().toISOString(),
        });
      }
    }

    return Array.from(resultsMap.values());
  };

  if (loading && !job) {
    return (
      <div className="glass-card rounded-lg p-6">
        <div className="text-center py-8 text-gray-500 dark:text-gray-400">Loading...</div>
      </div>
    );
  }

  if (error && !job) {
    return (
      <div className="glass-card rounded-lg p-6">
        <div className="p-4 bg-red-100 dark:bg-red-900/30 text-red-700 dark:text-red-400 rounded-lg">{error}</div>
        <div className="mt-4">
          <Link to="/deployments" className="text-slate-600 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-300">
            Back to Deployments
          </Link>
        </div>
      </div>
    );
  }

  if (!job) return null;

  const results = getMergedResults();
  const successCount = results.filter((r) => r.success && r.completedAt).length;
  const failureCount = results.filter((r) => !r.success && r.completedAt && r.message !== 'Pending').length;
  const pendingCount = results.filter((r) => !r.completedAt || r.message === 'Pending').length;
  const totalCount = results.length;
  const completedCount = successCount + failureCount;

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="glass-card rounded-lg p-6">
        <div className="flex justify-between items-start mb-4">
          <div>
            <div className="flex items-center gap-3">
              <h2 className="text-xl font-semibold text-gray-800 dark:text-gray-200">
                Deployment #{job.id}
              </h2>
              <span className={`px-2 py-1 rounded text-sm font-medium ${getStatusBadge(job.status)}`}>
                {job.status}
              </span>
              {job.status === 'Running' && connected && (
                <span className="px-2 py-1 bg-green-100 text-green-700 rounded text-xs">
                  Live
                </span>
              )}
            </div>
            <div className="mt-2 text-gray-600 dark:text-gray-400">
              {getOperationLabel(job.operation)}
              {job.configFilename && (
                <span className="ml-2 text-gray-500 dark:text-gray-400">using {job.configFilename}</span>
              )}
            </div>
          </div>
          <div className="flex gap-2">
            {job.status === 'Running' && (
              <button
                onClick={cancelJob}
                disabled={cancelling}
                className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50"
              >
                {cancelling ? 'Cancelling...' : 'Cancel'}
              </button>
            )}
            <button
              onClick={fetchJob}
              disabled={loading}
              className="px-4 py-2 bg-gray-200 dark:bg-gray-600 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-300 dark:hover:bg-gray-500 disabled:opacity-50"
            >
              Refresh
            </button>
            <Link
              to="/deployments"
              className="px-4 py-2 border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-300 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700"
            >
              Back
            </Link>
          </div>
        </div>

        {/* Progress bar */}
        <div className="mt-4">
          <div className="flex justify-between text-sm text-gray-500 dark:text-gray-400 mb-1">
            <span>Progress</span>
            <span>
              {completedCount} / {totalCount}
              {failureCount > 0 && <span className="text-red-500 dark:text-red-400 ml-1">({failureCount} failed)</span>}
            </span>
          </div>
          <div className="h-3 bg-gray-200 dark:bg-gray-600 rounded-full overflow-hidden">
            <div className="h-full flex">
              <div
                className="bg-green-500 transition-all"
                style={{ width: `${(successCount / totalCount) * 100}%` }}
              />
              <div
                className="bg-red-500 transition-all"
                style={{ width: `${(failureCount / totalCount) * 100}%` }}
              />
            </div>
          </div>
        </div>

        {/* Summary stats */}
        <div className="mt-4 grid grid-cols-4 gap-4 text-center">
          <div className="p-3 bg-gray-50 dark:bg-gray-700 rounded-lg">
            <div className="text-2xl font-semibold text-gray-900 dark:text-gray-100">{totalCount}</div>
            <div className="text-sm text-gray-500 dark:text-gray-400">Total</div>
          </div>
          <div className="p-3 bg-green-50 dark:bg-green-900/30 rounded-lg">
            <div className="text-2xl font-semibold text-green-600 dark:text-green-400">{successCount}</div>
            <div className="text-sm text-gray-500 dark:text-gray-400">Success</div>
          </div>
          <div className="p-3 bg-red-50 dark:bg-red-900/30 rounded-lg">
            <div className="text-2xl font-semibold text-red-600 dark:text-red-400">{failureCount}</div>
            <div className="text-sm text-gray-500 dark:text-gray-400">Failed</div>
          </div>
          <div className="p-3 bg-slate-50 dark:bg-slate-800 rounded-lg">
            <div className="text-2xl font-semibold text-slate-600 dark:text-slate-300">{pendingCount}</div>
            <div className="text-sm text-gray-500 dark:text-gray-400">Pending</div>
          </div>
        </div>

        {/* Metadata */}
        <div className="mt-4 pt-4 border-t dark:border-gray-700 text-sm text-gray-500 dark:text-gray-400 flex gap-6">
          <div>
            <span className="font-medium">Started by:</span> {job.startedBy || 'Unknown'}
          </div>
          <div>
            <span className="font-medium">Started:</span>{' '}
            {formatTimestamp(job.startedAt)}
          </div>
          {job.completedAt && (
            <div>
              <span className="font-medium">Completed:</span>{' '}
              {formatTimestamp(job.completedAt)}
            </div>
          )}
        </div>
      </div>

      {/* Results table */}
      <div className="glass-card rounded-lg p-6">
        <h3 className="text-lg font-semibold mb-4 text-gray-900 dark:text-gray-100">Results</h3>
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200/50 dark:divide-gray-700/50">
            <thead className="bg-gray-50 dark:bg-gray-700">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                  Hostname
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                  Status
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                  Message
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">
                  Completed At
                </th>
              </tr>
            </thead>
            <tbody className="bg-white dark:bg-gray-800 divide-y divide-gray-200/50 dark:divide-gray-700/50">
              {results.map((result) => {
                const isPending = !result.completedAt || result.message === 'Pending';
                const isRunning = progress.some(
                  (p) => p.computerId === result.computerId && p.status === 'Running'
                );

                return (
                  <tr key={result.id} className="hover:bg-gray-50 dark:hover:bg-gray-700">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-gray-100">
                      {result.hostname}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {isPending ? (
                        isRunning ? (
                          <span className="px-2 py-1 bg-slate-100 text-slate-800 rounded text-xs font-medium">
                            Running...
                          </span>
                        ) : (
                          <span className="px-2 py-1 bg-gray-100 text-gray-600 rounded text-xs font-medium">
                            Pending
                          </span>
                        )
                      ) : result.success ? (
                        <span className="px-2 py-1 bg-green-100 text-green-800 rounded text-xs font-medium">
                          Success
                        </span>
                      ) : (
                        <span className="px-2 py-1 bg-red-100 text-red-800 rounded text-xs font-medium">
                          Failed
                        </span>
                      )}
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-500 dark:text-gray-400">
                      {result.message || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-gray-400">
                      {result.completedAt && result.message !== 'Pending'
                        ? formatTimestamp(result.completedAt)
                        : '-'}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
