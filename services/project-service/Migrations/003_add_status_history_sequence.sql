-- BuildNexus :: Project Service — US-06 (View & Update Project Status)
-- A monotonic ordering column for the status history.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- Why this exists
-- ---------------
-- 002 ordered the history by `changed_at, id`. changed_at is a plain DATETIME,
-- so it holds whole seconds only, and `id` is a random GUID — meaning two
-- changes to the same project landing in the same second were tie-broken on a
-- value with no relationship to which actually happened first. The result was
-- deterministic but not chronological: QA reproduced a project whose Designing
-- transition rendered above the creation it followed, which is the reverse of
-- what US-06 asks the view to show.
--
-- Creation followed immediately by a transition is not a contrived case — it is
-- a demo, a smoke test, or somebody clicking through a review quickly.
--
-- Sub-second precision on changed_at would narrow the window rather than close
-- it, and would still leave ordering dependent on the clock. AUTO_INCREMENT is
-- monotonic by construction: rows inserted in a transaction take increasing
-- values whatever the timestamp says.

-- Added nullable and without AUTO_INCREMENT first, so the existing rows can be
-- numbered deliberately below. Attaching AUTO_INCREMENT in one step would let
-- MySQL assign values during the table rebuild in primary-key order — which is
-- the random GUID order, exactly the bug being fixed, baked into the data.
ALTER TABLE project_status_history
    ADD COLUMN sequence_number BIGINT NULL AFTER id;

-- Number the rows that already exist by the best reconstruction available:
-- the second they were stamped, then creations before transitions within that
-- second (a project is created before it moves), then the id as a last resort
-- so the statement is deterministic.
--
-- ROW_NUMBER() rather than a @variable counter: MySqlConnector reads @name as a
-- parameter placeholder unless the connection string opts into user variables,
-- and this service's does not.
UPDATE project_status_history AS history
JOIN (
    SELECT
        id,
        ROW_NUMBER() OVER (ORDER BY changed_at, (from_status IS NOT NULL), id) AS position
    FROM project_status_history
) AS ordered ON ordered.id = history.id
SET history.sequence_number = ordered.position;

-- AUTO_INCREMENT requires the column to be indexed, and unique is what it means.
ALTER TABLE project_status_history
    ADD UNIQUE KEY uq_project_status_history_sequence (sequence_number);

-- Now that every existing row holds a value, the column can be made NOT NULL and
-- take over numbering new rows. MySQL sets the counter to the highest value
-- present, so it carries on from the backfill rather than colliding with it.
ALTER TABLE project_status_history
    MODIFY COLUMN sequence_number BIGINT NOT NULL AUTO_INCREMENT;

-- The history is now read as "one project's rows, in sequence order", so the
-- index matches that. The old one led on changed_at, which nothing orders by
-- any more.
ALTER TABLE project_status_history
    ADD INDEX ix_project_status_history_project_sequence (project_id, sequence_number);

ALTER TABLE project_status_history
    DROP INDEX ix_project_status_history_project;
