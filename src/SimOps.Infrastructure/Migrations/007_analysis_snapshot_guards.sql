-- PostgreSQL CHECK accepts UNKNOWN. Require TRUE so absent/JSON-null hashes cannot pass.
ALTER TABLE simops.analysis_jobs ADD CONSTRAINT analysis_snapshot_identity CHECK (
    (jsonb_typeof(snapshot) = 'object' AND snapshot->>'experimentId' = experiment_id
     AND snapshot->>'schemaVersion' = '1') IS TRUE
);
ALTER TABLE simops.analysis_jobs ADD CONSTRAINT analysis_report_identity CHECK (
    report IS NULL OR (jsonb_typeof(report) = 'object' AND report->>'snapshotHash' = snapshot_hash
        AND report->>'validationVersion' IS NOT NULL) IS TRUE
);
ALTER TABLE simops.analysis_jobs ADD CONSTRAINT analysis_unleased_state CHECK (
    status = 'running' OR (lease_until IS NULL AND lock_token IS NULL)
);
