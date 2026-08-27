CREATE TABLE simops.config_publications (
    id uuid PRIMARY KEY,
    kind text NOT NULL CHECK(kind IN ('publish','rollback')),
    idempotency_key text NOT NULL UNIQUE,
    request_hash text NOT NULL,
    previous_season_id uuid NOT NULL UNIQUE REFERENCES simops.seasons(id),
    season_id uuid NOT NULL UNIQUE REFERENCES simops.seasons(id),
    config_checksum text NOT NULL REFERENCES simops.game_configs(checksum),
    experiment_id text REFERENCES simops.experiments(id),
    actor text NOT NULL DEFAULT 'approver',
    reason text NOT NULL CHECK(length(reason) BETWEEN 1 AND 2000),
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK(previous_season_id<>season_id)
);
CREATE FUNCTION simops.protect_publication() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Publication audit is immutable' USING ERRCODE='23514'; END $$;
CREATE TRIGGER protect_publication BEFORE UPDATE OR DELETE ON simops.config_publications
    FOR EACH ROW EXECUTE FUNCTION simops.protect_publication();
-- Closing is the only operation that may set an unplanned end timestamp, exactly once.
CREATE OR REPLACE FUNCTION simops.protect_season() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF (NEW.id,NEW.name,NEW.game_version,NEW.game_core_checksum,NEW.config_checksum,NEW.score_rule_version,NEW.score_rule_checksum,NEW.starts_at)
        IS DISTINCT FROM (OLD.id,OLD.name,OLD.game_version,OLD.game_core_checksum,OLD.config_checksum,OLD.score_rule_version,OLD.score_rule_checksum,OLD.starts_at)
        OR (OLD.status='closed' AND NEW IS DISTINCT FROM OLD)
        OR (NEW.ends_at IS DISTINCT FROM OLD.ends_at AND NOT
            (OLD.status='active' AND NEW.status='closed' AND OLD.ends_at IS NULL AND NEW.ends_at IS NOT NULL)) THEN
        RAISE EXCEPTION 'Season context is immutable' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
