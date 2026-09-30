-- BuildNexus :: Construction Service — US-14 (Start, Complete & Hand Over Construction)
-- Who handed the finished project over to the Client.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- Why a column here, when start and complete have none
-- ----------------------------------------------------
-- Those two transitions publish an event, and construction_outbox_events keeps the
-- envelope permanently — so the Project Manager who decided is already recorded, and
-- a second copy on this row would be redundant.
--
-- Handover publishes nothing: US-14 names two events and this is not one of them.
-- Without this column the person who actioned the terminal move would be recorded
-- nowhere at all, which is the one place an audit trail should not have a gap — it is
-- the decision that ends the project and hands it to the client.
--
-- Nullable, not NOT NULL: rows written by migration 005 before this column existed
-- have no actor to backfill, and inventing one — a zero GUID, say — would put a false
-- name in the very trail this column exists to keep honest. NULL here means "handed
-- over before this service recorded who did it", which is true, and it can only ever
-- apply to rows that predate this script.

ALTER TABLE construction_phases
    ADD COLUMN handed_over_by CHAR(36) NULL AFTER handed_over_at;
