#!/usr/bin/env python3
"""Parse a reviewer subagent's verdict block into a validated decision.

Reads raw subagent output on stdin, writes one JSON object to stdout, and
always exits 0 — the caller must always get parseable JSON back, even for
garbage input.

This module owns the band boundaries. They are defined here and nowhere else:
duplicating them into a shell script or a prompt is how the two drift apart.
"""
import json
import re
import sys

MERGE_THRESHOLD = 80
NEEDS_WORK_THRESHOLD = 40

VERDICT_FOR_ACTION = {"merge": "MERGE", "comment": "COMMENT", "needs-work": "REJECT"}

_SCORE_RE = re.compile(r"^score:\s*(\S+)\s*$", re.MULTILINE)
_PR_RE = re.compile(r"^pr:\s*(\d+)\s*$", re.MULTILINE)
_VERDICT_RE = re.compile(r"^verdict:\s*(\S+)\s*$", re.MULTILINE)
_RISK_RE = re.compile(r"^risk:\s*(\S+)\s*$", re.MULTILINE)
_CONCERNS_RE = re.compile(r"^concerns:\s*(.*)$", re.MULTILINE)
_REASON_RE = re.compile(r"^\s*-\s+(.*\S)\s*$", re.MULTILINE)


def action_for_score(score: int) -> str:
    """Map a 0-100 score onto the action the parent will take."""
    if score >= MERGE_THRESHOLD:
        return "merge"
    if score >= NEEDS_WORK_THRESHOLD:
        return "comment"
    return "needs-work"


def _invalid(error: str) -> dict:
    """Every rejection path lands here: score 0, comment, never merge."""
    return {
        "pr": None, "score": 0, "action": "comment", "risk": "unknown",
        "reasons": [], "concerns": "review could not be parsed",
        "valid": False, "error": error,
    }


def parse_verdict(text: str) -> dict:
    """Parse the last verdict block in `text`, validating every field."""
    if not text or not text.strip():
        return _invalid("empty reviewer output")

    score_matches = list(_SCORE_RE.finditer(text))
    if not score_matches:
        return _invalid("no `score:` line found in reviewer output")

    # The reviewer may reconsider mid-output; the final block is its conclusion.
    tail = text[score_matches[-1].start():]

    raw_score = score_matches[-1].group(1)
    try:
        score = int(raw_score)
    except ValueError:
        return _invalid(f"score is not an integer: {raw_score!r}")
    if not 0 <= score <= 100:
        return _invalid(f"score out of range 0-100: {score}")

    reasons = _REASON_RE.findall(tail)
    if not reasons:
        return _invalid("no `reasons:` bullets found")

    verdict_match = _VERDICT_RE.search(tail)
    if not verdict_match:
        return _invalid("no `verdict:` line found")

    action = action_for_score(score)
    stated = verdict_match.group(1).upper()
    if stated != VERDICT_FOR_ACTION[action]:
        return _invalid(
            f"verdict {stated} contradicts score {score} "
            f"(expected {VERDICT_FOR_ACTION[action]})"
        )

    # `pr:` precedes `score:` inside a block, so look in the prefix — and take
    # the LAST match, which belongs to the same (final) block as the score.
    pr_matches = list(_PR_RE.finditer(text[: score_matches[-1].start()]))
    pr_match = pr_matches[-1] if pr_matches else None
    risk_match = _RISK_RE.search(tail)
    concerns_match = _CONCERNS_RE.search(tail)

    return {
        "pr": int(pr_match.group(1)) if pr_match else None,
        "score": score,
        "action": action,
        "risk": risk_match.group(1).lower() if risk_match else "unknown",
        "reasons": reasons,
        "concerns": concerns_match.group(1).strip() if concerns_match else "none",
        "valid": True,
        "error": None,
    }


def main() -> int:
    print(json.dumps(parse_verdict(sys.stdin.read())))
    return 0


if __name__ == "__main__":
    sys.exit(main())
