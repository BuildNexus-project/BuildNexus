-- BuildNexus :: Payment Service — US-15 (Generate Quotation & Invoice)
-- The cost estimate a Project Manager or Admin draws up for a project: what the
-- build is expected to cost, before anything is billed against it (AC-1).
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- This database is owned exclusively by the Payment Service. No other service
-- may query it or hold a foreign key into it — project_id is a plain column
-- copied off the request, never a key across a service boundary.

CREATE TABLE IF NOT EXISTS quotations (
    id              CHAR(36)       NOT NULL,
    -- The project this estimate is for. Not unique: a project can be re-quoted
    -- as its scope firms up, and overwriting the previous figure would destroy
    -- the record of what the Client was originally told. The reads order by
    -- created_at DESC, so the newest quotation is the project's current one and
    -- the older rows remain as history.
    project_id      CHAR(36)       NOT NULL,
    -- The estimated total. DECIMAL, not a float: money compared or summed as
    -- binary floating point drifts, and an estimate that renders as 1249999.99
    -- instead of 1250000.00 is the kind of defect nobody trusts a number after.
    -- (15,2) leaves thirteen digits before the decimal point, comfortably past
    -- any figure a construction project carries.
    estimated_total DECIMAL(15, 2) NOT NULL,
    -- The Project Manager or Admin who generated it, from the sub claim of their
    -- token. Recorded because a cost estimate is a statement someone made to a
    -- Client, and an estimate with no author cannot be questioned later.
    created_by      CHAR(36)       NOT NULL,
    -- Microsecond precision, matching the convention the Construction Service
    -- settled on in its migration 003: two quotations generated inside the same
    -- second would otherwise share a created_at, and "newest first" would fall
    -- back to a random Guid whose order means nothing.
    created_at      DATETIME(6)    NOT NULL,
    CONSTRAINT pk_quotations PRIMARY KEY (id),
    -- An estimate of zero or less is not an estimate. Enforced in the database as
    -- well as in the request validator, so a future caller that bypasses the
    -- controller cannot write one.
    CONSTRAINT ck_quotations_estimated_total_positive CHECK (estimated_total > 0),
    -- Serves the two reads this story needs — the Client's list for a project and
    -- the PM's — both of which filter by project and order newest first.
    INDEX ix_quotations_project_created (project_id, created_at DESC)
) ENGINE = InnoDB;
