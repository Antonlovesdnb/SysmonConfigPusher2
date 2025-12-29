import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useDeploymentQueue, type QueuedDeployment } from '../context/DeploymentQueueContext';
import { deploymentsApi } from '../api';

export function DeploymentQueuePanel() {
  const navigate = useNavigate();
  const { queue, removeFromQueue, clearQueue, isQueuePanelOpen, setQueuePanelOpen } = useDeploymentQueue();
  const [isRunning, setIsRunning] = useState(false);
  const [runProgress, setRunProgress] = useState<{ current: number; total: number; results: { id: string; success: boolean; jobId?: number }[] } | null>(null);

  if (!isQueuePanelOpen) return null;

  const handleRunQueue = async () => {
    if (queue.length === 0) return;

    setIsRunning(true);
    setRunProgress({ current: 0, total: queue.length, results: [] });

    const results: { id: string; success: boolean; jobId?: number }[] = [];

    for (let i = 0; i < queue.length; i++) {
      const item = queue[i];
      setRunProgress((prev) => prev ? { ...prev, current: i + 1 } : null);

      try {
        const job = await deploymentsApi.start(
          item.operation,
          item.computers.map((c) => c.id),
          item.config?.id
        );
        results.push({ id: item.id, success: true, jobId: job.id });
      } catch (err) {
        results.push({ id: item.id, success: false });
      }

      setRunProgress((prev) => prev ? { ...prev, results: [...results] } : null);
    }

    // Clear queue after running
    clearQueue();
    setIsRunning(false);

    // If all succeeded, navigate to deployments page
    const allSucceeded = results.every((r) => r.success);
    if (allSucceeded && results.length > 0) {
      // Navigate to the first deployment if only one, otherwise to the list
      if (results.length === 1 && results[0].jobId) {
        navigate(`/deployments/${results[0].jobId}`);
      } else {
        navigate('/deployments');
      }
      setQueuePanelOpen(false);
    }

    setRunProgress(null);
  };

  const totalComputers = queue.reduce((sum, item) => sum + item.computers.length, 0);

  return (
    <div className="fixed right-0 top-0 h-full w-96 bg-white dark:bg-gray-800 shadow-2xl z-50 flex flex-col">
      {/* Header */}
      <div className="bg-slate-800 text-white p-4 flex justify-between items-center">
        <div>
          <h2 className="text-lg font-semibold">Deployment Queue</h2>
          <p className="text-sm text-slate-300">
            {queue.length} deployment{queue.length !== 1 ? 's' : ''} / {totalComputers} computer{totalComputers !== 1 ? 's' : ''}
          </p>
        </div>
        <button
          onClick={() => setQueuePanelOpen(false)}
          className="p-1 hover:bg-slate-700 rounded"
        >
          <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      {/* Queue items */}
      <div className="flex-1 overflow-y-auto p-4 space-y-3">
        {queue.length === 0 ? (
          <div className="text-center text-gray-500 dark:text-gray-400 py-8">
            <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-gray-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
            </svg>
            <p>Queue is empty</p>
            <p className="text-sm mt-1">Add deployments from the wizard</p>
          </div>
        ) : (
          queue.map((item, index) => (
            <QueueItem
              key={item.id}
              item={item}
              index={index}
              onRemove={() => removeFromQueue(item.id)}
              disabled={isRunning}
            />
          ))
        )}
      </div>

      {/* Progress indicator */}
      {runProgress && (
        <div className="px-4 pb-2">
          <div className="bg-blue-50 dark:bg-blue-900/30 border border-blue-200 dark:border-blue-800 rounded-lg p-3">
            <div className="flex items-center gap-2 text-blue-700 dark:text-blue-400 mb-2">
              <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
              Running deployments...
            </div>
            <div className="text-sm text-blue-600 dark:text-blue-300">
              {runProgress.current} of {runProgress.total} completed
            </div>
            <div className="mt-2 bg-blue-200 dark:bg-blue-800 rounded-full h-2">
              <div
                className="bg-blue-600 dark:bg-blue-500 h-2 rounded-full transition-all"
                style={{ width: `${(runProgress.current / runProgress.total) * 100}%` }}
              />
            </div>
          </div>
        </div>
      )}

      {/* Actions */}
      <div className="border-t dark:border-gray-700 p-4 space-y-2">
        <button
          onClick={handleRunQueue}
          disabled={queue.length === 0 || isRunning}
          className="w-full px-4 py-3 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed font-medium flex items-center justify-center gap-2"
        >
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          Run All Deployments
        </button>
        {queue.length > 0 && !isRunning && (
          <button
            onClick={clearQueue}
            className="w-full px-4 py-2 text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-lg text-sm"
          >
            Clear Queue
          </button>
        )}
      </div>
    </div>
  );
}

function QueueItem({
  item,
  index,
  onRemove,
  disabled,
}: {
  item: QueuedDeployment;
  index: number;
  onRemove: () => void;
  disabled: boolean;
}) {
  return (
    <div className="border dark:border-gray-700 rounded-lg p-3 bg-gray-50 dark:bg-gray-700">
      <div className="flex justify-between items-start mb-2">
        <div className="flex items-center gap-2">
          <span className="w-6 h-6 bg-slate-700 text-white rounded-full flex items-center justify-center text-xs font-medium">
            {index + 1}
          </span>
          <span className="font-medium text-gray-900 dark:text-gray-100">{item.operationLabel}</span>
        </div>
        <button
          onClick={onRemove}
          disabled={disabled}
          className="text-gray-400 dark:text-gray-500 hover:text-red-500 dark:hover:text-red-400 disabled:opacity-50"
        >
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      {item.config && (
        <div className="text-sm text-gray-600 dark:text-gray-300 mb-1">
          <span className="text-gray-500 dark:text-gray-400">Config:</span> {item.config.filename}
          {item.config.tag && (
            <span className="ml-1 px-1.5 py-0.5 bg-slate-200 dark:bg-slate-600 text-slate-700 dark:text-slate-200 rounded text-xs">
              {item.config.tag}
            </span>
          )}
        </div>
      )}

      <div className="text-sm text-gray-600 dark:text-gray-300">
        <span className="text-gray-500 dark:text-gray-400">Targets:</span>{' '}
        {item.computers.length <= 3
          ? item.computers.map((c) => c.hostname).join(', ')
          : `${item.computers.slice(0, 2).map((c) => c.hostname).join(', ')} +${item.computers.length - 2} more`}
      </div>

      <div className="text-xs text-gray-400 dark:text-gray-500 mt-2">
        Added {new Date(item.addedAt).toLocaleTimeString()}
      </div>
    </div>
  );
}
