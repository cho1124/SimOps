CREATE TABLE simops.experiments (
    id text PRIMARY KEY,
    definition jsonb NOT NULL,
    plan_hash text NOT NULL,
    revision integer NOT NULL DEFAULT 1 CHECK (revision > 0),
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft','ready','running','analyzing','decided','failed')),
    decision jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE FUNCTION simops.protect_experiment() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' OR NEW.id IS DISTINCT FROM OLD.id OR
       (OLD.status <> 'draft' AND (NEW.definition IS DISTINCT FROM OLD.definition OR NEW.plan_hash IS DISTINCT FROM OLD.plan_hash OR NEW.revision <> OLD.revision)) THEN
        RAISE EXCEPTION 'Registered experiment is immutable' USING ERRCODE = '23514';
    END IF;
    IF NEW.status <> OLD.status AND NOT (
       (OLD.status = 'draft' AND NEW.status = 'ready') OR
       (OLD.status = 'ready' AND NEW.status = 'running') OR
       (OLD.status = 'running' AND NEW.status IN ('analyzing','failed')) OR
       (OLD.status = 'analyzing' AND NEW.status = 'decided')) THEN
        RAISE EXCEPTION 'Invalid experiment transition' USING ERRCODE = '23514';
    END IF;
    IF OLD.decision IS NOT NULL AND NEW.decision IS DISTINCT FROM OLD.decision THEN
        RAISE EXCEPTION 'Decision is immutable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_experiment BEFORE UPDATE OR DELETE ON simops.experiments
    FOR EACH ROW EXECUTE FUNCTION simops.protect_experiment();

CREATE TABLE simops.experiment_variants (
    experiment_id text NOT NULL REFERENCES simops.experiments(id),
    variant_id text NOT NULL,
    role text NOT NULL CHECK (role IN ('control','treatment')),
    config_checksum text NOT NULL REFERENCES simops.game_configs(checksum),
    PRIMARY KEY (experiment_id, variant_id)
);
CREATE FUNCTION simops.immutable_record() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Record is append-only' USING ERRCODE = '23514'; END $$;
CREATE TRIGGER immutable_config BEFORE UPDATE OR DELETE ON simops.game_configs
    FOR EACH ROW EXECUTE FUNCTION simops.immutable_record();
CREATE TRIGGER immutable_variant BEFORE UPDATE OR DELETE ON simops.experiment_variants
    FOR EACH ROW EXECUTE FUNCTION simops.immutable_record();
CREATE FUNCTION simops.insert_draft_variant() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM 1 FROM simops.experiments WHERE id = NEW.experiment_id AND status = 'draft' FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'Variants require a draft experiment' USING ERRCODE = '23514'; END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER insert_draft_variant BEFORE INSERT ON simops.experiment_variants
    FOR EACH ROW EXECUTE FUNCTION simops.insert_draft_variant();

CREATE TABLE simops.simulation_batches (
    id uuid PRIMARY KEY,
    experiment_id text NOT NULL UNIQUE REFERENCES simops.experiments(id),
    idempotency_key text NOT NULL UNIQUE,
    execution_fingerprint text NOT NULL,
    status text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued','running','completed','failed','cancelled')),
    expected_cells integer NOT NULL CHECK (expected_cells = 18),
    expected_runs integer NOT NULL CHECK (expected_runs BETWEEN 18 AND 18000),
    report jsonb,
    summary jsonb,
    result_digest text,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz,
    CHECK ((status = 'completed') = (report IS NOT NULL AND summary IS NOT NULL AND result_digest IS NOT NULL))
);
CREATE TABLE simops.simulation_jobs (
    id uuid PRIMARY KEY,
    batch_id uuid NOT NULL REFERENCES simops.simulation_batches(id),
    kind text NOT NULL CHECK (kind IN ('cell','aggregate')),
    variant_id text,
    agent_id text,
    status text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued','running','succeeded','failed','cancelled')),
    attempts integer NOT NULL DEFAULT 0,
    max_attempts integer NOT NULL DEFAULT 3 CHECK (max_attempts BETWEEN 1 AND 3),
    available_at timestamptz NOT NULL DEFAULT now(),
    locked_until timestamptz,
    lock_token uuid,
    last_error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK ((kind = 'cell' AND variant_id IS NOT NULL AND agent_id IS NOT NULL) OR
           (kind = 'aggregate' AND variant_id IS NULL AND agent_id IS NULL)),
    UNIQUE NULLS NOT DISTINCT (batch_id, kind, variant_id, agent_id)
);
CREATE INDEX ix_simulation_jobs_claim ON simops.simulation_jobs(status, available_at, locked_until);
CREATE TABLE simops.experiment_cells (
    batch_id uuid NOT NULL REFERENCES simops.simulation_batches(id),
    variant_id text NOT NULL,
    agent_id text NOT NULL,
    valid_runs integer NOT NULL CHECK (valid_runs BETWEEN 1 AND 1000),
    sample_hash text NOT NULL,
    content jsonb NOT NULL,
    PRIMARY KEY (batch_id, variant_id, agent_id)
);
CREATE TRIGGER immutable_cell BEFORE UPDATE OR DELETE ON simops.experiment_cells
    FOR EACH ROW EXECUTE FUNCTION simops.immutable_record();
CREATE TABLE simops.experiment_audit (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    experiment_id text NOT NULL REFERENCES simops.experiments(id),
    action text NOT NULL,
    actor text NOT NULL DEFAULT 'operator',
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now()
);
CREATE TRIGGER immutable_experiment_audit BEFORE UPDATE OR DELETE ON simops.experiment_audit
    FOR EACH ROW EXECUTE FUNCTION simops.immutable_record();
