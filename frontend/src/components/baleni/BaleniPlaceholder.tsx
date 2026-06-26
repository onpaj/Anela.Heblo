import React from 'react';
import { Wrench } from 'lucide-react';

interface BaleniPlaceholderProps {
  title: string;
}

const BaleniPlaceholder: React.FC<BaleniPlaceholderProps> = ({ title }) => (
  <div
    className="flex flex-col items-center justify-center py-20 text-center"
    data-testid="baleni-placeholder"
  >
    <div className="w-16 h-16 bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded-full flex items-center justify-center mb-4">
      <Wrench className="h-8 w-8 text-primary-blue dark:text-graphite-accent" />
    </div>
    <h2 className="text-xl font-bold text-neutral-slate dark:text-graphite-text mb-2">{title}</h2>
    <p className="text-sm text-neutral-gray dark:text-graphite-muted">Brzy k dispozici</p>
  </div>
);

export default BaleniPlaceholder;
