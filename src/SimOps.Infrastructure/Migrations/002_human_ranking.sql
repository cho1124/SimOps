CREATE TABLE simops.human_players (
    id uuid PRIMARY KEY,
    nickname text NOT NULL CHECK (length(nickname) BETWEEN 1 AND 24),
    credential_hash text NOT NULL UNIQUE,
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'blocked', 'deleted')),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE simops.seasons (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    status text NOT NULL CHECK (status IN ('active', 'closed')),
    game_version text NOT NULL,
    game_core_checksum text NOT NULL,
    config_checksum text NOT NULL REFERENCES simops.game_configs(checksum),
    score_rule_version text NOT NULL,
    score_rule_checksum text NOT NULL REFERENCES simops.score_rules(checksum),
    starts_at timestamptz NOT NULL DEFAULT now(),
    ends_at timestamptz,
    CHECK (ends_at IS NULL OR ends_at > starts_at)
);
CREATE UNIQUE INDEX ux_one_active_season ON simops.seasons(status) WHERE status = 'active';

CREATE FUNCTION simops.protect_season() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF (NEW.id, NEW.game_version, NEW.game_core_checksum, NEW.config_checksum,
        NEW.score_rule_version, NEW.score_rule_checksum, NEW.starts_at, NEW.ends_at)
       IS DISTINCT FROM
       (OLD.id, OLD.game_version, OLD.game_core_checksum, OLD.config_checksum,
        OLD.score_rule_version, OLD.score_rule_checksum, OLD.starts_at, OLD.ends_at)
       OR (OLD.status = 'closed' AND NEW.status <> 'closed') THEN
        RAISE EXCEPTION 'Season context is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_season BEFORE UPDATE ON simops.seasons FOR EACH ROW EXECUTE FUNCTION simops.protect_season();

CREATE TABLE simops.run_tickets (
    id uuid PRIMARY KEY,
    player_id uuid NOT NULL REFERENCES simops.human_players(id),
    season_id uuid NOT NULL REFERENCES simops.seasons(id),
    begin_key text NOT NULL,
    claims jsonb NOT NULL,
    expires_at timestamptz NOT NULL,
    used_at timestamptz,
    UNIQUE (player_id, begin_key)
);

ALTER TABLE simops.runs DROP CONSTRAINT runs_population_check;
ALTER TABLE simops.runs ALTER COLUMN agent_id DROP NOT NULL;
ALTER TABLE simops.runs ALTER COLUMN agent_version DROP NOT NULL;
ALTER TABLE simops.runs ADD COLUMN player_id uuid REFERENCES simops.human_players(id);
ALTER TABLE simops.runs ADD COLUMN season_id uuid REFERENCES simops.seasons(id);
ALTER TABLE simops.runs ADD COLUMN ticket_id uuid UNIQUE REFERENCES simops.run_tickets(id);
ALTER TABLE simops.runs ADD CONSTRAINT run_actor_exclusive CHECK (
    (population = 'synthetic' AND agent_id IS NOT NULL AND agent_version IS NOT NULL
        AND player_id IS NULL AND season_id IS NULL AND ticket_id IS NULL)
    OR (population = 'human' AND agent_id IS NULL AND agent_version IS NULL
        AND player_id IS NOT NULL AND season_id IS NOT NULL AND ticket_id IS NOT NULL)
);

CREATE TABLE simops.leaderboard_entries (
    season_id uuid NOT NULL REFERENCES simops.seasons(id),
    player_id uuid NOT NULL REFERENCES simops.human_players(id),
    run_id uuid NOT NULL UNIQUE REFERENCES simops.runs(id),
    score integer NOT NULL,
    cleared_stages integer NOT NULL,
    total_turns integer NOT NULL,
    final_health integer NOT NULL,
    max_health integer NOT NULL CHECK (max_health > 0),
    health_ratio numeric(30,20) GENERATED ALWAYS AS (final_health::numeric / max_health) STORED,
    verified_at timestamptz NOT NULL,
    PRIMARY KEY (season_id, player_id)
);
CREATE INDEX ix_leaderboard_order ON simops.leaderboard_entries
    (season_id, score DESC, cleared_stages DESC, total_turns, health_ratio DESC, verified_at, player_id);

CREATE FUNCTION simops.protect_leaderboard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE r simops.runs; s simops.seasons;
BEGIN
    IF TG_OP = 'DELETE' THEN
        SELECT * INTO s FROM simops.seasons WHERE id = OLD.season_id FOR SHARE;
        IF s.status = 'closed' OR s.ends_at <= now() THEN RAISE EXCEPTION 'Season leaderboard is frozen'; END IF;
        RETURN OLD;
    END IF;
    SELECT * INTO s FROM simops.seasons WHERE id = NEW.season_id FOR SHARE;
    IF s.status <> 'active' OR s.starts_at > now() OR s.ends_at <= now() THEN RAISE EXCEPTION 'Season leaderboard is frozen'; END IF;
    SELECT * INTO r FROM simops.runs WHERE id = NEW.run_id;
    IF r.population <> 'human' OR r.status <> 'verified'
       OR r.player_id <> NEW.player_id OR r.season_id <> NEW.season_id
       OR r.config_checksum <> s.config_checksum OR r.score_rule_checksum <> s.score_rule_checksum
       OR NEW.score <> (r.result_json->>'finalScore')::integer
       OR NEW.cleared_stages <> (r.result_json->>'clearedStages')::integer
       OR NEW.total_turns <> (r.result_json->>'totalTurns')::integer
       OR NEW.final_health <> (r.result_json->>'finalHealth')::integer
       OR NEW.max_health <> (r.result_json->>'maxHealth')::integer
       OR NEW.verified_at <> r.verified_at THEN
        RAISE EXCEPTION 'Only authoritative verified human runs can enter the leaderboard';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_leaderboard BEFORE INSERT OR UPDATE OR DELETE ON simops.leaderboard_entries
    FOR EACH ROW EXECUTE FUNCTION simops.protect_leaderboard();
