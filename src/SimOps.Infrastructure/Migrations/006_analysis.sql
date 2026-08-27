-- Analysis owns only derived reports; it never updates experiments, metrics, decisions or seasons.
CREATE TABLE simops.analysis_jobs (
    id uuid PRIMARY KEY,
    experiment_id text NOT NULL REFERENCES simops.experiments(id),
    idempotency_key text NOT NULL UNIQUE CHECK (length(idempotency_key) BETWEEN 1 AND 100),
    snapshot_hash text NOT NULL CHECK (length(snapshot_hash) = 64),
    snapshot jsonb NOT NULL,
    status text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued','running','succeeded','failed')),
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 3),
    available_at timestamptz NOT NULL DEFAULT now(),
    lease_until timestamptz,
    lock_token uuid,
    last_error text,
    report jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK ((status = 'running') = (lease_until IS NOT NULL AND lock_token IS NOT NULL)),
    CHECK ((status = 'succeeded') = (report IS NOT NULL)),
    CHECK (report IS NULL OR report->>'snapshotHash' = snapshot_hash)
);
CREATE INDEX analysis_jobs_claim ON simops.analysis_jobs(available_at, created_at) WHERE status IN ('queued','running');
CREATE INDEX analysis_jobs_experiment ON simops.analysis_jobs(experiment_id, created_at DESC);

CREATE FUNCTION simops.protect_analysis_job() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Analysis history is immutable' USING ERRCODE='23514'; END IF;
    IF (NEW.id,NEW.experiment_id,NEW.idempotency_key,NEW.snapshot_hash,NEW.snapshot,NEW.created_at)
        IS DISTINCT FROM (OLD.id,OLD.experiment_id,OLD.idempotency_key,OLD.snapshot_hash,OLD.snapshot,OLD.created_at)
        OR OLD.status IN ('succeeded','failed') THEN
        RAISE EXCEPTION 'Analysis input/terminal evidence is immutable' USING ERRCODE='23514';
    END IF;
    IF NOT ((OLD.status='queued' AND NEW.status IN ('running','failed')) OR
        (OLD.status='running' AND NEW.status IN ('running','queued','succeeded','failed'))) THEN
        RAISE EXCEPTION 'Illegal analysis transition' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_analysis BEFORE UPDATE OR DELETE ON simops.analysis_jobs
    FOR EACH ROW EXECUTE FUNCTION simops.protect_analysis_job();
