-- BuildNexus :: Design Service — US-09 (Upload & Version Design Documents)
-- Design documents for a construction project, with every upload kept as its
-- own version so the latest work can be reviewed while the full history stays.
--
-- This database is owned exclusively by the Design Service. No other service
-- may query it or hold a foreign key into it.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.

CREATE TABLE IF NOT EXISTS design_documents (
    id          CHAR(36)     NOT NULL,
    -- The project this document belongs to. Taken from the route and checked
    -- against the Project Service with the caller's own token — deliberately
    -- NOT a foreign key: projects live in the Project Service's own database,
    -- and a key across that boundary would couple the two schemas.
    project_id  CHAR(36)     NOT NULL,
    -- The logical document — "GroundFloorPlan", "Elevations". A later upload
    -- under the same name in the same project is a new version of this row,
    -- not a new document.
    name        VARCHAR(150) NOT NULL,
    -- The Architect who first created it, from the sub claim of their token.
    created_by  CHAR(36)     NOT NULL,
    created_at  DATETIME     NOT NULL,
    CONSTRAINT pk_design_documents PRIMARY KEY (id),
    -- One document of a given name per project, so "GroundFloorPlan" uploaded
    -- twice resolves to one document with two versions rather than two
    -- documents at v1.
    CONSTRAINT uq_design_documents_project_name UNIQUE (project_id, name),
    -- Every listing this service serves starts from "the documents for this
    -- project", so the column is indexed from the beginning.
    INDEX ix_design_documents_project (project_id)
) ENGINE = InnoDB;

CREATE TABLE IF NOT EXISTS design_document_versions (
    id               CHAR(36)     NOT NULL,
    document_id      CHAR(36)     NOT NULL,
    -- 1 for the first upload, then 2, 3, ... The display name the story asks
    -- for (GroundFloorPlan_v1) is this number and the document name; it is
    -- built from the two rather than stored.
    version_number   INT          NOT NULL,
    -- What the Architect called the file on their machine, kept for the
    -- download's filename.
    file_name        VARCHAR(255) NOT NULL,
    -- The validated content type, one of the three the story allows.
    content_type     VARCHAR(100) NOT NULL,
    file_size_bytes  BIGINT       NOT NULL,
    -- The bytes themselves. LONGBLOB holds far more than the endpoint's upload
    -- cap allows through. Kept in the row rather than on a disk the container
    -- would have to mount — the service owns its data outright, and a move to
    -- object storage later sits behind the repository.
    file_bytes       LONGBLOB     NOT NULL,
    -- Every version lands as 'Submitted'. The review states that follow
    -- (Approved, Rejected) arrive with the story that adds them, in its own
    -- numbered script that widens this constraint.
    status           VARCHAR(20)  NOT NULL,
    -- The Architect's note on what changed in this revision. Optional — a first
    -- upload often has nothing to say.
    revision_comment TEXT         NULL,
    -- The Architect who uploaded this version, from their token.
    uploaded_by      CHAR(36)     NOT NULL,
    uploaded_at      DATETIME     NOT NULL,
    CONSTRAINT pk_design_document_versions PRIMARY KEY (id),
    -- A foreign key inside this service's own schema is fine, and wanted: a
    -- version of a document that does not exist is not a thing. This is the one
    -- direction a key may point — never across a service boundary.
    CONSTRAINT fk_design_document_versions_document
        FOREIGN KEY (document_id) REFERENCES design_documents (id) ON DELETE CASCADE,
    -- The version number is unique within its document. This is also what makes
    -- the increment safe under two uploads at once: the second writer to claim
    -- a number loses to this constraint and retries.
    CONSTRAINT uq_design_document_versions_number UNIQUE (document_id, version_number),
    -- 'Submitted' is the only status US-09 defines. A later story adds the
    -- review states in its own numbered script rather than editing this one.
    CONSTRAINT ck_design_document_versions_status CHECK (status IN ('Submitted')),
    CONSTRAINT ck_design_document_versions_size CHECK (file_size_bytes > 0),
    -- The read the listing endpoint serves: one document's versions, in order.
    INDEX ix_design_document_versions_document (document_id, version_number)
) ENGINE = InnoDB;
