import React, { useEffect, useState, useRef, useMemo } from 'react';
import { useOrgChart } from '../api/hooks/useOrgChart';
import { PositionCard } from '../components/OrgChart/PositionCard';
import { useScreenView } from '../telemetry/useScreenView';
import {
  calculateLevels,
  getAllParentPositionIds,
  buildTree,
  getChildren as orgChartGetChildren,
  Position,
  OrganizationData,
} from './orgChartUtils';

interface PositionRect {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

const OrgChartPage: React.FC = () => {
  useScreenView('Admin', 'OrgChart');

  // Fetch organization data from backend
  const { data: orgChartResponse, isLoading, error: queryError } = useOrgChart();

  const [filters, setFilters] = useState({
    department: 'all',
    level: 'all',
  });
  const [positionRects, setPositionRects] = useState<PositionRect[]>([]);
  const [zoom, setZoom] = useState(1);
  const containerRef = useRef<HTMLDivElement>(null);

  // Transform backend response to local format and calculate levels
  const orgData = useMemo(() => {
    if (!orgChartResponse) return null;

    const transformedData: OrganizationData = {
      organization: {
        name: orgChartResponse.organization?.name || '',
        positions: orgChartResponse.organization?.positions || [],
      },
    };

    return calculateLevels(transformedData);
  }, [orgChartResponse]);

  // Helper function to get element position relative to scaled container
  const getElementPosition = (element: HTMLElement): { x: number; y: number } => {
    let x = 0;
    let y = 0;
    let currentElement: HTMLElement | null = element;

    // Walk up the DOM tree and accumulate offsets until we reach the scaled container
    while (currentElement && currentElement !== containerRef.current) {
      x += currentElement.offsetLeft;
      y += currentElement.offsetTop;
      currentElement = currentElement.offsetParent as HTMLElement | null;
    }

    return { x, y };
  };

  // Calculate position rectangles after render
  useEffect(() => {
    if (!containerRef.current) return;

    // Use requestAnimationFrame to ensure DOM is updated after zoom change
    requestAnimationFrame(() => {
      if (!containerRef.current) return;

      const rects: PositionRect[] = [];
      const cards = containerRef.current.querySelectorAll('[data-position-id]');

      cards.forEach((card) => {
        const positionId = card.getAttribute('data-position-id');
        if (!positionId) return;

        const element = card as HTMLElement;

        // Get position in pre-transform coordinates (before zoom)
        const pos = getElementPosition(element);
        const x = pos.x + element.offsetWidth / 2;
        const y = pos.y;
        const width = element.offsetWidth;
        const height = element.offsetHeight;

        rects.push({
          id: positionId,
          x,
          y,
          width,
          height,
        });
      });

      setPositionRects(rects);
    });
  }, [orgData, filters, zoom]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-screen">
        <div className="text-xl text-indigo-600">Načítám organizační strukturu...</div>
      </div>
    );
  }

  if (queryError) {
    return (
      <div className="flex items-center justify-center h-screen">
        <div className="text-xl text-red-600">❌ Nepodařilo se načíst data: {(queryError as Error).message}</div>
      </div>
    );
  }

  if (!orgData) {
    return (
      <div className="flex items-center justify-center h-screen">
        <div className="text-xl text-indigo-600">Načítám organizační strukturu...</div>
      </div>
    );
  }

  const departments = Array.from(new Set(orgData.organization.positions.map((p) => p.department)));

  // Filter positions based on department and level
  const filteredPositions = (() => {
    const allPositions = orgData.organization.positions;

    // First, find positions that match the department filter
    let matchingPositions = allPositions;

    if (filters.department !== 'all') {
      // Find all positions in the selected department
      const departmentPositions = allPositions.filter(pos => pos.department === filters.department);

      // Collect all parent position IDs for these department positions
      const parentPositionIds = new Set<string>();
      departmentPositions.forEach(pos => {
        const parents = getAllParentPositionIds(pos.id!, allPositions);
        parents.forEach(id => parentPositionIds.add(id));
      });

      // Include department positions + all their parents
      matchingPositions = allPositions.filter(pos =>
        pos.department === filters.department || parentPositionIds.has(pos.id!)
      );
    }

    // Apply level filter (show selected level and all parent levels)
    if (filters.level !== 'all') {
      matchingPositions = matchingPositions.filter(pos =>
        !pos.level || pos.level <= parseInt(filters.level)
      );
    }

    return matchingPositions;
  })();

  const totalEmployees = filteredPositions.reduce((sum, pos) => sum + (pos.employees?.length || 0), 0);

  const getLevelColor = (level: number) => {
    switch (level) {
      case 1:
        return 'border-l-4 border-red-500';
      case 2:
        return 'border-l-4 border-orange-500';
      case 3:
        return 'border-l-4 border-yellow-500';
      case 4:
        return 'border-l-4 border-green-500';
      default:
        return 'border-l-4 border-gray-500';
    }
  };

  const getChildren = (parentId: string): Position[] =>
    orgChartGetChildren(parentId, filteredPositions);

  // Draw connection lines
  const renderConnections = () => {
    if (positionRects.length === 0) return null;

    const lines: JSX.Element[] = [];
    const filteredPositionIds = new Set(filteredPositions.map((p) => p.id));

    filteredPositions.forEach((position) => {
      if (!position.parentPositionId) return;

      // Only draw connection if parent is also in filtered positions
      if (!filteredPositionIds.has(position.parentPositionId)) return;

      const childRect = positionRects.find((r) => r.id === position.id);
      const parentRect = positionRects.find((r) => r.id === position.parentPositionId);

      if (!childRect || !parentRect) return;

      // Calculate connection points with spacing
      const verticalSpacing = 20; // Add 20px spacing from parent card
      const parentX = parentRect.x;
      const parentY = parentRect.y + parentRect.height + verticalSpacing;
      const childX = childRect.x;
      const childY = childRect.y - 10; // 10px before child card

      // Create path with vertical drop and horizontal connection
      const midY = parentY + (childY - parentY) / 2;

      const pathData = `M ${parentX} ${parentY} L ${parentX} ${midY} L ${childX} ${midY} L ${childX} ${childY}`;

      lines.push(
        <path
          key={`${position.parentPositionId}-${position.id}`}
          d={pathData}
          stroke="#94a3b8"
          strokeWidth="2"
          fill="none"
          strokeDasharray="0"
          className="transition-all"
        />
      );
    });

    return lines;
  };

  return (
    <div className="h-screen bg-gradient-to-br from-indigo-500 to-purple-600 flex flex-col">
      {/* Controls - Fixed */}
      <div className="bg-white border-b border-gray-200 p-4 flex flex-wrap gap-4 items-center shadow-sm">
        <div className="flex items-center gap-2">
          <label className="font-semibold text-gray-700">Oddělení:</label>
          <select
            value={filters.department}
            onChange={(e) => setFilters({ ...filters, department: e.target.value })}
            className="px-3 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-indigo-500 text-sm"
          >
            <option value="all">Všechna oddělení</option>
            {departments.sort().map((dept) => (
              <option key={dept} value={dept}>
                {dept}
              </option>
            ))}
          </select>
        </div>

        <div className="flex items-center gap-2">
          <label className="font-semibold text-gray-700">Úroveň:</label>
          <select
            value={filters.level}
            onChange={(e) => setFilters({ ...filters, level: e.target.value })}
            className="px-3 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-indigo-500 text-sm"
          >
            <option value="all">Všechny úrovně</option>
            <option value="1">Až po úroveň 1 (Vedení)</option>
            <option value="2">Až po úroveň 2 (Ředitelé)</option>
            <option value="3">Až po úroveň 3 (Vedoucí)</option>
            <option value="4">Až po úroveň 4 (Pracovníci)</option>
          </select>
        </div>

        <button
          onClick={() => setFilters({ department: 'all', level: 'all' })}
          className="px-4 py-2 bg-indigo-600 text-white font-semibold rounded-lg hover:bg-indigo-700 transition-all hover:shadow-lg text-sm"
        >
          Resetovat filtry
        </button>

        <div className="flex items-center gap-2 ml-4">
          <label className="font-semibold text-gray-700">Zoom:</label>
          <button
            onClick={() => setZoom(Math.max(0.5, zoom - 0.1))}
            className="px-3 py-2 bg-gray-200 text-gray-700 font-bold rounded-lg hover:bg-gray-300 transition-all text-sm"
            disabled={zoom <= 0.5}
          >
            −
          </button>
          <span className="text-sm font-semibold text-gray-700 min-w-[50px] text-center">
            {Math.round(zoom * 100)}%
          </span>
          <button
            onClick={() => setZoom(Math.min(2, zoom + 0.1))}
            className="px-3 py-2 bg-gray-200 text-gray-700 font-bold rounded-lg hover:bg-gray-300 transition-all text-sm"
            disabled={zoom >= 2}
          >
            +
          </button>
          <button
            onClick={() => setZoom(1)}
            className="px-3 py-2 bg-gray-100 text-gray-600 text-xs rounded-lg hover:bg-gray-200 transition-all"
          >
            Reset
          </button>
        </div>

        <div className="ml-auto flex gap-4">
          <div className="text-center px-3 py-1.5 bg-gray-50 border border-gray-200 rounded-lg">
            <div className="text-xs text-gray-500 uppercase tracking-wide">Pozice</div>
            <div className="text-xl font-bold text-indigo-600">{filteredPositions.length}</div>
          </div>
          <div className="text-center px-3 py-1.5 bg-gray-50 border border-gray-200 rounded-lg">
            <div className="text-xs text-gray-500 uppercase tracking-wide">Zaměstnanci</div>
            <div className="text-xl font-bold text-indigo-600">{totalEmployees}</div>
          </div>
        </div>
      </div>

      {/* Orgchart - Scrollable with wide layout */}
      <div className="flex-1 overflow-auto bg-gradient-to-b from-gray-50 to-white mb-8">
        <div
          className="p-10 min-w-max relative origin-top-left transition-transform duration-200"
          ref={containerRef}
          style={{ transform: `scale(${zoom})` }}
        >
          {/* SVG for connection lines */}
          <svg className="absolute top-0 left-0 w-full h-full pointer-events-none" style={{ zIndex: 0 }}>
            {renderConnections()}
          </svg>

          {/* Position cards - Hierarchical Tree Layout */}
          <div className="relative" style={{ zIndex: 1 }}>
            <div className="flex justify-center gap-12">
              {buildTree(filteredPositions).map((root) => (
                <PositionCard
                  key={root.id}
                  position={root}
                  getChildren={getChildren}
                  getLevelColor={getLevelColor}
                />
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default OrgChartPage;
