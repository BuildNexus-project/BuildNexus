-- BuildNexus :: Construction Service — US-21 (Role-Based Dashboard Statistics)
-- An optional due date on a construction milestone, so a Project Manager's "milestones due"
-- can mean what it says.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are recorded in the
-- schemaversions table — never edit one that has shipped, add the next number instead.
--
-- US-12 defined a milestone by a name and one of three states, and nothing about when it
-- should be finished — so the dashboard could only say what was still outstanding, never what
-- was late. This adds the missing fact.
--
-- NULL, and additive
-- ------------------
-- The column is nullable and every milestone that exists today has none. Nothing about a
-- milestone without a date changes: it is created, moved and counted exactly as before, and
-- still shows as outstanding until it is completed. A date is something a Project Manager may
-- add when they create a milestone, or set (and clear) afterwards — the template milestones
-- are planted without one, and get theirs later.
--
-- DATE, not DATETIME
-- ------------------
-- A due date is a calendar day. It carries no time of day and no timezone, so there is no
-- instant to be an hour off in another country; "due on the 5th" is the 5th wherever it is
-- read.
--
-- This database is owned exclusively by the Construction Service.

ALTER TABLE construction_milestones
    ADD COLUMN due_date DATE NULL AFTER status;
