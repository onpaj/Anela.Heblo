# Review: promote-ci-and-docs r1

Spec check: knip exits 0 -> `continue-on-error` removed from the knip step only; report upload kept; process-docs index clean; only allowed paths changed. `set -o pipefail` is a necessary addition, otherwise the tee pipe would make the step never fail. Validation (build, tests, knip) passes; lint errors are pre-existing and outside this change.

**Status:** PASS
