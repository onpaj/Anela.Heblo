import React, { useState } from 'react';
import { Settings, BarChart, Plus, Play } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { 
  useClassificationRules, 
  useCreateClassificationRule, 
  useUpdateClassificationRule,
  useDeleteClassificationRule,
  useReorderClassificationRules,
  useClassifyInvoices,
  CreateClassificationRuleRequest,
  UpdateClassificationRuleRequest,
  ClassificationRule
} from '../../api/hooks/useInvoiceClassification';
import ClassificationHistoryPage from './ClassificationHistoryPage';
import RulesList from './components/RulesList';
import RuleForm from './components/RuleForm';
import ClassificationStats from './components/ClassificationStats';

const InvoiceClassificationPage: React.FC = () => {
  const { t } = useTranslation();
  const [showRulesModal, setShowRulesModal] = useState(false);
  const [showStatsModal, setShowStatsModal] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [editingRule, setEditingRule] = useState<ClassificationRule | null>(null);

  const { data: rules = [], isLoading, error } = useClassificationRules(false);
  const createMutation = useCreateClassificationRule();
  const updateMutation = useUpdateClassificationRule();
  const deleteMutation = useDeleteClassificationRule();
  const reorderMutation = useReorderClassificationRules();
  const classifyMutation = useClassifyInvoices();

  const handleSubmitRule = async (data: CreateClassificationRuleRequest | UpdateClassificationRuleRequest) => {
    try {
      if (editingRule) {
        await updateMutation.mutateAsync(data as UpdateClassificationRuleRequest);
        setEditingRule(null);
      } else {
        await createMutation.mutateAsync(data as CreateClassificationRuleRequest);
      }
      setShowForm(false);
    } catch (error) {
      console.error('Failed to save rule:', error);
    }
  };

  const handleDeleteRule = async (ruleId: string) => {
    if (window.confirm(t('invoiceClassification.confirmDelete', 'Are you sure you want to delete this rule?'))) {
      try {
        await deleteMutation.mutateAsync(ruleId);
      } catch (error) {
        console.error('Failed to delete rule:', error);
      }
    }
  };

  const handleReorderRules = async (ruleIds: string[]) => {
    try {
      await reorderMutation.mutateAsync(ruleIds);
    } catch (error) {
      console.error('Failed to reorder rules:', error);
    }
  };

  const handleRunClassification = async () => {
    try {
      const result = await classifyMutation.mutateAsync(true);
      alert(t('invoiceClassification.classificationComplete', {
        defaultValue: 'Classification complete: {{successful}} successful, {{manual}} manual review required, {{errors}} errors',
        successful: result.successfulClassifications,
        manual: result.manualReviewRequired,
        errors: result.errors
      }));
    } catch (error) {
      console.error('Failed to run classification:', error);
    }
  };

  const handleEditRule = (rule: ClassificationRule) => {
    setEditingRule(rule);
    setShowForm(true);
  };

  const handleCancelForm = () => {
    setShowForm(false);
    setEditingRule(null);
  };

  if (isLoading) {
    return (
      <div className="container mx-auto px-4 py-8">
        <div className="text-center">Loading...</div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="container mx-auto px-4 py-8">
        <div className="text-center text-red-600">
          Error loading classification rules: {error.message}
        </div>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col">
      {/* Header with buttons */}
      <div className="bg-white border-b border-gray-200 px-6 py-4">
        <div className="flex justify-between items-center">
          <div>
            <h1 className="text-2xl font-bold text-gray-900">
              {t('invoiceClassification.title', 'Invoice Classification')}
            </h1>
            <p className="mt-1 text-sm text-gray-500">
              {t('invoiceClassification.description', 'View classification history and manage rules')}
            </p>
          </div>
          
          <div className="flex gap-3">
            <button
              onClick={handleRunClassification}
              disabled={classifyMutation.isPending}
              className="inline-flex items-center px-4 py-2 bg-emerald-600 text-white text-sm font-medium rounded-md hover:bg-emerald-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-emerald-500 disabled:opacity-50"
            >
              <Play className="w-4 h-4 mr-2" />
              {classifyMutation.isPending 
                ? t('invoiceClassification.classifying', 'Classifying...') 
                : t('invoiceClassification.runClassification', 'Run Classification')
              }
            </button>
            
            <button
              onClick={() => setShowRulesModal(true)}
              className="inline-flex items-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
            >
              <Settings className="w-4 h-4 mr-2" />
              {t('invoiceClassification.rules', 'Rules')}
            </button>
            
            <button
              onClick={() => setShowStatsModal(true)}
              className="inline-flex items-center px-4 py-2 bg-gray-600 text-white text-sm font-medium rounded-md hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500"
            >
              <BarChart className="w-4 h-4 mr-2" />
              {t('invoiceClassification.statistics', 'Statistics')}
            </button>
          </div>
        </div>
      </div>

      {/* Main content - Classification History */}
      <div className="flex-1 overflow-hidden">
        <ClassificationHistoryPage />
      </div>

      {/* Rules Modal */}
      {showRulesModal && (
        <div className="fixed inset-0 z-50 overflow-y-auto">
          <div className="flex items-center justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
            <div className="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" onClick={() => setShowRulesModal(false)}></div>
            
            <span className="hidden sm:inline-block sm:align-middle sm:h-screen">&#8203;</span>
            
            <div className="inline-block align-bottom bg-white rounded-lg text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-6xl sm:w-full">
              <div className="bg-white px-4 pt-5 pb-4 sm:p-6 sm:pb-4">
                <div className="flex justify-between items-center mb-4">
                  <h3 className="text-lg leading-6 font-medium text-gray-900">
                    {t('invoiceClassification.rulesManagement', 'Classification Rules Management')}
                  </h3>
                  <button
                    onClick={() => setShowRulesModal(false)}
                    className="rounded-md bg-white text-gray-400 hover:text-gray-500 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  >
                    <span className="sr-only">Close</span>
                    <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </button>
                </div>
                
                <div className="max-h-96 overflow-y-auto">
                  {showForm && (
                    <div className="mb-6">
                      <RuleForm
                        rule={editingRule}
                        onSubmit={handleSubmitRule}
                        onCancel={handleCancelForm}
                        isLoading={createMutation.isPending || updateMutation.isPending}
                      />
                    </div>
                  )}
                  
                  <div className="mb-4">
                    <button
                      onClick={() => setShowForm(true)}
                      disabled={showForm}
                      className="inline-flex items-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50"
                    >
                      <Plus className="w-4 h-4 mr-2" />
                      {t('invoiceClassification.addRule', 'Add Rule')}
                    </button>
                  </div>

                  {isLoading ? (
                    <div className="text-center py-4">
                      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600 mx-auto"></div>
                      <p className="mt-2 text-sm text-gray-500">{t('common.loading', 'Loading...')}</p>
                    </div>
                  ) : error ? (
                    <div className="text-center py-4 text-red-600">
                      Error loading classification rules: {String(error)}
                    </div>
                  ) : (
                    <RulesList
                      rules={rules}
                      onEdit={handleEditRule}
                      onDelete={handleDeleteRule}
                      onReorder={handleReorderRules}
                      isReordering={reorderMutation.isPending}
                      isDeleting={deleteMutation.isPending}
                    />
                  )}
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Statistics Modal */}
      {showStatsModal && (
        <div className="fixed inset-0 z-50 overflow-y-auto">
          <div className="flex items-center justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
            <div className="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" onClick={() => setShowStatsModal(false)}></div>
            
            <span className="hidden sm:inline-block sm:align-middle sm:h-screen">&#8203;</span>
            
            <div className="inline-block align-bottom bg-white rounded-lg text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-4xl sm:w-full">
              <div className="bg-white px-4 pt-5 pb-4 sm:p-6 sm:pb-4">
                <div className="flex justify-between items-center mb-4">
                  <h3 className="text-lg leading-6 font-medium text-gray-900">
                    {t('invoiceClassification.statisticsTitle', 'Classification Statistics')}
                  </h3>
                  <button
                    onClick={() => setShowStatsModal(false)}
                    className="rounded-md bg-white text-gray-400 hover:text-gray-500 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  >
                    <span className="sr-only">Close</span>
                    <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </button>
                </div>
                
                <div className="max-h-96 overflow-y-auto">
                  <ClassificationStats />
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default InvoiceClassificationPage;