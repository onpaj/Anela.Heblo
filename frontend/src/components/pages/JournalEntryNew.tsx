import React from "react";
import JournalEntryForm from "../JournalEntryForm";
import { useScreenView } from '../../telemetry/useScreenView';

export default function JournalEntryNew() {
  useScreenView('Journal', 'JournalEntryNew');

  return (
    <div className="min-h-screen bg-gray-50">
      <div className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
        <JournalEntryForm />
      </div>
    </div>
  );
}
