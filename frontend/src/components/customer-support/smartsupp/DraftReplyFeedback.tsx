import React, { useState } from "react";
import RagFeedbackForm from "../../feedback/RagFeedbackForm";
import { useSubmitDraftReplyFeedback } from "./hooks/useSubmitDraftReplyFeedback";

interface DraftReplyFeedbackProps {
  logId: string;
}

/**
 * Precision/style rating for a generated Smartsupp draft reply, feeding the RAG eval dataset.
 * Mirrors the KnowledgeBase Ask feedback affordance.
 */
const DraftReplyFeedback: React.FC<DraftReplyFeedbackProps> = ({ logId }) => {
  const submitFeedback = useSubmitDraftReplyFeedback();
  const [alreadySubmitted, setAlreadySubmitted] = useState(false);

  return (
    <RagFeedbackForm
      onSubmit={(data) =>
        submitFeedback.mutate(
          {
            logId,
            precisionScore: data.precisionScore,
            styleScore: data.styleScore,
            comment: data.comment,
          },
          {
            onSuccess: (result) => {
              if (result.alreadySubmitted) setAlreadySubmitted(true);
            },
          },
        )
      }
      isSubmitting={submitFeedback.isPending}
      isSuccess={submitFeedback.isSuccess && !submitFeedback.data?.alreadySubmitted}
      alreadySubmitted={alreadySubmitted}
    />
  );
};

export default DraftReplyFeedback;
