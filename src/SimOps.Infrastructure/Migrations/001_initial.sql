CREATE TABLE IF NOT EXISTS simops.game_configs (
    checksum text PRIMARY KEY,
    game_version text NOT NULL,
    config_version text NOT NULL,
    content jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (game_version, config_version)
);

CREATE TABLE IF NOT EXISTS simops.score_rules (
    checksum text PRIMARY KEY,
    version text NOT NULL UNIQUE,
    definition jsonb NOT NULL
);

CREATE TABLE IF NOT EXISTS simops.agent_definitions (
    agent_id text NOT NULL,
    version text NOT NULL,
    persona integer NOT NULL,
    PRIMARY KEY (agent_id, version)
);

CREATE TABLE IF NOT EXISTS simops.runs (
    id uuid PRIMARY KEY,
    population text NOT NULL CHECK (population = 'synthetic'),
    agent_id text NOT NULL,
    agent_version text NOT NULL,
    game_version text NOT NULL,
    config_checksum text NOT NULL REFERENCES simops.game_configs(checksum),
    score_rule_version text NOT NULL,
    score_rule_checksum text NOT NULL REFERENCES simops.score_rules(checksum),
    base_seed numeric(20, 0) NOT NULL CHECK (base_seed >= 0 AND base_seed <= 18446744073709551615),
    idempotency_key text NOT NULL UNIQUE,
    request_hash text NOT NULL,
    client_result_hash text NOT NULL,
    action_count integer NOT NULL CHECK (action_count BETWEEN 1 AND 10000),
    status text NOT NULL CHECK (status IN ('submitted', 'verifying', 'verified', 'rejected', 'failed')),
    rejection_code text,
    result_json jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    verified_at timestamptz,
    FOREIGN KEY (agent_id, agent_version) REFERENCES simops.agent_definitions(agent_id, version),
    CHECK ((status = 'verified' AND result_json IS NOT NULL) OR status <> 'verified')
);

CREATE TABLE IF NOT EXISTS simops.run_actions (
    run_id uuid NOT NULL REFERENCES simops.runs(id),
    sequence integer NOT NULL CHECK (sequence >= 0),
    action_type integer NOT NULL CHECK (action_type BETWEEN 0 AND 5),
    reward_id text,
    PRIMARY KEY (run_id, sequence)
);

CREATE TABLE IF NOT EXISTS simops.run_events (
    run_id uuid NOT NULL REFERENCES simops.runs(id),
    sequence integer NOT NULL CHECK (sequence >= 0),
    event_type text NOT NULL,
    stage_index integer NOT NULL,
    turn_index integer NOT NULL,
    schema_version integer NOT NULL DEFAULT 1,
    payload jsonb NOT NULL,
    emitted_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (run_id, sequence)
);

CREATE TABLE IF NOT EXISTS simops.run_stage_summaries (
    run_id uuid NOT NULL REFERENCES simops.runs(id),
    stage_index integer NOT NULL CHECK (stage_index BETWEEN 1 AND 6),
    encounter_id text NOT NULL,
    cleared boolean NOT NULL,
    turns integer NOT NULL CHECK (turns > 0),
    PRIMARY KEY (run_id, stage_index)
);

CREATE TABLE IF NOT EXISTS simops.jobs (
    id uuid PRIMARY KEY,
    job_type text NOT NULL CHECK (job_type = 'verify_run'),
    run_id uuid NOT NULL UNIQUE REFERENCES simops.runs(id),
    status text NOT NULL CHECK (status IN ('queued', 'running', 'succeeded', 'failed')),
    attempts integer NOT NULL DEFAULT 0,
    max_attempts integer NOT NULL DEFAULT 3,
    available_at timestamptz NOT NULL DEFAULT now(),
    locked_until timestamptz,
    lock_token uuid,
    last_error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz
);

CREATE INDEX IF NOT EXISTS ix_jobs_claim ON simops.jobs(status, available_at, locked_until);
CREATE INDEX IF NOT EXISTS ix_runs_agent_status ON simops.runs(agent_id, agent_version, status);
CREATE INDEX IF NOT EXISTS ix_stage_funnel ON simops.run_stage_summaries(stage_index, cleared);
