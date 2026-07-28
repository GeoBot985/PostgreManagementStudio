# Sprint 28 — Query Performance History and Local Baselines

Implemented the privacy-aware local query-history analysis foundation.

## Delivered

- Stable local query-history identity based on application fingerprint, server, database, and command type.
- Execution samples with explicit outcome and inclusion states; failures, cancellations, warm-ups, exclusions, and anomalies do not silently influence successful baselines.
- Robust local baselines using median, MAD, quartiles, p90, min/max, sample limits, age windows, and confidence explanations.
- Deterministic performance classification with both relative and absolute thresholds.
- Environment/timing compatibility checks that prevent incompatible samples from sharing a baseline silently.
- Privacy modes for full, normalized, preview-only, and no query text, plus local string/numeric literal redaction.
- Retention selection with pinned-record protection and Markdown export.
- Unit coverage for statistics, classification, compatibility, privacy, retention, percentiles, and export.

## Boundary

The repository has no existing local SQLite/storage abstraction, so this sprint adds the immutable history domain and repository-facing contracts without introducing an unreviewed storage dependency. A transactional SQLite repository, editor recording hooks, history workspace, repeated-execution UI, JSON/CSV import-export, and plan-reference lifecycle remain follow-up integration/UI work. Normal query execution is unaffected.
