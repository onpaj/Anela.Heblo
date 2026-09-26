import json
import subprocess
import sys
from pathlib import Path

from docs_model import REQUIRED_HEADINGS
from helpers import commit, init_repo

SCRIPT = Path(__file__).resolve().parents[1] / "check.py"
BODY = "\n\n".join(f"{h}\n\nNone." for h in REQUIRED_HEADINGS)
CONFIG = "orphan_mode: {mode}\ninclude: []\nignore: []\n"


def doc_text(verified_at: str) -> str:
    return (f'---\nprocess: sync-a\nkind: sync\nsummary: A sync.\nowns:\n  - backend/src/A/**\n'
            f'verified_at: "{verified_at}"\nrelated: []\n---\n\n# A\n\n{BODY}\n')


def run(repo, *args):
    return subprocess.run([sys.executable, str(SCRIPT), "--repo", str(repo), *args],
                          capture_output=True, text=True)


def setup(tmp_path, mode="warn", with_orphan=False):
    repo = init_repo(tmp_path)
    files = {"backend/src/A/SyncJob.cs": "public sealed class SyncJob : IRecurringJob {}",
             "scripts/process-docs/config.yaml": CONFIG.format(mode=mode)}
    if with_orphan:
        files["backend/src/B/OtherJob.cs"] = "public sealed class OtherJob : IRecurringJob {}"
    sha = commit(repo, files)
    commit(repo, {"docs/processes/sync-a.md": doc_text(sha[:9])})
    assert run(repo, "index").returncode == 0  # INDEX.md lives in the working tree; check reads it from disk
    return repo, sha


def test_check_passes_on_clean_catalog(tmp_path):
    repo, _ = setup(tmp_path)
    result = run(repo, "check")
    assert result.returncode == 0, result.stdout + result.stderr


def test_check_fails_on_index_drift(tmp_path):
    repo, _ = setup(tmp_path)
    (repo / "docs/processes/INDEX.md").write_text("stale", encoding="utf-8")
    result = run(repo, "check")
    assert result.returncode == 1
    assert "INDEX.md" in result.stdout


def test_orphan_warns_in_warn_mode_and_fails_in_fail_mode(tmp_path):
    warn_repo, _ = setup(tmp_path / "w", mode="warn", with_orphan=True)
    assert run(warn_repo, "check").returncode == 0
    fail_repo, _ = setup(tmp_path / "f", mode="fail", with_orphan=True)
    result = run(fail_repo, "check")
    assert result.returncode == 1
    assert "backend/src/B/OtherJob.cs" in result.stdout


def test_check_json_reports_stale(tmp_path):
    repo, _ = setup(tmp_path)
    commit(repo, {"backend/src/A/SyncJob.cs": "public sealed class SyncJob : IRecurringJob { int x; }"})
    data = json.loads(run(repo, "check", "--json").stdout)
    assert data["stale"] == [{"process": "sync-a", "reason": "changed", "files": ["backend/src/A/SyncJob.cs"]}]
    assert data["oldest"] == ["sync-a"]
    assert data["orphan_mode"] == "warn"


def test_pr_writes_comment_only_when_flagged(tmp_path):
    repo, _ = setup(tmp_path)
    subprocess.run(["git", "branch", "base"], cwd=repo, check=True)
    comment = tmp_path / "comment.md"
    assert run(repo, "pr", "--base", "base", "--comment-file", str(comment)).returncode == 0
    assert not comment.exists()
    commit(repo, {"backend/src/A/SyncJob.cs": "changed"})
    assert run(repo, "pr", "--base", "base", "--comment-file", str(comment)).returncode == 0
    text = comment.read_text(encoding="utf-8")
    assert "sync-a" in text and "backend/src/A/SyncJob.cs" in text


def test_pr_comment_starts_with_hidden_marker_for_upsert(tmp_path):
    repo, _ = setup(tmp_path)
    subprocess.run(["git", "branch", "base"], cwd=repo, check=True)
    commit(repo, {"backend/src/A/SyncJob.cs": "changed"})
    comment = tmp_path / "comment.md"
    assert run(repo, "pr", "--base", "base", "--comment-file", str(comment)).returncode == 0
    assert comment.read_text(encoding="utf-8").splitlines()[0] == "<!-- process-docs -->"


def test_pr_resolved_file_written_only_when_nothing_flagged_and_requested(tmp_path):
    repo, _ = setup(tmp_path)
    subprocess.run(["git", "branch", "base"], cwd=repo, check=True)
    comment = tmp_path / "comment.md"
    resolved = tmp_path / "resolved.md"

    # nothing flagged, --resolved-file passed -> resolved body written, no comment file
    assert run(repo, "pr", "--base", "base", "--comment-file", str(comment),
              "--resolved-file", str(resolved)).returncode == 0
    assert not comment.exists()
    resolved_text = resolved.read_text(encoding="utf-8")
    assert resolved_text.splitlines()[0] == "<!-- process-docs -->"
    assert "no longer flagged" in resolved_text

    # flagged -> resolved file is not (re)written, comment file is
    resolved.unlink()
    commit(repo, {"backend/src/A/SyncJob.cs": "changed"})
    assert run(repo, "pr", "--base", "base", "--comment-file", str(comment),
              "--resolved-file", str(resolved)).returncode == 0
    assert comment.exists()
    assert not resolved.exists()


def test_pr_with_unknown_base_warns_and_exits_zero(tmp_path):
    repo, _ = setup(tmp_path)
    comment = tmp_path / "comment.md"
    result = run(repo, "pr", "--base", "no-such-ref", "--comment-file", str(comment))
    assert result.returncode == 0
    assert not comment.exists()
    assert "no-such-ref" in result.stderr
