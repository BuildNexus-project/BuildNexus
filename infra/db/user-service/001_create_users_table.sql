-- BuildNexus :: User Service schema
-- This database is owned exclusively by the User Service.
-- No other service may query it or hold a foreign key into it.

CREATE DATABASE IF NOT EXISTS buildnexus_user_db
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE buildnexus_user_db;

CREATE TABLE IF NOT EXISTS users (
    id            CHAR(36)     NOT NULL,
    full_name     VARCHAR(150) NOT NULL,
    email         VARCHAR(255) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    role          VARCHAR(20)  NOT NULL,
    is_active     TINYINT(1)   NOT NULL DEFAULT 1,
    created_at    DATETIME     NOT NULL,
    updated_at    DATETIME     NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT uq_users_email UNIQUE (email),
    CONSTRAINT ck_users_role CHECK (role IN ('Client', 'Architect', 'ProjectManager', 'Admin'))
) ENGINE = InnoDB;
