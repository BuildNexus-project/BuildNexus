-- BuildNexus :: Design Service — US-10 (View Design Documents & History)
-- Widens the version status constraint to add the review outcomes referenced
-- by 001: a version awaiting a decision, and one that has been approved.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.

-- The "current" version US-10 shows is the latest one that is UnderReview or
-- Approved — Submitted alone cannot answer that. Rejected is left out: no
-- story yet moves a version there, and the constraint can be widened again,
-- the same way this one widens 001, when one does.
--
-- MySQL has no ALTER CHECK, so the constraint from 001 is replaced rather than
-- widened in place.
ALTER TABLE design_document_versions
    DROP CHECK ck_design_document_versions_status;

ALTER TABLE design_document_versions
    ADD CONSTRAINT ck_design_document_versions_status
        CHECK (status IN ('Submitted', 'UnderReview', 'Approved'));
