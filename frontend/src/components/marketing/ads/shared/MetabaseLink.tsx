import React from 'react';
import { ExternalLink } from 'lucide-react';
import { METABASE_ADS_DASHBOARD_URL } from './metabase';

const MetabaseLink: React.FC = () => (
  <a
    href={METABASE_ADS_DASHBOARD_URL}
    target="_blank"
    rel="noopener noreferrer"
    className="inline-flex items-center gap-1 text-sm text-indigo-600 dark:text-graphite-accent hover:underline"
  >
    <ExternalLink className="h-4 w-4" aria-hidden="true" />
    Výkon kampaní v Metabase
  </a>
);

export default MetabaseLink;
