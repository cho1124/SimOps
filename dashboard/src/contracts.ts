// Explicit transport DTOs. Aligned with ExperimentContracts.cs and ExperimentMetrics.cs.
export interface Definition {
  experimentId: string;
  hypothesis: string;
  gameVersion: string;
  agentVersion: string;
  runsPerCell: number;
  firstSeed: string;
  bootstrapReplicates: number;
  primaryMetric: string;
  targetCumulativeFailureRates: number[];
  agentIds: string[];
  variants: { id: string; role: string; attackPercentByStage: number[] }[];
  [key: string]: unknown;
}
export interface ExperimentListItem {
  id: string;
  status: string;
  revision: number;
  planHash: string;
  createdAt: string;
}
export interface Batch {
  id: string;
  status: string;
  completedCells: number;
  expectedCells: number;
  completedRuns: number;
  expectedRuns: number;
  resultDigest: string | null;
  jobs: {
    kind: string;
    variantId: string | null;
    agentId: string | null;
    status: string;
    attempts: number;
    lastError: string | null;
  }[];
}
export interface Decision {
  planHash: string;
  resultDigest: string;
  conclusion: string;
  selectedVariantId: string | null;
  reason: string;
}
export interface Detail {
  id: string;
  status: string;
  revision: number;
  planHash: string;
  definition: Definition;
  batch: Batch | null;
  decision: Decision | null;
}
export interface Estimate {
  difference: number;
  lower95: number;
  upper95: number;
  pairs: number;
}
export interface Stage {
  stage: number;
  entries: number;
  clears: number;
  failures: number;
  conditionalPassRate: number | null;
  cumulativeFailureRate: number;
  undefinedReason: string | null;
}
export interface Cell {
  variantId: string;
  agentId: string;
  agentVersion: string;
  validRuns: number;
  clearRate: number;
  curveTargetMae: number;
  stages: Stage[];
  configChecksum: string;
  sampleHash: string;
  turns: { mean: number; median: number; p90: number };
  rewardEntropy: number | null;
  examples: { seed: string; resultHash: string; actions: unknown[] }[];
}
export interface Comparison {
  variantId: string;
  eligibleForHumanReview: boolean;
  noviceMaeDifference: Estimate;
  checks: {
    key: string;
    passed: boolean;
    observed: number | null;
    requirement: string;
  }[];
}
export interface Report {
  experimentId: string;
  planHash: string;
  calculatorVersion: string;
  resultDigest: string;
  completedRuns: number;
  replayCheckedRuns: number;
  replayMismatchCount: number;
  reviewCandidateIds: string[];
  publicationState: string;
  cells: Cell[];
  comparisons: Comparison[];
  treatmentMaeDifference: Estimate;
  treatmentMaeDifferenceDirection: string;
}
