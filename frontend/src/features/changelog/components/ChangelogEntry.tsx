/**
 * Single changelog entry component
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import React from 'react';
import { 
  CheckCircle, 
  AlertCircle, 
  Package, 
  Zap, 
  RefreshCw, 
  FileText, 
  Shield,
  Wrench,
  Sparkles,
  GitCommit,
  Github,
  ExternalLink
} from 'lucide-react';
import { ChangelogEntry as ChangelogEntryType, ChangelogEntryProps } from '../types';

/**
 * Get icon component for change type
 */
function getChangeTypeIcon(type: ChangelogEntryType['type']): React.ElementType {
  switch (type) {
    case 'funkce':
    case 'funkcionalita':
      return Package;
    case 'oprava':
      return CheckCircle;
    case 'dokumentace':
      return FileText;
    case 'výkon':
    case 'optimalizace':
      return Zap;
    case 'refaktoring':
      return RefreshCw;
    case 'vylepšení':
      return Sparkles;
    case 'bezpečnost':
      return Shield;
    case 'údržba':
      return Wrench;
    default:
      return AlertCircle;
  }
}

/**
 * Get color classes for change type
 */
function getChangeTypeColors(type: ChangelogEntryType['type']): {
  icon: string;
  badge: string;
  border: string;
} {
  switch (type) {
    case 'funkce':
    case 'funkcionalita':
      return {
        icon: 'text-blue-600',
        badge: 'bg-blue-100 text-blue-800',
        border: 'border-blue-200',
      };
    case 'oprava':
      return {
        icon: 'text-green-600',
        badge: 'bg-green-100 text-green-800',
        border: 'border-green-200',
      };
    case 'vylepšení':
      return {
        icon: 'text-purple-600',
        badge: 'bg-purple-100 text-purple-800',
        border: 'border-purple-200',
      };
    case 'výkon':
    case 'optimalizace':
      return {
        icon: 'text-yellow-600',
        badge: 'bg-yellow-100 text-yellow-800',
        border: 'border-yellow-200',
      };
    case 'bezpečnost':
      return {
        icon: 'text-red-600',
        badge: 'bg-red-100 text-red-800',
        border: 'border-red-200',
      };
    case 'refaktoring':
      return {
        icon: 'text-indigo-600',
        badge: 'bg-indigo-100 text-indigo-800',
        border: 'border-indigo-200',
      };
    case 'dokumentace':
      return {
        icon: 'text-gray-600',
        badge: 'bg-gray-100 text-gray-800',
        border: 'border-gray-200',
      };
    default:
      return {
        icon: 'text-gray-600',
        badge: 'bg-gray-100 text-gray-800',
        border: 'border-gray-200',
      };
  }
}

/**
 * Generate GitHub issue URL
 */
function getGitHubIssueUrl(issueId: string): string {
  const issueNumber = issueId.replace('#', '');
  return `https://github.com/onpaj/Anela.Heblo/issues/${issueNumber}`;
}

/**
 * Generate GitHub commit URL
 */
function getGitHubCommitUrl(commitHash: string): string {
  return `https://github.com/onpaj/Anela.Heblo/commit/${commitHash}`;
}

/**
 * Changelog entry component
 */
const ChangelogEntry: React.FC<ChangelogEntryProps> = ({
  entry,
  showSource = false,
  compact = false,
}) => {
  const IconComponent = getChangeTypeIcon(entry.type);
  const colors = getChangeTypeColors(entry.type);

  if (compact) {
    return (
      <div className="flex items-center space-x-2 py-1">
        <IconComponent className={`h-3 w-3 flex-shrink-0 ${colors.icon}`} />
        <span className="text-sm text-gray-900 truncate">{entry.title}</span>
        {showSource && (
          <span className="text-xs text-gray-500 flex-shrink-0">
            {entry.source === 'github-issue' && entry.id ? (
              <a 
                href={getGitHubIssueUrl(entry.id)} 
                target="_blank" 
                rel="noopener noreferrer"
                className="inline-flex items-center space-x-1 text-blue-600 hover:text-blue-800 hover:underline"
              >
                <Github className="h-3 w-3" />
                <span>{entry.id}</span>
                <ExternalLink className="h-2 w-2" />
              </a>
            ) : entry.hash ? (
              <a 
                href={getGitHubCommitUrl(entry.hash)} 
                target="_blank" 
                rel="noopener noreferrer"
                className="inline-flex items-center space-x-1 text-blue-600 hover:text-blue-800 hover:underline"
              >
                <GitCommit className="h-3 w-3" />
                <span>{entry.hash.substring(0, 7)}</span>
                <ExternalLink className="h-2 w-2" />
              </a>
            ) : null}
          </span>
        )}
      </div>
    );
  }

  return (
    <div className={`bg-white border rounded-lg p-4 ${colors.border}`}>
      <div className="flex items-start space-x-3">
        {/* Icon */}
        <div className="flex-shrink-0">
          <IconComponent className={`h-5 w-5 ${colors.icon}`} />
        </div>

        {/* Content */}
        <div className="flex-1 min-w-0">
          {/* Header with type badge */}
          <div className="flex items-center space-x-2 mb-2">
            <span className={`inline-flex px-2 py-1 text-xs font-medium rounded-full ${colors.badge}`}>
              {entry.type}
            </span>
            {showSource && (
              <div className="flex items-center space-x-1 text-xs text-gray-500">
                {entry.source === 'github-issue' && entry.id ? (
                  <a 
                    href={getGitHubIssueUrl(entry.id)} 
                    target="_blank" 
                    rel="noopener noreferrer"
                    className="inline-flex items-center space-x-1 text-blue-600 hover:text-blue-800 hover:underline"
                  >
                    <Github className="h-3 w-3" />
                    <span>{entry.id}</span>
                    <ExternalLink className="h-2 w-2" />
                  </a>
                ) : entry.hash ? (
                  <a 
                    href={getGitHubCommitUrl(entry.hash)} 
                    target="_blank" 
                    rel="noopener noreferrer"
                    className="inline-flex items-center space-x-1 text-blue-600 hover:text-blue-800 hover:underline"
                  >
                    <GitCommit className="h-3 w-3" />
                    <span>{entry.hash.substring(0, 7)}</span>
                    <ExternalLink className="h-2 w-2" />
                  </a>
                ) : null}
              </div>
            )}
          </div>

          {/* Title */}
          <h4 className="text-sm font-medium text-gray-900 mb-1">
            {entry.title}
          </h4>

          {/* Description (if different from title) */}
          {entry.description && entry.description !== entry.title && (
            <p className="text-sm text-gray-600">
              {entry.description}
            </p>
          )}
        </div>
      </div>
    </div>
  );
};

export default ChangelogEntry;