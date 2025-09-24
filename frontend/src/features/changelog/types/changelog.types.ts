/**
 * TypeScript interfaces for the Changelog system
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

/**
 * English types from changelog generation
 */
export type ChangeTypeEn = 
  | 'feature'
  | 'fix'
  | 'docs'
  | 'perf'
  | 'refactor'
  | 'test'
  | 'chore'
  | 'style'
  | 'ci'
  | 'build'
  | 'improvement'
  | 'security';

/**
 * Czech types for display (mapped from English)
 */
export type ChangeTypeCz = 
  | 'funkce'        // feature
  | 'oprava'        // fix/bug
  | 'dokumentace'   // docs
  | 'výkon'         // performance
  | 'refaktoring'   // refactor
  | 'testy'         // test
  | 'údržba'        // chore/maintenance
  | 'vylepšení'     // enhancement
  | 'funkcionalita' // functionality
  | 'bezpečnost'    // security
  | 'optimalizace'  // optimization
  | 'styl'          // style
  | 'sestavení';    // build/ci

/**
 * Types of changes that can be tracked in the changelog (raw from API)
 */
export type ChangeType = ChangeTypeEn;

/**
 * Source of the changelog entry
 */
export type ChangeSource = 'commit' | 'github-issue';

/**
 * Individual change entry in a version
 */
export interface ChangelogEntry {
  /** Type of change (translated to Czech) */
  type: ChangeType;
  
  /** Short title of the change */
  title: string;
  
  /** Detailed description of the change */
  description: string;
  
  /** Source where this change came from */
  source: ChangeSource;
  
  /** Git commit hash (for commits) */
  hash?: string;
  
  /** GitHub issue reference (for issues) */
  id?: string;
}

/**
 * Version information with all changes
 */
export interface ChangelogVersion {
  /** Semantic version string (e.g., "1.2.0") */
  version: string;
  
  /** Release date in YYYY-MM-DD format */
  date: string;
  
  /** List of changes in this version */
  changes: ChangelogEntry[];
}

/**
 * Complete changelog data structure
 */
export interface ChangelogData {
  /** Current deployed version */
  currentVersion: string;
  
  /** List of versions (current + previous 5) */
  versions: ChangelogVersion[];
}

/**
 * Version tracking for localStorage
 */
export interface VersionTracking {
  /** Last version shown to user */
  lastShownVersion: string;
  
  /** Timestamp when last shown */
  lastShownAt: string;
  
  /** Array of all versions user has seen */
  seenVersions: string[];
}

/**
 * Toaster notification state
 */
export interface ToasterState {
  /** Whether toaster is currently visible */
  isVisible: boolean;
  
  /** Current version being shown */
  version?: string;
  
  /** Changes for current version */
  changes?: ChangelogEntry[];
  
  /** Whether toaster is in auto-hide countdown */
  isAutoHiding: boolean;
}

/**
 * Modal state for changelog history
 */
export interface ModalState {
  /** Whether modal is open */
  isOpen: boolean;
  
  /** Complete changelog data */
  data?: ChangelogData;
  
  /** Loading state */
  isLoading: boolean;
  
  /** Error state */
  error?: string;
}

/**
 * Hook return types
 */
export interface UseChangelogReturn {
  /** Changelog data */
  data: ChangelogData | null;
  
  /** Loading state */
  isLoading: boolean;
  
  /** Error message if any */
  error: string | null;
  
  /** Refresh changelog data */
  refetch: () => Promise<void>;
}

export interface UseChangelogToasterReturn {
  /** Toaster state */
  toaster: ToasterState;
  
  /** Show toaster for new version */
  showToaster: (version: string, changes: ChangelogEntry[]) => void;
  
  /** Hide toaster manually */
  hideToaster: () => void;
  
  /** Check if version is new for user */
  isNewVersion: (version: string) => boolean;
  
  /** Mark version as seen */
  markVersionAsSeen: (version: string) => void;
}

/**
 * Component props
 */
export interface ChangelogToasterProps {
  /** Override auto-show behavior */
  manualControl?: boolean;
  
  /** Custom positioning class */
  positionClass?: string;
  
  /** Custom auto-hide timeout (ms) */
  autoHideTimeout?: number;
}

export interface ChangelogModalProps {
  /** Whether modal is open */
  isOpen: boolean;
  
  /** Function to close modal */
  onClose: () => void;
  
  /** Optional custom title */
  title?: string;
}

export interface ChangelogEntryProps {
  /** The changelog entry to display */
  entry: ChangelogEntry;
  
  /** Show source information */
  showSource?: boolean;
  
  /** Compact display mode */
  compact?: boolean;
}

/**
 * Utility function types
 */
export interface VersionCompareResult {
  /** Whether first version is newer than second */
  isNewer: boolean;
  
  /** Semantic comparison result (-1, 0, 1) */
  comparison: number;
}

/**
 * Translation mapping type for script
 */
export interface TranslationMappings {
  [key: string]: string;
}

/**
 * Generation script configuration
 */
export interface ChangelogConfig {
  /** Maximum number of versions to include */
  maxVersions: number;
  
  /** GitHub repository information */
  repository: {
    owner: string;
    name: string;
  };
  
  /** Translation mappings */
  translations: TranslationMappings;
  
  /** Exclude patterns for commits */
  excludePatterns: string[];
  
  /** Include labels for GitHub issues */
  includeLabels: string[];
  
  /** Exclude labels for GitHub issues */
  excludeLabels: string[];
}

/**
 * Error types for changelog system
 */
export class ChangelogError extends Error {
  constructor(
    message: string,
    public code: string,
    public cause?: Error
  ) {
    super(message);
    this.name = 'ChangelogError';
  }
}

export class VersionTrackingError extends ChangelogError {
  constructor(message: string, cause?: Error) {
    super(message, 'VERSION_TRACKING_ERROR', cause);
  }
}

export class ChangelogFetchError extends ChangelogError {
  constructor(message: string, cause?: Error) {
    super(message, 'CHANGELOG_FETCH_ERROR', cause);
  }
}

export class ChangelogParseError extends ChangelogError {
  constructor(message: string, cause?: Error) {
    super(message, 'CHANGELOG_PARSE_ERROR', cause);
  }
}

/**
 * Mapping from English changelog types to Czech display types
 */
export const CHANGE_TYPE_MAPPING: Record<ChangeTypeEn, ChangeTypeCz> = {
  feature: 'funkce',
  fix: 'oprava',
  docs: 'dokumentace',
  perf: 'výkon',
  refactor: 'refaktoring',
  test: 'testy',
  chore: 'údržba',
  style: 'styl',
  ci: 'sestavení',
  build: 'sestavení',
  improvement: 'vylepšení',
  security: 'bezpečnost',
};

/**
 * Convert English changelog type to Czech display type
 */
export function mapChangeTypeToCzech(englishType: ChangeTypeEn): ChangeTypeCz {
  return CHANGE_TYPE_MAPPING[englishType] || 'funkce';
}