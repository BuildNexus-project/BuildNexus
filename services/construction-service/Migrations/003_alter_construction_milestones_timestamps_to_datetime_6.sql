-- BuildNexus :: Construction Service — US-12 (Manage Construction Milestones)
--
-- Widens created_at and updated_at from whole-second DATETIME (the precision
-- 002 declared) to microsecond DATETIME(6).
--
-- Why the new migration instead of editing 002 in place: DbUp records applied
-- migrations by filename in schemaversions, and any environment — teammate's
-- laptop, dev database, CI cache — that already ran 002 has that recorded and
-- would never re-run an edited version. A separate ALTER runs on top of what
-- is there, so every environment reaches the same shape without a database
-- reset.
--
-- Why the precision change: ListForProjectAsync orders by (created_at, id),
-- and three back-to-back CreateAsync calls in a single test finish inside one
-- wall-clock second. At DATETIME's whole-second precision, all three land on
-- the same stored value; the id tiebreaker is a random Guid whose
-- alphanumeric order has no relationship to insertion order, so the "oldest
-- first" guarantee failed intermittently. DATETIME(6) matches what
-- DateTime.UtcNow already delivers on the CI Linux host, so three sequential
-- inserts get three distinct stored values and the ordering is stable in the
-- data model rather than by luck of the clock.

ALTER TABLE construction_milestones
    MODIFY COLUMN created_at DATETIME(6) NOT NULL,
    MODIFY COLUMN updated_at DATETIME(6) NOT NULL;