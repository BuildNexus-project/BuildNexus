-- BuildNexus :: User Service — US-04 (Password Reset)
-- One row per reset link that has been emailed out.
--
-- The link's token is never stored. What is stored is its SHA-256 hash, so a
-- leaked copy of this table cannot be used to reset anybody's password — the
-- same reason `users` holds a password hash rather than a password.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.

CREATE TABLE IF NOT EXISTS password_reset_tokens (
    id          CHAR(36) NOT NULL,
    user_id     CHAR(36) NOT NULL,
    -- SHA-256 as lowercase hex: always 64 characters, so CHAR is exact.
    token_hash  CHAR(64) NOT NULL,
    expires_at  DATETIME NOT NULL,
    -- NULL until the reset completes. A row with a value here is spent and can
    -- never be redeemed again, however long it has left to run.
    consumed_at DATETIME NULL,
    created_at  DATETIME NOT NULL,
    CONSTRAINT pk_password_reset_tokens PRIMARY KEY (id),
    -- Also the lookup index: a redeemed link is found by its hash and nothing
    -- else, and two links can never collide onto one row.
    CONSTRAINT uq_password_reset_tokens_token_hash UNIQUE (token_hash),
    -- Within this service's own schema, which is the only place a foreign key
    -- may point. Deleting an account takes its outstanding links with it.
    CONSTRAINT fk_password_reset_tokens_user FOREIGN KEY (user_id)
        REFERENCES users (id) ON DELETE CASCADE
) ENGINE = InnoDB;
