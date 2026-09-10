-- BuildNexus :: Design Service — US-11 (Review Design: Approve or Request Revision)
-- Widens the version status constraint to add Revision Requested, and adds the
-- columns a review decision records against the version it was made on.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.

-- MySQL has no ALTER CHECK, so the constraint from 002 is replaced rather than
-- widened in place.
ALTER TABLE design_document_versions
    DROP CHECK ck_design_document_versions_status;

ALTER TABLE design_document_versions
    ADD CONSTRAINT ck_design_document_versions_status
        CHECK (status IN ('Submitted', 'UnderReview', 'Approved', 'RevisionRequested'));

-- Who decided, and when. Set by either review action, not just approval — a
-- request for revision is a decision too, just not the final one. NULL until a
-- Client reviews it, which is every version before this story's endpoints
-- touch it.
ALTER TABLE design_document_versions
    ADD COLUMN reviewed_by CHAR(36) NULL AFTER uploaded_at,
    ADD COLUMN reviewed_at DATETIME NULL AFTER reviewed_by;

-- The Client's own note on what needs to change. Kept separate from
-- revision_comment, which is the Architect's note on what they changed in this
-- upload — conflating the two would mean an approval could silently overwrite
-- what the Architect said, or a revision request could silently overwrite it
-- the other way. Set only by a request for revision; an approval leaves it
-- NULL, since there is nothing to ask for.
ALTER TABLE design_document_versions
    ADD COLUMN review_comment TEXT NULL AFTER reviewed_at;
