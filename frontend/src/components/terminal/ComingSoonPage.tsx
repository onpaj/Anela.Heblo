import React from 'react';
import { Wrench } from 'lucide-react';

interface ComingSoonPageProps {
  title: string;
}

const ComingSoonPage: React.FC<ComingSoonPageProps> = ({ title }) => (
  <div
    className="flex flex-col items-center justify-center py-20 text-center"
    data-testid="coming-soon-page"
  >
    <div className="w-16 h-16 bg-secondary-blue-pale rounded-full flex items-center justify-center mb-4">
      <Wrench className="h-8 w-8 text-primary-blue" />
    </div>
    <h2 className="text-xl font-bold text-neutral-slate mb-2">{title}</h2>
    <p className="text-sm text-neutral-gray">Tato funkce bude brzy k dispozici.</p>
  </div>
);

export default ComingSoonPage;
