import React, { useState } from 'react'
import { AlertCircle, BookOpen, Calendar, Edit2, Loader2, Plus } from 'lucide-react'
import { format } from 'date-fns'
import type { JournalEntryDto } from '../../../api/generated/api-client'
import JournalEntryModal from '../../JournalEntryModal'
import { truncateContent } from '../Journal/journalPreview'

interface FinancialJournalPanelProps {
  entries: JournalEntryDto[]
  isLoading: boolean
  isError: boolean
}

export const FinancialJournalPanel: React.FC<FinancialJournalPanelProps> = ({
  entries,
  isLoading,
  isError,
}) => {
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [selectedEntry, setSelectedEntry] = useState<JournalEntryDto | undefined>()

  const openCreate = () => {
    setSelectedEntry(undefined)
    setIsModalOpen(true)
  }

  const openEdit = (entry: JournalEntryDto) => {
    setSelectedEntry(entry)
    setIsModalOpen(true)
  }

  const closeModal = () => {
    setIsModalOpen(false)
    setSelectedEntry(undefined)
  }

  return (
    <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark sm:rounded-md mb-8">
      <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-graphite-border flex items-start justify-between gap-4">
        <div>
          <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text flex items-center">
            <BookOpen className="h-5 w-5 mr-2 text-gray-500 dark:text-graphite-muted" />
            Záznamy deníku ({entries.length})
          </h3>
          <p className="mt-1 max-w-2xl text-sm text-gray-500 dark:text-graphite-muted">
            Firemní záznamy bez vazby na produkt. Měsíce se záznamem jsou v grafu označeny oranžovým bodem.
          </p>
        </div>
        <button
          type="button"
          onClick={openCreate}
          className="inline-flex items-center flex-shrink-0 px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 transition-colors"
        >
          <Plus className="h-4 w-4 mr-1.5" />
          Přidat záznam
        </button>
      </div>

      <div className="px-4 py-4 sm:px-6">
        {isLoading ? (
          <div className="flex items-center space-x-2 text-sm text-gray-500 dark:text-graphite-muted">
            <Loader2 className="h-4 w-4 animate-spin text-indigo-500" />
            <span>Načítání záznamů deníku...</span>
          </div>
        ) : isError ? (
          <div className="flex items-center space-x-2 text-sm text-red-600 dark:text-red-400">
            <AlertCircle className="h-4 w-4" />
            <span>Záznamy deníku se nepodařilo načíst.</span>
          </div>
        ) : entries.length === 0 ? (
          <p className="text-sm text-gray-500 dark:text-graphite-muted">
            Pro zobrazené období nejsou žádné firemní záznamy deníku.
          </p>
        ) : (
          <ul className="space-y-3">
            {entries.map((entry) => (
              <li key={entry.id}>
                <button
                  type="button"
                  onClick={() => openEdit(entry)}
                  className="w-full text-left bg-white border border-gray-200 rounded-lg p-4 hover:shadow-md transition-shadow dark:bg-graphite-surface dark:border-graphite-border dark:hover:shadow-soft-dark"
                  title="Upravit záznam"
                >
                  <div className="flex items-start justify-between">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center space-x-3 mb-1">
                        <span className="text-sm font-semibold text-gray-900 dark:text-graphite-text truncate">
                          {entry.title || 'Bez názvu'}
                        </span>
                        <span className="text-xs text-gray-500 dark:text-graphite-muted flex items-center flex-shrink-0">
                          <Calendar className="h-3 w-3 mr-1" />
                          {entry.entryDate ? format(new Date(entry.entryDate), 'dd.MM.yyyy') : ''}
                        </span>
                      </div>
                      {entry.content && (
                        <p className="text-sm text-gray-600 dark:text-graphite-muted line-clamp-2">
                          {truncateContent(entry.content)}
                        </p>
                      )}
                      {entry.tags && entry.tags.length > 0 && (
                        <div className="flex flex-wrap gap-1 mt-2">
                          {entry.tags.map((tag) => (
                            <span
                              key={tag.id}
                              className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-700 dark:bg-graphite-surface-2 dark:text-graphite-muted"
                            >
                              {tag.name}
                            </span>
                          ))}
                        </div>
                      )}
                    </div>
                    <Edit2 className="ml-3 h-4 w-4 flex-shrink-0 text-gray-400 dark:text-graphite-faint" />
                  </div>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      <JournalEntryModal
        isOpen={isModalOpen}
        onClose={closeModal}
        entry={selectedEntry}
        isEdit={!!selectedEntry}
      />
    </div>
  )
}
