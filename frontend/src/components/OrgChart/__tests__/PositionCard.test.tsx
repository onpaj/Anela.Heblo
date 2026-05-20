import React from 'react';
import { render } from '@testing-library/react';
import '@testing-library/jest-dom';
import { PositionCard } from '../PositionCard';
import { PositionDto, EmployeeDto } from '../../../api/generated/api-client';

const makePosition = (overrides: Partial<PositionDto>): PositionDto => {
  const p = new PositionDto();
  Object.assign(p, overrides);
  return p;
};

const makeEmployee = (overrides: Partial<EmployeeDto>): EmployeeDto => {
  const e = new EmployeeDto();
  Object.assign(e, overrides);
  return e;
};

const noChildren = (_parentId: string): PositionDto[] => [];

const stubLevelColor = (level: number): string => `border-l-4 level-${level}`;

describe('PositionCard', () => {
  it('renders a leaf position with data-position-id on the outer card', () => {
    // Arrange
    const position = makePosition({
      id: 'pos-1',
      title: 'CEO',
      department: 'Executive',
      description: 'Top of the org',
      level: 1,
      employees: [makeEmployee({ id: 'e1', name: 'Alice Anderson', email: 'a@example.com' })],
    });

    // Act
    const { container } = render(
      <PositionCard
        position={position}
        getChildren={noChildren}
        getLevelColor={stubLevelColor}
      />,
    );

    // Assert — the geometry useEffect in OrgChartPage queries this attribute.
    expect(container.querySelector('[data-position-id="pos-1"]')).not.toBeNull();
    expect(container).toMatchSnapshot();
  });

  it('renders a recursive position with one child', () => {
    // Arrange
    const parent = makePosition({
      id: 'parent',
      title: 'Manager',
      department: 'Sales',
      description: 'Manages the team',
      level: 2,
      employees: [],
    });
    const child = makePosition({
      id: 'child',
      title: 'Rep',
      department: 'Sales',
      description: 'Sells things',
      level: 3,
      parentPositionId: 'parent',
    });

    const getChildren = (parentId: string): PositionDto[] =>
      parentId === 'parent' ? [child] : [];

    // Act
    const { container } = render(
      <PositionCard
        position={parent}
        getChildren={getChildren}
        getLevelColor={stubLevelColor}
      />,
    );

    // Assert
    expect(container.querySelector('[data-position-id="parent"]')).not.toBeNull();
    expect(container.querySelector('[data-position-id="child"]')).not.toBeNull();
    // Verify child is rendered within parent's outer wrapper (flex container)
    const parentCard = container.querySelector('[data-position-id="parent"]');
    const parentWrapper = parentCard?.parentElement;
    const childCard = container.querySelector('[data-position-id="child"]');
    expect(parentWrapper?.contains(childCard)).toBe(true);
    expect(container).toMatchSnapshot();
  });
});
