/**
 * Main export barrel for changelog feature
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

// Components
export { 
  ChangelogToaster, 
  ChangelogModal,
  ChangelogModalContainer 
} from './components';

// Hooks
export * from './hooks';

// Types (explicitly export to avoid naming conflicts)
export type { 
  ChangeType,
  ChangeSource,
  ChangelogEntry as ChangelogEntryType,
  ChangelogVersion,
  ChangelogData,
  VersionTracking,
  ToasterState,
  ModalState,
  UseChangelogReturn,
  UseChangelogToasterReturn,
  ChangelogToasterProps,
  ChangelogModalProps,
  ChangelogEntryProps
} from './types';

// Utils
export * from './utils/version-tracking';