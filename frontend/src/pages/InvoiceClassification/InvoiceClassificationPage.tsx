import React, { useState } from 'react';
import { Plus, Play, BarChart } from 'lucide-react';
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
import RulesList from './components/RulesList';
import RuleForm from './components/RuleForm';
import ClassificationStats from './components/ClassificationStats';

const InvoiceClassificationPage: React.FC = () => {
  const { t } = useTranslation();
  const [showForm, setShowForm] = useState(false);
  const [editingRule, setEditingRule] = useState<ClassificationRule | null>(null);
  const [showStats, setShowStats] = useState(false);

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
    <div className="container mx-auto px-4 py-8">
      <div className="mb-8">
        <h1 className="text-3xl font-bold text-gray-900 mb-2">
          {t('invoiceClassification.title', 'Invoice Classification')}
        </h1>
        <p className="text-gray-600">
          {t('invoiceClassification.description', 'Manage automated classification rules for received invoices')}
        </p>
      </div>

      <div className="flex gap-4 mb-6">
        <button
          onClick={() => setShowForm(true)}
          disabled={showForm}
          className="inline-flex items-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50"
        >
          <Plus className="w-4 h-4 mr-2" />
          {t('invoiceClassification.addRule', 'Add Rule')}
        </button>

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
          onClick={() => setShowStats(!showStats)}
          className="inline-flex items-center px-4 py-2 bg-gray-600 text-white text-sm font-medium rounded-md hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500"
        >
          <BarChart className="w-4 h-4 mr-2" />
          {showStats 
            ? t('invoiceClassification.hideStats', 'Hide Statistics') 
            : t('invoiceClassification.showStats', 'Show Statistics')
          }
        </button>
      </div>

      {showStats && (
        <div className="mb-8">
          <ClassificationStats />
        </div>
      )}

      {showForm && (
        <div className="mb-8">
          <RuleForm
            rule={editingRule}
            onSubmit={handleSubmitRule}
            onCancel={handleCancelForm}
            isLoading={createMutation.isPending || updateMutation.isPending}
          />
        </div>
      )}

      <RulesList
        rules={rules}
        onEdit={handleEditRule}
        onDelete={handleDeleteRule}
        onReorder={handleReorderRules}
        isReordering={reorderMutation.isPending}
        isDeleting={deleteMutation.isPending}
      />
    </div>
  );
};

export default InvoiceClassificationPage;