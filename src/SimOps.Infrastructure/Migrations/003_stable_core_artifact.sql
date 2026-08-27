-- One-time pre-release transition: the first local test season included Git HEAD in its DLL checksum.
-- Preserve its tickets, runs and frozen leaderboard; the stable artifact receives a new season.
UPDATE simops.seasons SET status = 'closed'
WHERE id = '10000000-0000-0000-0000-000000000001' AND name = 'Local baseline' AND status = 'active';
