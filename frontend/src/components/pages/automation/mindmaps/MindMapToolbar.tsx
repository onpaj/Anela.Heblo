import React from "react";

// Floating toolbar over the canvas, mirroring the template's control bar.
const BUTTON_CLASS =
  "px-3 py-1.5 text-xs font-medium rounded-lg border border-gray-300 bg-white/90 backdrop-blur-sm text-gray-700 hover:bg-white hover:border-gray-400 disabled:opacity-40 disabled:cursor-not-allowed dark:border-graphite-border dark:bg-graphite-surface/90 dark:text-graphite-muted dark:hover:bg-graphite-surface";

export interface MindMapToolbarProps {
  isReadOnly: boolean;
  hasSelection: boolean;
  onExpandAll: () => void;
  onCollapseAll: () => void;
  onFit: () => void;
  onAddSibling: () => void;
  onAddChild: () => void;
  onUndo: () => void;
  onOpenHelp: () => void;
  onExportPng: () => void;
  onExportSvg: () => void;
}

const Separator: React.FC = () => (
  <span className="mx-0.5 h-5 w-px bg-gray-300 dark:bg-graphite-border" aria-hidden="true" />
);

const MindMapToolbar: React.FC<MindMapToolbarProps> = ({
  isReadOnly,
  hasSelection,
  onExpandAll,
  onCollapseAll,
  onFit,
  onAddSibling,
  onAddChild,
  onUndo,
  onOpenHelp,
  onExportPng,
  onExportSvg,
}) => (
  <div
    data-testid="mindmap-toolbar"
    className="absolute left-3 top-3 z-10 flex flex-wrap items-center gap-1.5"
  >
    <button type="button" className={BUTTON_CLASS} onClick={onExpandAll} disabled={isReadOnly}>
      Rozbalit
    </button>
    <button type="button" className={BUTTON_CLASS} onClick={onCollapseAll} disabled={isReadOnly}>
      Sbalit
    </button>
    <button type="button" data-testid="mindmap-fit-button" className={BUTTON_CLASS} onClick={onFit}>
      Vycentrovat
    </button>

    <Separator />

    <button
      type="button"
      data-testid="mindmap-add-sibling"
      className={BUTTON_CLASS}
      onClick={onAddSibling}
      disabled={isReadOnly || !hasSelection}
      title="Nový uzel vedle vybraného (Enter)"
    >
      + vedle
    </button>
    <button
      type="button"
      data-testid="mindmap-add-child"
      className={BUTTON_CLASS}
      onClick={onAddChild}
      disabled={isReadOnly || !hasSelection}
      title="Nový uzel pod vybraný (Tab)"
    >
      + pod
    </button>

    <Separator />

    <button
      type="button"
      data-testid="mindmap-undo"
      className={BUTTON_CLASS}
      onClick={onUndo}
      disabled={isReadOnly}
      title="Zpět (⌘Z)"
    >
      Zpět
    </button>

    <Separator />

    <button
      type="button"
      data-testid="mindmap-export-png"
      className={BUTTON_CLASS}
      onClick={onExportPng}
      title="Stáhnout mapu jako PNG"
    >
      PNG
    </button>
    <button
      type="button"
      data-testid="mindmap-export-svg"
      className={BUTTON_CLASS}
      onClick={onExportSvg}
      title="Stáhnout mapu jako SVG"
    >
      SVG
    </button>
    <button type="button" className={BUTTON_CLASS} onClick={onOpenHelp} title="Klávesové zkratky">
      ?
    </button>
  </div>
);

export default MindMapToolbar;
