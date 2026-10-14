import React from 'react';

interface TagChipsProps {
  tags: string[];
}

function TagChips({ tags }: TagChipsProps) {
  if (tags.length === 0) return null;

  return (
    <div className="flex flex-wrap gap-1">
      {tags.map((tag) => (
        <span
          key={tag}
          className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-50 text-blue-700 dark:bg-graphite-surface-2 dark:text-graphite-text"
        >
          {tag}
        </span>
      ))}
    </div>
  );
}

export default TagChips;
