-- BuildNexus :: Construction Service — US-24 (Construction & Payment Event Integration)
-- Let the outbox carry MilestoneCompleted, and stop requiring a build to have started
-- before an event about it may be written.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are recorded in
-- the schemaversions table — never edit one that has shipped, add the next number instead.
--
-- Two changes, both to construction_outbox_events, both forced by the same new event.
--
-- 1. The event-type CHECK
-- -----------------------
-- 006 allowed exactly the two types US-14 named. US-24 adds a third, MilestoneCompleted,
-- so the list widens. MySQL has no ALTER CHECK, so the constraint is dropped and re-added
-- under the same name rather than edited in place.
--
-- 2. The foreign key to construction_phases — dropped
-- ---------------------------------------------------
-- 006 pointed project_id at construction_phases, on the reasoning that "a construction
-- event about a project whose build never started is not a thing". That was true of the
-- two events US-14 published: both are transitions of the phase itself, so the row always
-- existed by the time either was enqueued.
--
-- MilestoneCompleted breaks that assumption. A Project Manager may mark a milestone
-- Completed at any point after the design is approved — nothing in US-12 gates a milestone
-- status change on the build having been started — so a project can legitimately have a
-- completed milestone and no construction_phases row at all. With the key in place that
-- event's INSERT fails, and because it is enqueued in the same transaction as the status
-- change, the failure would roll back the Project Manager's update too: completing the
-- first milestone before pressing Start would simply error.
--
-- So the key goes. It was enforcing a rule about this service's own data that is no longer
-- true, and the alternative — publishing MilestoneCompleted only for started builds —
-- would silently drop events that other services and reporting are entitled to see.
-- project_id remains a plain column, as it is in every other table here.

ALTER TABLE construction_outbox_events
    DROP FOREIGN KEY fk_construction_outbox_events_phase;

ALTER TABLE construction_outbox_events
    DROP CHECK ck_construction_outbox_events_type;

ALTER TABLE construction_outbox_events
    ADD CONSTRAINT ck_construction_outbox_events_type
        CHECK (event_type IN ('ConstructionStarted', 'ConstructionCompleted', 'MilestoneCompleted'));
