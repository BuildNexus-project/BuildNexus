namespace BuildNexus.EndToEnd.Tests;

/// <summary>
/// Where the suite points and how it drives the browser, read from environment variables so the
/// same tests run against local Docker Compose, CI, or a deployed environment without an edit.
/// </summary>
/// <remarks>
/// Every default is the local development stack from the repository README: the Vite dev server
/// on 5173 (which proxies <c>/api</c> to the gateway) and the Admin the User Service seeds in
/// Development. Nothing here is a secret that works anywhere else.
/// </remarks>
public static class E2ESettings
{
    /// <summary>The React app's origin. Every API call the app makes goes through it to the gateway.</summary>
    public static string BaseUrl => Read("E2E_BASE_URL", "http://localhost:5173").TrimEnd('/');

    /// <summary>
    /// The Admin account. The workflow needs one because assigning an Architect and a Project
    /// Manager is Admin-only, and an Admin cannot be created through the registration form.
    /// </summary>
    public static string AdminEmail => Read("E2E_ADMIN_EMAIL", "admin@buildnexus.local");

    public static string AdminPassword => Read("E2E_ADMIN_PASSWORD", "ChangeMe123!");

    /// <summary>Headless by default, so the suite runs on CI; set <c>E2E_HEADLESS=false</c> to watch it.</summary>
    public static bool Headless => !string.Equals(Read("E2E_HEADLESS", "true"), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>How long any single wait for the UI — including a Kafka-driven change — may take.</summary>
    public static TimeSpan WaitTimeout => TimeSpan.FromSeconds(int.Parse(Read("E2E_WAIT_SECONDS", "30")));

    private static string Read(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
