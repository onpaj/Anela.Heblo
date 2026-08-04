#!/usr/bin/env bash
#
# survey.sh — measure a repository so a module map can be cut from real numbers
# instead of guesses. Language-agnostic: it asks git what is tracked rather than
# knowing anything about frameworks or layouts.
#
# Prints a digest on stdout for the arch-map skill to reason over: detected
# ecosystems, LOC and file counts per directory at depth 1 and 2, the biggest
# individual files, and the documents that are candidates for the map's
# `## Normative documents` section.
#
# Usage:
#   survey.sh [repo-path]        # defaults to the repo you are standing in
#
# Env:
#   ARCH_MAX_DEPTH   deepest directory grouping to report (default 2)
set -euo pipefail

ROOT="${1:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
[ -d "$ROOT" ] || { echo "survey: not a directory: $ROOT" >&2; exit 1; }
ROOT="$(cd "$ROOT" && pwd)"
MAX_DEPTH="${ARCH_MAX_DEPTH:-2}"

# Source extensions worth measuring. Deliberately broad — a map should account
# for every language a repo actually contains, not just its primary one.
EXT_RE='\.(py|js|jsx|mjs|cjs|ts|tsx|vue|svelte|cs|fs|vb|java|kt|kts|scala|go|rs|rb|php|swift|m|mm|c|h|cc|cpp|hpp|cxx|ex|exs|erl|clj|cljs|dart|lua|pl|r|sh|bash|zsh|ps1|sql|graphql|proto)$'

# Paths that exist in a checkout but are not hand-written source. Generated and
# vendored trees inflate a part's size and make it look like a split candidate
# when it is not.
SKIP_RE='(^|/)(\.git|node_modules|vendor|third_party|dist|build|out|target|bin|obj|\.venv|venv|\.tox|__pycache__|\.next|\.nuxt|coverage|htmlcov|\.mypy_cache|\.pytest_cache|migrations|generated|__generated__|\.gen)/'
MINIFIED_RE='(\.min\.(js|css)|\-lock\.(json|yaml)|\.lock)$'

cd "$ROOT"

# git ls-files is the whole trick: it respects .gitignore, so build output and
# local junk never reach the numbers. `--others --exclude-standard` adds files
# that are new but not ignored — without it a map written moments ago would be
# invisible to the survey that is supposed to find it. The find fallback covers
# a non-git tree.
if git rev-parse --git-dir >/dev/null 2>&1; then
  all_files="$(git ls-files --cached --others --exclude-standard)"
else
  all_files="$(find . -type f | sed 's|^\./||')"
fi

src_files="$(
  printf '%s\n' "$all_files" \
    | grep -Ei "$EXT_RE" \
    | grep -Ev "$SKIP_RE" \
    | grep -Ev "$MINIFIED_RE" \
    || true
)"

src_count="$(printf '%s\n' "$src_files" | grep -c . || true)"
all_count="$(printf '%s\n' "$all_files" | grep -c . || true)"

if [ "$src_count" -eq 0 ]; then
  echo "survey: no source files found under $ROOT" >&2
  echo "  the extension list may not cover this repository's languages" >&2
  exit 1
fi

# file<TAB>loc for every source file, computed once and reused by each section.
loc_table="$(
  printf '%s\n' "$src_files" \
    | tr '\n' '\0' \
    | xargs -0 wc -l 2>/dev/null \
    | awk '$2 != "total" { loc = $1; $1 = ""; sub(/^ /, ""); print $0 "\t" loc }'
)"

total_loc="$(printf '%s\n' "$loc_table" | awk -F'\t' '{s += $2} END {print s + 0}')"

echo "== Repository =="
echo "root: $ROOT"

# Ecosystems, by marker file. Tells the map author which conventional seams to
# look for (features/, controllers/, packages/) without hard-coding any.
ecosystems=""
add_eco() { case " $ecosystems " in *" $1 "*) ;; *) ecosystems="$ecosystems $1" ;; esac; }
while IFS= read -r marker; do
  case "$(basename "$marker")" in
    package.json)                 add_eco "javascript/node" ;;
    pyproject.toml|setup.py|setup.cfg) add_eco "python" ;;
    go.mod)                       add_eco "go" ;;
    Cargo.toml)                   add_eco "rust" ;;
    pom.xml|build.gradle|build.gradle.kts) add_eco "jvm" ;;
    Gemfile)                      add_eco "ruby" ;;
    composer.json)                add_eco "php" ;;
    *.sln|*.csproj|*.fsproj)      add_eco "dotnet" ;;
    pubspec.yaml)                 add_eco "dart/flutter" ;;
  esac
done <<EOF
$(printf '%s\n' "$all_files" | grep -Ei '(^|/)(package\.json|pyproject\.toml|setup\.py|setup\.cfg|go\.mod|Cargo\.toml|pom\.xml|build\.gradle(\.kts)?|Gemfile|composer\.json|pubspec\.yaml)$|\.(sln|csproj|fsproj)$' | grep -Ev "$SKIP_RE" || true)
EOF
echo "ecosystems:${ecosystems:- unknown}"
echo "tracked files: $all_count   source files: $src_count   source LOC: $total_loc"
echo

for depth in $(seq 1 "$MAX_DEPTH"); do
  echo "== LOC by directory, depth $depth =="
  printf '%s\n' "$loc_table" \
    | awk -F'\t' -v d="$depth" '
        {
          n = split($1, parts, "/")
          if (n <= d) { key = (n == 1 ? "(repo root)" : $1) }
          else {
            key = parts[1]
            for (i = 2; i <= d; i++) key = key "/" parts[i]
            key = key "/"
          }
          loc[key] += $2; files[key] += 1
        }
        END { for (k in loc) printf "%8d LOC  %5d files  %s\n", loc[k], files[k], k }' \
    | sort -rn
  echo
done

echo "== Largest individual files =="
printf '%s\n' "$loc_table" \
  | awk -F'\t' '{ printf "%8d LOC  %s\n", $2, $1 }' \
  | sort -rn | head -15
echo

echo "== Candidate normative documents =="
echo "(the map's '## Normative documents' section should name the real ones)"
# `|| true` throughout: grep exits 1 when it matches nothing, and under
# `set -o pipefail` that would abort the whole survey instead of printing an
# empty section. A repo with no docs is a fact to report, not a failure.
printf '%s\n' "$all_files" \
  | { grep -Ei '(^|/)(CLAUDE|AGENTS|GEMINI|README|CONTRIBUTING|ARCHITECTURE|CONVENTIONS)\.mdx?$|(^|/)docs?/.*\.mdx?$|(^|/)adr/|(^|/)rfcs?/' || true; } \
  | { grep -Ev "$SKIP_RE" || true; } \
  | head -40
echo

echo "== Existing module map =="
found_map="$(printf '%s\n' "$all_files" | grep -E '(^|/)module-map\.md$' || true)"
if [ -n "$found_map" ]; then
  printf '%s\n' "$found_map"
else
  echo "(none — this is a first cut)"
fi
echo

echo "== Sizing guidance =="
cat <<'EOF'
Aim for parts a single review can hold in context. Scale the target to the repo:
  - large codebase (>50k LOC): 1,500-6,000 LOC per part
  - small codebase (<5k LOC):  one module + its tests is usually one part
Split anything too big along an existing seam (feature folders, route groups,
package boundaries) — never at an arbitrary LOC boundary. Merge anything too
thin to say something useful about. A part must still be describable in one
sentence after a split.
EOF
