/**
 * Changelog modal for viewing version history
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import React, { useState, useEffect } from 'react';
import { X, Calendar, Package, AlertCircle, ExternalLink } from 'lucide-react';
import { ChangelogModalProps } from '../types';
import { useChangelog } from '../hooks';
import ChangelogEntry from './ChangelogEntry';

/**
 * Format date for display
 */
function formatDate(dateString: string): string {
  try {
    const date = new Date(dateString);
    return date.toLocaleDateString('cs-CZ', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  } catch {
    return dateString;
  }
}

/**
 * Calculate days ago
 */
function getDaysAgo(dateString: string): string {
  try {
    const date = new Date(dateString);
    const now = new Date();
    const diffTime = Math.abs(now.getTime() - date.getTime());
    const diffDays = Math.floor(diffTime / (1000 * 60 * 60 * 24));
    
    if (diffDays === 0) return 'dnes';
    if (diffDays === 1) return 'včera';
    if (diffDays < 7) return `před ${diffDays} dny`;
    if (diffDays < 30) return `před ${Math.floor(diffDays / 7)} týdny`;
    if (diffDays < 365) return `před ${Math.floor(diffDays / 30)} měsíci`;
    return `před ${Math.floor(diffDays / 365)} lety`;
  } catch {
    return '';
  }
}

/**
 * Generate GitHub release URL
 */
function getGitHubReleaseUrl(version: string): string {
  // Remove 'v' prefix if present
  const cleanVersion = version.startsWith('v') ? version : `v${version}`;
  return `https://github.com/onpaj/Anela.Heblo/releases/tag/${cleanVersion}`;
}

/**
 * Changelog modal component
 */
const ChangelogModal: React.FC<ChangelogModalProps> = ({
  isOpen,
  onClose,
  title = 'Co je nové',
}) => {
  const { data, isLoading, error } = useChangelog();
  const [selectedVersion, setSelectedVersion] = useState<string | null>(null);

  // Set current version as selected by default
  useEffect(() => {
    if (data && !selectedVersion) {
      setSelectedVersion(data.currentVersion);
    }
  }, [data, selectedVersion]);

  // Handle escape key
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };

    if (isOpen) {
      document.addEventListener('keydown', handleEscape);
      document.body.style.overflow = 'hidden';
    }

    return () => {
      document.removeEventListener('keydown', handleEscape);
      document.body.style.overflow = 'unset';
    };
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const selectedVersionData = data?.versions.find(v => v.version === selectedVersion);

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      {/* Backdrop */}
      <div 
        className="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"
        onClick={onClose}
      />

      {/* Modal */}
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative transform overflow-hidden rounded-lg bg-white shadow-xl transition-all w-full max-w-4xl max-h-[90vh] flex flex-col">
          {/* Header */}
          <div className="bg-gradient-to-r from-indigo-500 to-purple-600 px-6 py-4">
            <div className="flex items-center justify-between">
              <h3 className="text-lg font-medium text-white flex items-center">
                <Package className="h-5 w-5 mr-2" />
                {title}
              </h3>
              <button
                onClick={onClose}
                className="text-white/80 hover:text-white focus:outline-none focus:ring-2 focus:ring-white/50 rounded transition-colors"
              >
                <X className="h-5 w-5" />
              </button>
            </div>
            {data && (
              <p className="text-white/80 text-sm mt-1">
                Aktuální verze: {data.currentVersion}
              </p>
            )}
          </div>

          {/* Content */}
          <div className="flex-1 flex overflow-hidden">
            {/* Version sidebar */}
            <div className="w-64 bg-gray-50 border-r border-gray-200 overflow-y-auto">
              <div className="p-4">
                <h4 className="text-sm font-medium text-gray-900 mb-3">Verze</h4>
                
                {isLoading && (
                  <div className="space-y-2">
                    {[...Array(3)].map((_, i) => (
                      <div key={i} className="h-16 bg-gray-200 rounded animate-pulse" />
                    ))}
                  </div>
                )}

                {error && (
                  <div className="text-sm text-red-600 flex items-center">
                    <AlertCircle className="h-4 w-4 mr-1" />
                    Chyba načítání
                  </div>
                )}

                {data && (
                  <div className="space-y-2">
                    {data.versions.map((version) => (
                      <button
                        key={version.version}
                        onClick={() => setSelectedVersion(version.version)}
                        className={`w-full text-left p-3 rounded-lg border transition-colors ${
                          selectedVersion === version.version
                            ? 'border-indigo-300 bg-indigo-50 text-indigo-900'
                            : 'border-gray-200 hover:border-gray-300 hover:bg-gray-50'
                        }`}
                      >
                        <div className="flex items-center justify-between mb-1">
                          <span className="font-medium text-sm">v{version.version}</span>
                          {version.version === data.currentVersion && (
                            <span className="px-2 py-0.5 text-xs bg-green-100 text-green-800 rounded-full">
                              Aktuální
                            </span>
                          )}
                        </div>
                        <div className="text-xs text-gray-500 mb-1">
                          {formatDate(version.date)}
                        </div>
                        <div className="text-xs text-gray-400">
                          {getDaysAgo(version.date)}
                        </div>
                        <div className="text-xs text-gray-500 mt-1">
                          {version.changes.length} změn
                        </div>
                      </button>
                    ))}
                  </div>
                )}
              </div>
            </div>

            {/* Version details */}
            <div className="flex-1 overflow-y-auto">
              <div className="p-6">
                {isLoading && (
                  <div className="space-y-4">
                    <div className="h-8 bg-gray-200 rounded animate-pulse" />
                    <div className="space-y-3">
                      {[...Array(3)].map((_, i) => (
                        <div key={i} className="h-20 bg-gray-200 rounded animate-pulse" />
                      ))}
                    </div>
                  </div>
                )}

                {error && (
                  <div className="text-center py-12">
                    <AlertCircle className="h-12 w-12 text-red-500 mx-auto mb-4" />
                    <h3 className="text-lg font-medium text-gray-900 mb-2">
                      Chyba načítání changelogu
                    </h3>
                    <p className="text-gray-500">{error}</p>
                  </div>
                )}

                {selectedVersionData && (
                  <>
                    {/* Version header */}
                    <div className="mb-6">
                      <div className="flex items-center space-x-3 mb-2">
                        <a 
                          href={getGitHubReleaseUrl(selectedVersionData.version)}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="inline-flex items-center space-x-2 text-2xl font-bold text-blue-600 hover:text-blue-800 hover:underline"
                        >
                          <span>Verze {selectedVersionData.version}</span>
                          <ExternalLink className="h-5 w-5" />
                        </a>
                        {selectedVersionData.version === data?.currentVersion && (
                          <span className="px-3 py-1 text-sm bg-green-100 text-green-800 rounded-full font-medium">
                            Aktuální verze
                          </span>
                        )}
                      </div>
                      <div className="flex items-center text-gray-500 text-sm">
                        <Calendar className="h-4 w-4 mr-1" />
                        {formatDate(selectedVersionData.date)} ({getDaysAgo(selectedVersionData.date)})
                      </div>
                      <div className="text-sm text-gray-500 mt-1">
                        {selectedVersionData.changes.length} změn v této verzi
                      </div>
                    </div>

                    {/* Changes */}
                    {selectedVersionData.changes.length > 0 ? (
                      <div className="space-y-4">
                        <h3 className="text-lg font-medium text-gray-900 mb-4">
                          Změny
                        </h3>
                        {selectedVersionData.changes.map((change, index) => (
                          <ChangelogEntry
                            key={`${change.source}-${change.hash || change.id}-${index}`}
                            entry={change}
                            showSource={true}
                            compact={false}
                          />
                        ))}
                      </div>
                    ) : (
                      <div className="text-center py-8 text-gray-500">
                        Žádné změny v této verzi
                      </div>
                    )}
                  </>
                )}

                {!selectedVersionData && !isLoading && !error && (
                  <div className="text-center py-12">
                    <Package className="h-12 w-12 text-gray-400 mx-auto mb-4" />
                    <h3 className="text-lg font-medium text-gray-900 mb-2">
                      Vyberte verzi
                    </h3>
                    <p className="text-gray-500">
                      Klikněte na verzi v levém panelu pro zobrazení změn
                    </p>
                  </div>
                )}
              </div>
            </div>
          </div>

          {/* Footer */}
          <div className="bg-gray-50 px-6 py-3 border-t border-gray-200">
            <div className="flex justify-between items-center">
              <div className="text-sm text-gray-500">
                Automaticky generováno z Git commitů a GitHub issues
              </div>
              <button
                onClick={onClose}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 transition-colors"
              >
                Zavřít
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ChangelogModal;