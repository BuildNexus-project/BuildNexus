-- BuildNexus :: Project Service — US-05 (Create Construction Project)
-- One row per construction project a Client has submitted.
--
-- This database is owned exclusively by the Project Service. No other service
-- may query it or hold a foreign key into it.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.

CREATE TABLE IF NOT EXISTS projects (
    id                 CHAR(36)      NOT NULL,
    -- The Client who submitted it, taken from the `sub` claim of their token.
    -- Deliberately NOT a foreign key: `users` lives in the User Service's own
    -- database, and a key across that boundary would couple the two schemas.
    -- The token has already proved the account exists and holds the Client role.
    client_id          CHAR(36)      NOT NULL,
    name               VARCHAR(150)  NOT NULL,
    location           VARCHAR(255)  NOT NULL,
    -- Perches, the unit land is bought and sold in locally. Fractions are
    -- ordinary, so this is not an integer.
    land_size_perches  DECIMAL(10,2) NOT NULL,
    -- Money, so DECIMAL rather than a float: a budget must not drift by a
    -- rounding error between what was typed and what was stored.
    budget             DECIMAL(15,2) NOT NULL,
    floors             INT           NOT NULL,
    bedrooms           INT           NOT NULL,
    bathrooms          INT           NOT NULL,
    -- A count rather than a yes/no, so "no garage" and "two cars" are the same
    -- question. Zero means none.
    garage_spaces      INT           NOT NULL,
    -- Free text, and the one optional field on the form: NULL when the Client
    -- had nothing to add.
    other_requirements TEXT          NULL,
    status             VARCHAR(20)   NOT NULL,
    created_at         DATETIME      NOT NULL,
    updated_at         DATETIME      NOT NULL,
    CONSTRAINT pk_projects PRIMARY KEY (id),
    -- 'Pending' is the only status the system has so far. The story that adds
    -- the next one (approval, scheduling) adds it here in its own numbered
    -- script; listing statuses now would be guessing at a contract no story has
    -- defined yet.
    CONSTRAINT ck_projects_status CHECK (status IN ('Pending')),
    -- The same bounds the request contract enforces, restated where the data
    -- actually lives: a row that reached this table another way is still not
    -- allowed to describe a house with no floors or a project with no budget.
    CONSTRAINT ck_projects_land_size CHECK (land_size_perches > 0),
    CONSTRAINT ck_projects_budget CHECK (budget > 0),
    CONSTRAINT ck_projects_floors CHECK (floors >= 1),
    CONSTRAINT ck_projects_bedrooms CHECK (bedrooms >= 0),
    CONSTRAINT ck_projects_bathrooms CHECK (bathrooms >= 0),
    CONSTRAINT ck_projects_garage_spaces CHECK (garage_spaces >= 0),
    -- Every listing this service will serve starts from "the projects belonging
    -- to this client", so the column is indexed from the beginning.
    INDEX ix_projects_client_id (client_id)
) ENGINE = InnoDB;
