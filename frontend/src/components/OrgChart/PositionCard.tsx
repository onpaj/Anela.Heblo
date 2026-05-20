import React from 'react';
import { PositionDto } from '../../api/generated/api-client';

export interface PositionCardProps {
  position: PositionDto;
  getChildren: (parentId: string) => PositionDto[];
  getLevelColor: (level: number) => string;
}

const getInitials = (name: string | undefined): string => {
  if (!name) return '?';
  return name
    .split(' ')
    .map((n) => n[0])
    .join('')
    .toUpperCase();
};

function PositionCard({ position, getChildren, getLevelColor }: PositionCardProps): JSX.Element {
  const children = getChildren(position.id!);

  return (
    <div className="flex flex-col items-center">
      {/* Position card content */}
      <div
        data-position-id={position.id}
        className={`bg-white rounded-xl shadow-lg p-6 w-80 transition-all hover:shadow-2xl hover:-translate-y-1 ${getLevelColor(
          position.level ?? 1
        )} relative mb-20`}
      >
        {(position.employees?.length || 0) > 1 && (
          <div className="absolute top-3 right-3 bg-indigo-600 text-white w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold">
            {position.employees?.length}
          </div>
        )}

        <div className="inline-block bg-blue-100 text-blue-700 px-3 py-1 rounded-full text-xs font-semibold mb-3">
          {position.department}
        </div>

        {position.url ? (
          <a
            href={position.url}
            target="_blank"
            rel="noopener noreferrer"
            className="text-lg font-bold text-gray-900 mb-2 hover:text-indigo-600 transition-colors flex items-center gap-1 cursor-pointer"
          >
            {position.title}
            <svg
              xmlns="http://www.w3.org/2000/svg"
              className="h-4 w-4"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"
              />
            </svg>
          </a>
        ) : (
          <h3 className="text-lg font-bold text-gray-900 mb-2">{position.title}</h3>
        )}
        <p className="text-sm text-gray-600 mb-4 leading-relaxed">{position.description}</p>

        <div className="border-t border-gray-200 pt-4 space-y-2">
          {(position.employees || []).map((emp) => (
            <div key={emp.id} className="flex items-center gap-3 p-2 rounded-lg hover:bg-gray-50 transition-colors">
              <div
                className={`w-9 h-9 rounded-full flex items-center justify-center text-white font-bold text-sm flex-shrink-0 ${
                  emp.isPrimary
                    ? 'bg-gradient-to-br from-pink-400 to-red-500 shadow-md'
                    : 'bg-gradient-to-br from-indigo-500 to-purple-600'
                }`}
              >
                {getInitials(emp.name)}
              </div>
              <div className="flex-1 min-w-0">
                {emp.url ? (
                  <a
                    href={emp.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm font-semibold text-gray-900 truncate hover:text-indigo-600 transition-colors flex items-center gap-1 cursor-pointer"
                  >
                    {emp.name}
                    <svg
                      xmlns="http://www.w3.org/2000/svg"
                      className="h-3 w-3 flex-shrink-0"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"
                      />
                    </svg>
                  </a>
                ) : (
                  <div className="text-sm font-semibold text-gray-900 truncate">{emp.name}</div>
                )}
                <div className="text-xs text-gray-500 truncate">{emp.email}</div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Children */}
      {children.length > 0 && (
        <div className="flex justify-center gap-12">
          {children.map((child) => (
            <PositionCard
              key={child.id}
              position={child}
              getChildren={getChildren}
              getLevelColor={getLevelColor}
            />
          ))}
        </div>
      )}
    </div>
  );
}

export { PositionCard };
export default PositionCard;
