-- BuildNexus :: Design Service — US-23 (Design Approval Event Integration)
-- Widens the outbox event-type constraint to the three types this service now
-- raises: a design submitted, a revision requested, and a design approved.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.

-- MySQL has no ALTER CHECK, so the constraint from 004 is replaced rather than
-- widened in place. 004 allowed 'DesignApproved' alone; US-23 adds the two the
-- Construction Service does not consume yet but the story names.
ALTER TABLE design_outbox_events
    DROP CHECK ck_design_outbox_events_type;

ALTER TABLE design_outbox_events
    ADD CONSTRAINT ck_design_outbox_events_type
        CHECK (event_type IN ('DesignSubmitted', 'DesignRevisionRequested', 'DesignApproved'));
