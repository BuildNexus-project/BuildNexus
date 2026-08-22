-- BuildNexus :: User Service schema — US-02 (Manage User Profile)
-- Adds the contact details a user may maintain on their own profile.
--
-- Both columns are nullable: every account registered before this ran has none,
-- and a user is free to clear them again. `email` and `role` stay exactly as
-- they were — those are not self-editable, see the profile endpoint.

USE buildnexus_user_db;

ALTER TABLE users
    ADD COLUMN phone_number     VARCHAR(30)  NULL AFTER email,
    ADD COLUMN contact_address  VARCHAR(255) NULL AFTER phone_number;
