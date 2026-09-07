-- BuildNexus :: Project Service — US-07 (Assign Architect & Project Manager)
-- Index the assigned-staff columns, now that they hold values and are filtered on.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- 002 added assigned_architect_id and assigned_project_manager_id, but nothing
-- wrote them, so there was nothing to index. US-07 is the story that assigns
-- staff, and from here on every dashboard load runs
--   WHERE client_id = ? OR assigned_architect_id = ? OR assigned_project_manager_id = ?
-- in ProjectRepository.ListForUserAsync. client_id has had its index since 001;
-- these two get theirs now, so an Architect or Project Manager opening their
-- dashboard is an index lookup per column rather than a table scan.
--
-- No IF NOT EXISTS: MySQL does not accept it on CREATE INDEX, and DbUp runs a
-- script exactly once, so this cannot be applied twice.

CREATE INDEX ix_projects_assigned_architect_id
    ON projects (assigned_architect_id);

CREATE INDEX ix_projects_assigned_project_manager_id
    ON projects (assigned_project_manager_id);
