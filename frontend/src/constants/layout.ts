// Layout constants for consistent spacing and measurements

// Status bar height (24px for h-6) plus extra margin for proper scrolling clearance
export const STATUS_BAR_OFFSET = 38; // px

// Page container height calculation accounting for status bar
export const PAGE_CONTAINER_HEIGHT = `calc(100vh - ${STATUS_BAR_OFFSET}px)`;