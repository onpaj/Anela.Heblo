"""Thin wrappers over the git CLI."""
import subprocess
from pathlib import Path


def _git(repo: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["git", *args], cwd=repo, check=check, capture_output=True, text=True)


def tracked_files(repo: Path) -> list[str]:
    return _git(repo, "ls-files").stdout.split("\n")[:-1]


def commit_exists(repo: Path, sha: str) -> bool:
    return _git(repo, "cat-file", "-e", f"{sha}^{{commit}}", check=False).returncode == 0


def changed_files(repo: Path, from_ref: str, to_ref: str) -> list[str]:
    out = _git(repo, "diff", "--name-only", f"{from_ref}..{to_ref}").stdout
    return [line for line in out.split("\n") if line]


def merge_base(repo: Path, a: str, b: str) -> str:
    return _git(repo, "merge-base", a, b).stdout.strip()


def commit_time(repo: Path, sha: str) -> int:
    return int(_git(repo, "show", "-s", "--format=%ct", sha).stdout.strip())


def last_commit_touching(repo: Path, path: str) -> str | None:
    out = _git(repo, "log", "-1", "--format=%H", "--", path).stdout.strip()
    return out or None


def is_ancestor(repo: Path, a: str, b: str) -> bool:
    return _git(repo, "merge-base", "--is-ancestor", a, b, check=False).returncode == 0
