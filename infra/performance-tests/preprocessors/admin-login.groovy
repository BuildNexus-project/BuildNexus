import groovy.json.JsonOutput

// Read credentials from the process environment, outside logged -J properties.
// JSON encoding also handles quotes and backslashes in custom Admin passwords.
vars.put('adminLoginBody', JsonOutput.toJson([
    email: System.getenv('ADMIN_EMAIL') ?: 'admin@buildnexus.local',
    password: System.getenv('ADMIN_PASSWORD') ?: 'ChangeMe123!'
]))
