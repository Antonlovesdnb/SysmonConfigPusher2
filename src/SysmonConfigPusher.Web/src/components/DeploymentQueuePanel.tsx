import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useDeploymentQueue, type QueuedDeployment } from '../context/DeploymentQueueContext';
import { deploymentsApi } from '../api';
import type { ScheduledDeployment } from '../types';

type TabType = 'queue' | 'scheduled';

export function DeploymentQueuePanel() {
  const navigate = useNavigate();
  const { queue, removeFromQueue, updateScheduledAt, clearQueue, isQueuePanelOpen, setQueuePanelOpen } = useDeploymentQueue();
  const [isRunning, setIsRunning] = useState(false);
  const [runProgress, setRunProgress] = useState<{ current: number; total: number; results: { id: string; success: boolean; jobId?: number }[] } | null>(null);
  const [activeTab, setActiveTab] = useState<TabType>('queue');
  const [scheduledDeployments, setScheduledDeployments] = useState<ScheduledDeployment[]>([]);
  const [loadingScheduled, setLoadingScheduled] = useState(false);

  useEffect(() => {
    if (isQueuePanelOpen && activeTab === 'scheduled') {
      loadScheduledDeployments();
    }
  }, [isQueuePanelOpen, activeTab]);

  const loadScheduledDeployments = async () => {
    setLoadingScheduled(true);
    try {
      const data = await deploymentsApi.getScheduled();
      setScheduledDeployments(data);
    } catch (err) {
      console.error('Failed to load scheduled deployments:', err);
    } finally {
      setLoadingScheduled(false);
    }
  };

  if (!isQueuePanelOpen) return null;

  const handleRunQueue = async () => {
    if (queue.length === 0) return;

    setIsRunning(true);
    const immediateItems = queue.filter(item => !item.scheduledAt);
    const scheduledItems = queue.filter(item => item.scheduledAt);
    const total = queue.length;

    setRunProgress({ current: 0, total, results: [] });

    const results: { id: string; success: boolean; jobId?: number }[] = [];
    let processed = 0;

    // Process immediate deployments
    for (const item of immediateItems) {
      processed++;
      setRunProgress((prev) => prev ? { ...prev, current: processed } : null);

      try {
        const job = await deploymentsApi.start(
          item.operation,
          item.computers.map((c) => c.id),
          item.config?.id,
          item.sysmonVersion ?? undefined
        );
        results.push({ id: item.id, success: true, jobId: job.id });
      } catch (err) {
        results.push({ id: item.id, success: false });
      }

      setRunProgress((prev) => prev ? { ...prev, results: [...results] } : null);
    }

    // Process scheduled deployments
    for (const item of scheduledItems) {
      processed++;
      setRunProgress((prev) => prev ? { ...prev, current: processed } : null);

      try {
        await deploymentsApi.schedule(
          item.operation,
          item.computers.map((c) => c.id),
          item.scheduledAt!,
          item.config?.id
        );
        results.push({ id: item.id, success: true });
      } catch (err) {
        results.push({ id: item.id, success: false });
      }

      setRunProgress((prev) => prev ? { ...prev, results: [...results] } : null);
    }

    // Clear queue after running
    clearQueue();
    setIsRunning(false);

    // Refresh scheduled list if we added any
    if (scheduledItems.length > 0) {
      await loadScheduledDeployments();
    }

    // If all succeeded, navigate to deployments page (for immediate ones)
    const allSucceeded = results.every((r) => r.success);
    if (allSucceeded && immediateItems.length > 0) {
      const immediateResults = results.filter(r => immediateItems.some(i => i.id === r.id));
      if (immediateResults.length === 1 && immediateResults[0].jobId) {
        navigate(`/deployments/${immediateResults[0].jobId}`);
      } else if (immediateResults.length > 0) {
        navigate('/deployments');
      }
      setQueuePanelOpen(false);
    } else if (allSucceeded && scheduledItems.length > 0) {
      // Switch to scheduled tab to show the new scheduled items
      setActiveTab('scheduled');
    }

    setRunProgress(null);
  };

  const handleCancelScheduled = async (id: number) => {
    try {
      await deploymentsApi.cancelScheduled(id);
      setScheduledDeployments(prev => prev.filter(s => s.id !== id));
    } catch (err) {
      console.error('Failed to cancel scheduled deployment:', err);
    }
  };

  const totalComputers = queue.reduce((sum, item) => sum + item.computers.length, 0);
  const hasScheduledItems = queue.some(item => item.scheduledAt);
  const hasImmediateItems = queue.some(item => !item.scheduledAt);

  return (
    <div className="fixed right-0 top-0 h-full w-96 glass-panel shadow-2xl z-50 flex flex-col">
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

      {/* Tabs */}
      <div className="flex border-b dark:border-gray-700">
        <button
          onClick={() => setActiveTab('queue')}
          className={`flex-1 px-4 py-2 text-sm font-medium ${
            activeTab === 'queue'
              ? 'text-slate-700 dark:text-slate-300 border-b-2 border-slate-700 dark:border-slate-400'
              : 'text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300'
          }`}
        >
          Queue ({queue.length})
        </button>
        <button
          onClick={() => setActiveTab('scheduled')}
          className={`flex-1 px-4 py-2 text-sm font-medium ${
            activeTab === 'scheduled'
              ? 'text-slate-700 dark:text-slate-300 border-b-2 border-slate-700 dark:border-slate-400'
              : 'text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300'
          }`}
        >
          Scheduled ({scheduledDeployments.length})
        </button>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-4 space-y-3">
        {activeTab === 'queue' ? (
          queue.length === 0 ? (
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
                onScheduleChange={(scheduledAt) => updateScheduledAt(item.id, scheduledAt)}
                disabled={isRunning}
              />
            ))
          )
        ) : (
          loadingScheduled ? (
            <div className="text-center text-gray-500 dark:text-gray-400 py-8">
              Loading scheduled deployments...
            </div>
          ) : scheduledDeployments.length === 0 ? (
            <div className="text-center text-gray-500 dark:text-gray-400 py-8">
              <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-gray-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <p>No scheduled deployments</p>
              <p className="text-sm mt-1">Set a schedule time on queue items</p>
            </div>
          ) : (
            scheduledDeployments.map((item) => (
              <ScheduledItem
                key={item.id}
                item={item}
                onCancel={() => handleCancelScheduled(item.id)}
              />
            ))
          )
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
              Processing deployments...
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
      {activeTab === 'queue' && (
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
            {hasScheduledItems && hasImmediateItems
              ? 'Run & Schedule All'
              : hasScheduledItems
              ? 'Schedule All'
              : 'Run All Deployments'}
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
      )}
    </div>
  );
}

function QueueItem({
  item,
  index,
  onRemove,
  onScheduleChange,
  disabled,
}: {
  item: QueuedDeployment;
  index: number;
  onRemove: () => void;
  onScheduleChange: (scheduledAt: string | null) => void;
  disabled: boolean;
}) {
  const [showSchedulePicker, setShowSchedulePicker] = useState(false);

  const formatScheduledTime = (isoString: string) => {
    const date = new Date(isoString);
    return date.toLocaleString();
  };

  const getMinDateTime = () => {
    const now = new Date();
    now.setMinutes(now.getMinutes() + 5); // At least 5 minutes in the future
    return now.toISOString().slice(0, 16);
  };

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

      {item.sysmonVersion && (
        <div className="text-sm text-gray-600 dark:text-gray-300 mb-1">
          <span className="text-gray-500 dark:text-gray-400">Sysmon:</span> v{item.sysmonVersion}
        </div>
      )}

      <div className="text-sm text-gray-600 dark:text-gray-300">
        <span className="text-gray-500 dark:text-gray-400">Targets:</span>{' '}
        {item.computers.length <= 3
          ? item.computers.map((c) => c.hostname).join(', ')
          : `${item.computers.slice(0, 2).map((c) => c.hostname).join(', ')} +${item.computers.length - 2} more`}
      </div>

      {/* Schedule section */}
      <div className="mt-2 pt-2 border-t dark:border-gray-600">
        {item.scheduledAt ? (
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-1 text-sm text-amber-600 dark:text-amber-400">
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <span>{formatScheduledTime(item.scheduledAt)}</span>
            </div>
            <button
              onClick={() => onScheduleChange(null)}
              disabled={disabled}
              className="text-xs text-gray-500 dark:text-gray-400 hover:text-red-500 dark:hover:text-red-400"
            >
              Clear
            </button>
          </div>
        ) : showSchedulePicker ? (
          <div className="space-y-2">
            <input
              type="datetime-local"
              min={getMinDateTime()}
              onChange={(e) => {
                if (e.target.value) {
                  onScheduleChange(new Date(e.target.value).toISOString());
                  setShowSchedulePicker(false);
                }
              }}
              className="w-full px-2 py-1 text-sm border dark:border-gray-600 rounded bg-white dark:bg-gray-600 text-gray-900 dark:text-gray-100"
              disabled={disabled}
            />
            <button
              onClick={() => setShowSchedulePicker(false)}
              className="text-xs text-gray-500 dark:text-gray-400"
            >
              Cancel
            </button>
          </div>
        ) : (
          <button
            onClick={() => setShowSchedulePicker(true)}
            disabled={disabled}
            className="flex items-center gap-1 text-sm text-slate-600 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200"
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            Schedule for later
          </button>
        )}
      </div>

      <div className="text-xs text-gray-400 dark:text-gray-500 mt-2">
        Added {new Date(item.addedAt).toLocaleTimeString()}
      </div>
    </div>
  );
}

function ScheduledItem({
  item,
  onCancel,
}: {
  item: ScheduledDeployment;
  onCancel: () => void;
}) {
  const formatScheduledTime = (isoString: string) => {
    const date = new Date(isoString);
    return date.toLocaleString();
  };

  const getTimeUntil = (isoString: string) => {
    const date = new Date(isoString);
    const now = new Date();
    const diff = date.getTime() - now.getTime();

    if (diff < 0) return 'Overdue';

    const hours = Math.floor(diff / (1000 * 60 * 60));
    const minutes = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60));

    if (hours > 24) {
      const days = Math.floor(hours / 24);
      return `in ${days} day${days !== 1 ? 's' : ''}`;
    }
    if (hours > 0) {
      return `in ${hours}h ${minutes}m`;
    }
    return `in ${minutes}m`;
  };

  const operationLabels: Record<string, string> = {
    install: 'Install Sysmon',
    update: 'Update Config',
    uninstall: 'Uninstall Sysmon',
    test: 'Test Connectivity',
  };

  return (
    <div className="border dark:border-gray-700 rounded-lg p-3 bg-amber-50 dark:bg-amber-900/20">
      <div className="flex justify-between items-start mb-2">
        <div className="flex items-center gap-2">
          <svg className="w-5 h-5 text-amber-600 dark:text-amber-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <span className="font-medium text-gray-900 dark:text-gray-100">
            {operationLabels[item.operation] || item.operation}
          </span>
        </div>
        {item.status === 'Pending' && (
          <button
            onClick={onCancel}
            className="text-gray-400 dark:text-gray-500 hover:text-red-500 dark:hover:text-red-400"
            title="Cancel scheduled deployment"
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        )}
      </div>

      {item.configFilename && (
        <div className="text-sm text-gray-600 dark:text-gray-300 mb-1">
          <span className="text-gray-500 dark:text-gray-400">Config:</span> {item.configFilename}
          {item.configTag && (
            <span className="ml-1 px-1.5 py-0.5 bg-slate-200 dark:bg-slate-600 text-slate-700 dark:text-slate-200 rounded text-xs">
              {item.configTag}
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

      <div className="flex items-center justify-between mt-2 pt-2 border-t dark:border-amber-800">
        <div className="text-sm text-amber-700 dark:text-amber-400 font-medium">
          {formatScheduledTime(item.scheduledAt)}
        </div>
        <span className="text-xs px-2 py-0.5 bg-amber-200 dark:bg-amber-800 text-amber-800 dark:text-amber-200 rounded">
          {getTimeUntil(item.scheduledAt)}
        </span>
      </div>

      {item.status === 'Running' && item.deploymentJobId && (
        <div className="mt-2 text-xs text-blue-600 dark:text-blue-400">
          Running as job #{item.deploymentJobId}
        </div>
      )}

      <div className="text-xs text-gray-400 dark:text-gray-500 mt-2">
        Scheduled by {item.createdBy || 'unknown'}
      </div>
    </div>
  );
}
