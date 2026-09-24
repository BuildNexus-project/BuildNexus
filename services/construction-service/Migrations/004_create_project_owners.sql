-- BuildNexus :: Construction Service — US-13 (View Construction Progress)
-- Which Client owns which project, so this service can answer "may this Client
-- read this project's progress?" on its own.
--
-- US-13 opens the milestone and progress reads to the Client role. A role gate
-- alone is not enough there: without an ownership check, any signed-in Client
-- could read any project's build progress by guessing a project id. The owning
-- Client is a fact the Project Service holds (projects.client_id), and this
-- service may not query that database — one schema per service, no cross-service
-- joins or foreign keys. So the fact is replicated here instead, off the
-- ProjectCreated event that already carries clientId on the project-events topic.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- This database is owned exclusively by the Construction Service. No other
-- service may query it or hold a foreign key into it — both ids below are plain
-- columns copied off an event, never keys across a service boundary.

CREATE TABLE IF NOT EXISTS project_owners (
    -- The project, and the primary key: one owner per project. The Project
    -- Service never reassigns a project to a different Client, so there is no
    -- second row to keep and nothing to version.
    project_id  CHAR(36) NOT NULL,
    -- The Client who submitted the project, as the User Service knows them —
    -- the value this service compares a caller's `sub` claim against.
    client_id   CHAR(36) NOT NULL,
    -- When this service learned the fact, not when the project was created.
    -- Kept for a human tracing a row back to the consumer run that wrote it.
    recorded_at DATETIME NOT NULL,
    CONSTRAINT pk_project_owners PRIMARY KEY (project_id)
) ENGINE = InnoDB;
