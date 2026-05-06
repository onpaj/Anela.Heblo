import React, { useState } from 'react';
import RagFeedbackForm from '../../components/feedback/RagFeedbackForm';
import { ArticleDetail, useSubmitArticleFeedbackMutation } from '../../api/hooks/useArticles';
import { ArticleStatus } from '../../api/generated/api-client';

interface ArticleFeedbackSectionProps {
  article: ArticleDetail;
}

const ArticleFeedbackSection: React.FC<ArticleFeedbackSectionProps> = ({ article }) => {
  const [alreadySubmitted, setAlreadySubmitted] = useState(false);
  const submitFeedback = useSubmitArticleFeedbackMutation(article.id);

  if (article.status !== ArticleStatus.Generated) return null;

  if (article.precisionScore !== null || article.styleScore !== null) {
    const precision = article.precisionScore ?? '-';
    const style = article.styleScore ?? '-';
    return (
      <div className="mt-6 border-t pt-4 space-y-1">
        <p className="text-sm font-medium text-gray-700">
          Hodnocení: Přesnost {precision}/5, Styl {style}/5
        </p>
        {article.feedbackComment && (
          <p data-testid="article-feedback-comment" className="text-sm text-gray-600 whitespace-pre-wrap">{article.feedbackComment}</p>
        )}
      </div>
    );
  }

  return (
    <div className="mt-6 border-t pt-4">
      <RagFeedbackForm
        onSubmit={(data) =>
          submitFeedback.mutate(data, {
            onSuccess: (result) => {
              if (result.alreadySubmitted) setAlreadySubmitted(true);
            },
          })
        }
        isSubmitting={submitFeedback.isPending}
        isSuccess={submitFeedback.isSuccess && !submitFeedback.data?.alreadySubmitted}
        alreadySubmitted={alreadySubmitted}
      />
      {submitFeedback.isError && (
        <p className="mt-2 text-sm text-red-600">Odeslání selhalo. Zkuste to znovu.</p>
      )}
    </div>
  );
};

export default ArticleFeedbackSection;
