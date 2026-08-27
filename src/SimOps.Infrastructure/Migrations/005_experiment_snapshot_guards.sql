ALTER TABLE simops.experiments ADD CONSTRAINT ck_decision_state CHECK ((status='decided') = (decision IS NOT NULL));
ALTER TABLE simops.experiment_cells ADD CONSTRAINT ck_cell_run_count CHECK (jsonb_array_length(content->'runs')=valid_runs);
CREATE FUNCTION simops.protect_simulation_batch() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP='DELETE' OR OLD.status IN ('completed','failed','cancelled') OR
       NEW.id IS DISTINCT FROM OLD.id OR NEW.experiment_id IS DISTINCT FROM OLD.experiment_id OR
       NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key OR NEW.execution_fingerprint IS DISTINCT FROM OLD.execution_fingerprint OR
       NEW.expected_cells<>OLD.expected_cells OR NEW.expected_runs<>OLD.expected_runs THEN
        RAISE EXCEPTION 'Batch identity and terminal snapshots are immutable' USING ERRCODE='23514';
    END IF;
    IF NEW.status<>OLD.status AND NOT (
       (OLD.status='queued' AND NEW.status IN ('running','failed','cancelled')) OR
       (OLD.status='running' AND NEW.status IN ('completed','failed','cancelled'))) THEN
        RAISE EXCEPTION 'Invalid batch transition' USING ERRCODE='23514';
    END IF;
    IF NEW.status='completed' AND
       (SELECT count(*) FROM simops.experiment_cells WHERE batch_id=NEW.id)<>NEW.expected_cells THEN
        RAISE EXCEPTION 'All cells are required before completion' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_simulation_batch BEFORE UPDATE OR DELETE ON simops.simulation_batches
    FOR EACH ROW EXECUTE FUNCTION simops.protect_simulation_batch();
