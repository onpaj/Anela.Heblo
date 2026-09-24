from analysis import (find_dead_globs, find_job_files, find_orphans, find_stale,
                      find_untouched_in_pr, oldest_verified)
from docs_model import ProcessDoc
from git_ops import tracked_files
from helpers import commit, init_repo


def doc(process="calc-a", owns=("src/a/**",), verified_at="0000000", path=None):
    return ProcessDoc(path or f"docs/processes/{process}.md", process, "calculation",
                      "s", tuple(owns), verified_at, (), "")


def test_doc_is_stale_when_owned_file_changed_since_verified(tmp_path):
    repo = init_repo(tmp_path)
    base = commit(repo, {"src/a/x.cs": "1", "src/b/y.cs": "1"})
    commit(repo, {"src/a/x.cs": "2"})
    stale = find_stale(repo, [doc(verified_at=base[:9])])
    assert [(s.process, s.reason, s.files) for s in stale] == [("calc-a", "changed", ("src/a/x.cs",))]


def test_doc_not_stale_when_only_unowned_files_changed(tmp_path):
    repo = init_repo(tmp_path)
    base = commit(repo, {"src/a/x.cs": "1", "src/b/y.cs": "1"})
    commit(repo, {"src/b/y.cs": "2"})
    assert find_stale(repo, [doc(verified_at=base)]) == []


def test_unknown_verified_at_is_reported_not_raised(tmp_path):
    repo = init_repo(tmp_path)
    commit(repo, {"src/a/x.cs": "1"})
    stale = find_stale(repo, [doc(verified_at="deadbeef")])
    assert stale[0].reason == "unknown-commit"


def test_dead_glob_reported():
    errors = find_dead_globs([doc(owns=("src/a/**", "src/gone/**"))], ["src/a/x.cs"])
    assert len(errors) == 1 and "src/gone/**" in errors[0]


JOB_DIRECT = "public sealed class SyncJob : IRecurringJob { }"
JOB_BASE = "public abstract class ImportJobBase : IRecurringJob { }"
JOB_DERIVED = "public sealed class CzkImportJob : ImportJobBase { }"
NOT_JOB = "public class Helper : IDisposable { }"


def test_job_detection_follows_abstract_bases_and_skips_them(tmp_path):
    repo = init_repo(tmp_path)
    commit(repo, {
        "backend/src/A/SyncJob.cs": JOB_DIRECT,
        "backend/src/B/ImportJobBase.cs": JOB_BASE,
        "backend/src/B/CzkImportJob.cs": JOB_DERIVED,
        "backend/src/C/Helper.cs": NOT_JOB,
    })
    jobs = find_job_files(repo, tracked_files(repo))
    assert jobs == ["backend/src/A/SyncJob.cs", "backend/src/B/CzkImportJob.cs"]


def test_orphans_exclude_owned_and_ignored_and_add_included(tmp_path):
    repo = init_repo(tmp_path)
    commit(repo, {
        "backend/src/A/SyncJob.cs": JOB_DIRECT,
        "backend/src/B/OtherJob.cs": JOB_DIRECT.replace("SyncJob", "OtherJob"),
        "backend/src/C/IgnoredJob.cs": JOB_DIRECT.replace("SyncJob", "IgnoredJob"),
        "backend/src/D/MarginCalc.cs": "public class MarginCalc {}",
    })
    files = tracked_files(repo)
    orphans = find_orphans(repo, [doc(owns=("backend/src/A/**",))], files,
                           include=["backend/src/D/*.cs"], ignore=["backend/src/C/**"])
    assert orphans == ["backend/src/B/OtherJob.cs", "backend/src/D/MarginCalc.cs"]


def test_pr_flags_doc_whose_code_changed_without_doc():
    docs = [doc(), doc(process="calc-b", owns=("src/b/**",))]
    flagged = find_untouched_in_pr(docs, ["src/a/x.cs", "src/b/y.cs", "docs/processes/calc-b.md"])
    assert [(f.process, f.files) for f in flagged] == [("calc-a", ("src/a/x.cs",))]


def test_oldest_verified_orders_by_commit_time_unknown_first(tmp_path):
    repo = init_repo(tmp_path)
    first = commit(repo, {"f": "1"}, date="2026-01-01T00:00:00")
    second = commit(repo, {"f": "2"}, date="2026-02-01T00:00:00")
    docs = [doc("calc-new", verified_at=second), doc("calc-old", verified_at=first),
            doc("calc-bad", verified_at="deadbeef")]
    assert oldest_verified(repo, docs, limit=2) == ["calc-bad", "calc-old"]
