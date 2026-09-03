-- BuildNexus :: User Service — US-37 (Admin: Manage Users & Roles)
-- Supports the paged, role-filtered Admin directory.
--
-- The listing filters on `role` and orders by `full_name`, so the index carries
-- both columns in that order: MySQL can then satisfy "the Architects, by name,
-- rows 20 to 40" from the index instead of sorting the whole table for every
-- page. `full_name` alone is left out — an unfiltered page still reads the
-- index in order, since the filter is the leading column only when it is used.
--
-- No IF NOT EXISTS: MySQL does not accept it on CREATE INDEX, and DbUp runs a
-- script exactly once, so this cannot be applied twice.

CREATE INDEX idx_users_role_full_name ON users (role, full_name);
