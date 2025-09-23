/**
 * Changelog toaster notification component
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import React, { useEffect, useState, useCallback } from 'react';
import { X, Newspaper, ExternalLink } from 'lucide-react';
import { ChangelogToasterProps } from '../types';
import { useChangelog, useAutoChangelogToaster } from '../hooks';
import { useChangelogContext } from '../../../contexts/ChangelogContext';
import ChangelogEntry from './ChangelogEntry';

/**
 * Changelog toaster component
 */
const ChangelogToaster: React.FC<ChangelogToasterProps> = ({
  manualControl = false,
  positionClass = 'top-4 right-4',
  autoHideTimeout = 10000,
}) => {
  const { data: changelogData } = useChangelog();
  const { openModal } = useChangelogContext();
  
  // Get current version and changes for auto-toaster
  const currentVersion = changelogData?.currentVersion;
  const currentVersionData = changelogData?.versions.find(v => v.version === currentVersion);
  const currentChanges = currentVersionData?.changes || [];
  
  // Use auto toaster hook
  const { toaster, hideToaster } = useAutoChangelogToaster(
    manualControl ? undefined : currentVersion,
    manualControl ? undefined : currentChanges
  );
  
  const [isVisible, setIsVisible] = useState(false);
  const [isLeaving, setIsLeaving] = useState(false);

  // Handle visibility animations
  useEffect(() => {
    if (toaster.isVisible) {
      setIsLeaving(false);
      const timer = setTimeout(() => setIsVisible(true), 100);
      return () => clearTimeout(timer);
    } else {
      setIsVisible(false);
    }
  }, [toaster.isVisible]);

  // Handle close with animation
  const handleClose = useCallback(() => {
    setIsLeaving(true);
    setTimeout(() => {
      hideToaster();
      setIsLeaving(false);
    }, 300);
  }, [hideToaster]);

  // Handle click to view more (open modal)
  const handleViewMore = useCallback(() => {
    openModal();
    handleClose();
  }, [openModal, handleClose]);

  // Don't render if not visible
  if (!toaster.isVisible && !isLeaving) {
    return null;
  }

  const transformClass = isLeaving
    ? 'translate-x-full opacity-0'
    : isVisible
      ? 'translate-x-0 opacity-100'
      : 'translate-x-full opacity-0';

  const changeCount = toaster.changes?.length || 0;
  const displayChanges = toaster.changes?.slice(0, 3) || [];

  return (
    <div 
      className={`fixed ${positionClass} z-50 transition-all duration-300 ease-in-out ${transformClass}`}
      style={{ maxWidth: '400px' }}
    >
      <div className="bg-white border border-gray-200 rounded-lg shadow-lg ring-1 ring-black ring-opacity-5 overflow-hidden">
        {/* Header */}
        <div className="bg-gradient-to-r from-indigo-500 to-purple-600 px-4 py-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-2">
              <Newspaper className="h-5 w-5 text-white" />
              <h3 className="text-white font-medium">
                Co je nové v {toaster.version}
              </h3>
            </div>
            <button
              onClick={handleClose}
              className="text-white/80 hover:text-white focus:outline-none focus:ring-2 focus:ring-white/50 rounded transition-colors"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        {/* Content */}
        <div className="p-4">
          {/* Progress bar for auto-hide */}
          {toaster.isAutoHiding && (
            <div className="mb-3">
              <div className="h-1 bg-gray-200 rounded-full overflow-hidden">
                <div 
                  className="h-full bg-indigo-500 rounded-full"
                  style={{
                    animation: `progressShrink ${autoHideTimeout}ms linear`,
                    transformOrigin: 'left',
                  }}
                />
              </div>
            </div>
          )}

          {/* Changes list */}
          <div className="space-y-2 mb-4">
            {displayChanges.map((change, index) => (
              <ChangelogEntry
                key={`${change.source}-${change.hash || change.id}-${index}`}
                entry={change}
                compact={true}
                showSource={false}
              />
            ))}
          </div>

          {/* Show more indicator */}
          {changeCount > 3 && (
            <p className="text-xs text-gray-500 mb-3">
              a {changeCount - 3} dalších změn...
            </p>
          )}

          {/* Action buttons */}
          <div className="flex space-x-2">
            <button
              onClick={handleViewMore}
              className="flex-1 inline-flex items-center justify-center px-3 py-2 text-sm font-medium text-indigo-700 bg-indigo-50 border border-indigo-200 rounded-md hover:bg-indigo-100 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 transition-colors"
            >
              <ExternalLink className="h-4 w-4 mr-1" />
              Zobrazit vše
            </button>
            <button
              onClick={handleClose}
              className="px-3 py-2 text-sm font-medium text-gray-700 bg-gray-50 border border-gray-200 rounded-md hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500 transition-colors"
            >
              Zavřít
            </button>
          </div>
        </div>
      </div>

    </div>
  );
};

export default ChangelogToaster;