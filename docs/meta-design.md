# Meta design

## Feature

The human provides the requirements that constitute a feature.

## Delivering a feature

Delivering a feature is done in Slices. Each slice is a coherent, independently verifiable increment —
it builds and its tests pass on its own.

Rarely, a slice is split further — consult the human first.

## Designing a feature

Feature design has the following meta-structure. x is a number.

- Design options (Ox, each with pros/cons, which one we recommend & why)
- Slices (Sx, as described above)
- Tasks (Tx, one or more per slice)
- Risks (Rx, overall)
- Assumptions (Ax, overall)
- Deferrals (Dx, overall)

The planning-time options analysis may be richer (summary, affected layers, risk, effort); only
pros/cons + recommendation are persisted here. The persisted feature file also carries Requirements
(input) and Notes.
