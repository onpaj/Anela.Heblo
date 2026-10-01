import os
import subprocess
from pathlib import Path

GIT = ["git", "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false"]


def run(repo: Path, *args: str) -> str:
    return subprocess.run([*GIT, *args], cwd=repo, check=True, capture_output=True, text=True).stdout.strip()


def init_repo(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True)
    run(path, "init", "-q", "-b", "main")
    return path


def commit(repo: Path, files: dict[str, str], message: str = "c", date: str | None = None) -> str:
    """date: ISO timestamp for author+committer (tests that order by commit time need distinct dates)."""
    for rel, content in files.items():
        target = repo / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
    run(repo, "add", "-A")
    env = {**os.environ, "GIT_AUTHOR_DATE": date, "GIT_COMMITTER_DATE": date} if date else None
    subprocess.run([*GIT, "commit", "-q", "-m", message], cwd=repo, check=True, capture_output=True, env=env)
    return run(repo, "rev-parse", "HEAD")
