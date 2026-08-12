import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import MindMapToolbar from "../MindMapToolbar";

const handlers = () => ({
  onExpandAll: jest.fn(),
  onCollapseAll: jest.fn(),
  onFit: jest.fn(),
  onAddSibling: jest.fn(),
  onAddChild: jest.fn(),
  onUndo: jest.fn(),
  onOpenHelp: jest.fn(),
  onExportPng: jest.fn(),
  onExportSvg: jest.fn(),
});

function renderToolbar(overrides: Partial<React.ComponentProps<typeof MindMapToolbar>> = {}) {
  const props = { isReadOnly: false, hasSelection: true, ...handlers(), ...overrides };
  render(<MindMapToolbar {...props} />);
  return props;
}

it("wires each toolbar action to its handler", () => {
  const utils = renderToolbar();
  fireEvent.click(screen.getByText("Rozbalit"));
  fireEvent.click(screen.getByText("Sbalit"));
  fireEvent.click(screen.getByTestId("mindmap-fit-button"));
  fireEvent.click(screen.getByTestId("mindmap-undo"));
  expect(utils.onExpandAll).toHaveBeenCalled();
  expect(utils.onCollapseAll).toHaveBeenCalled();
  expect(utils.onFit).toHaveBeenCalled();
  expect(utils.onUndo).toHaveBeenCalled();
});

it("offers PNG and SVG export", () => {
  const utils = renderToolbar();
  fireEvent.click(screen.getByTestId("mindmap-export-png"));
  fireEvent.click(screen.getByTestId("mindmap-export-svg"));
  expect(utils.onExportPng).toHaveBeenCalled();
  expect(utils.onExportSvg).toHaveBeenCalled();
});

it("keeps export available on a read-only map but disables the editing actions", () => {
  renderToolbar({ isReadOnly: true });
  expect(screen.getByTestId("mindmap-export-png")).toBeEnabled();
  expect(screen.getByTestId("mindmap-add-child")).toBeDisabled();
  expect(screen.getByTestId("mindmap-undo")).toBeDisabled();
});

it("disables the add actions when nothing is selected", () => {
  renderToolbar({ hasSelection: false });
  expect(screen.getByTestId("mindmap-add-sibling")).toBeDisabled();
  expect(screen.getByTestId("mindmap-add-child")).toBeDisabled();
});
