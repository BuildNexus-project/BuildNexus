using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace BuildNexus.EndToEnd.Tests;

/// <summary>
/// One Chrome window, and the waiting primitives every step of a flow is built from.
/// </summary>
/// <remarks>
/// There is deliberately no <c>Thread.Sleep</c> anywhere in the suite and no implicit wait. The app
/// is asynchronous in two ways — React renders after a fetch, and some effects only arrive after a
/// Kafka event has crossed services — so every step waits for the <em>thing it needs</em> to be
/// on screen, up to <see cref="E2ESettings.WaitTimeout"/>, and fails with a message naming it.
/// Selenium Manager (bundled with Selenium) fetches the matching chromedriver, so nothing needs
/// installing beyond Chrome itself.
/// </remarks>
public sealed class BrowserSession : IDisposable
{
    public IWebDriver Driver { get; }

    private readonly WebDriverWait _wait;

    public BrowserSession()
    {
        var options = new ChromeOptions();

        if (E2ESettings.Headless)
        {
            options.AddArgument("--headless=new");
        }

        // A fixed, desktop-sized window: the header's "Sign out" button is hidden below the
        // `sm` breakpoint, and a headless default window is smaller than that.
        options.AddArgument("--window-size=1600,1000");
        // The app formats money and dates for the browser's locale; pinning it keeps the amounts
        // the test reads back ("LKR 0.00") the same on every machine.
        options.AddArgument("--lang=en-US");
        // Required when Chrome runs as root inside a CI container; harmless elsewhere.
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");

        Driver = new ChromeDriver(options);
        _wait = new WebDriverWait(Driver, E2ESettings.WaitTimeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(250)
        };
        // Selenium's own default is to fail the wait on these; a re-render can detach an element
        // between the find and the use, and that is a reason to look again, not to fail.
        _wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
    }

    public void GoTo(string path) => Driver.Navigate().GoToUrl(E2ESettings.BaseUrl + path);

    /// <summary>The element once it is displayed. <paramref name="what"/> names it in the failure message.</summary>
    public IWebElement WaitForVisible(By by, string what) =>
        Until(driver =>
        {
            var element = driver.FindElement(by);
            return element.Displayed ? element : null;
        }, what);

    /// <summary>The element once it is displayed and enabled — a submit button is disabled while its request is in flight.</summary>
    public IWebElement WaitForClickable(By by, string what) =>
        Until(driver =>
        {
            var element = driver.FindElement(by);
            return element.Displayed && element.Enabled ? element : null;
        }, what);

    public IWebElement Click(By by, string what)
    {
        var element = WaitForClickable(by, what);
        element.Click();
        return element;
    }

    /// <summary>Clears the field and types into it. Controlled React inputs ignore <c>value=</c> but see real key events.</summary>
    public void Type(By by, string text, string what)
    {
        var element = WaitForVisible(by, what);
        element.Clear();
        element.SendKeys(text);
    }

    /// <summary>Runs <paramref name="condition"/> until it returns something other than null/false, or the wait times out.</summary>
    public T Until<T>(Func<IWebDriver, T?> condition, string what) where T : class
    {
        try
        {
            return _wait.Until(driver => condition(driver))!;
        }
        catch (WebDriverTimeoutException)
        {
            throw new Xunit.Sdk.XunitException(
                $"Timed out after {E2ESettings.WaitTimeout.TotalSeconds:0}s waiting for: {what}. " +
                $"Page: {Driver.Url}{Environment.NewLine}Visible text:{Environment.NewLine}{VisibleText()}");
        }
    }

    /// <summary>
    /// For something an asynchronous event will put on a page that does not poll for it: reloads
    /// the page, looks briefly for the element, and repeats until it appears or the wait runs out.
    /// </summary>
    public void ReloadUntilVisible(By by, string what)
    {
        var deadline = DateTime.UtcNow + E2ESettings.WaitTimeout;

        while (DateTime.UtcNow < deadline)
        {
            Driver.Navigate().Refresh();

            try
            {
                new WebDriverWait(Driver, TimeSpan.FromSeconds(3))
                {
                    PollingInterval = TimeSpan.FromMilliseconds(250)
                }.Until(driver => driver.FindElements(by).Any(element => element.Displayed));

                return;
            }
            catch (WebDriverTimeoutException)
            {
                // Not there yet — the event may still be in flight. Reload and look again.
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Timed out after {E2ESettings.WaitTimeout.TotalSeconds:0}s, reloading, waiting for: {what}. " +
            $"Page: {Driver.Url}{Environment.NewLine}Visible text:{Environment.NewLine}{VisibleText()}");
    }

    /// <summary>Waits until <paramref name="condition"/> is true. Selenium's wait cannot poll a nullable value type, hence the separate method.</summary>
    public void UntilTrue(Func<IWebDriver, bool> condition, string what) =>
        Until(driver => condition(driver) ? (object)true : null, what);

    public bool PageContains(string text) => VisibleText().Contains(text, StringComparison.Ordinal);

    /// <summary>
    /// Saves a screenshot, for a failing run's evidence. Best effort: a failure to capture must
    /// not replace the assertion failure that is the reason for capturing.
    /// </summary>
    public string? Screenshot(string name)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"{name}.png");
            ((ITakesScreenshot)Driver).GetScreenshot().SaveAsFile(path);

            return path;
        }
        catch
        {
            return null;
        }
    }

    private string VisibleText()
    {
        try
        {
            return Driver.FindElement(By.TagName("body")).Text;
        }
        catch
        {
            return "(page text unavailable)";
        }
    }

    public void Dispose()
    {
        try
        {
            Driver.Quit();
        }
        finally
        {
            Driver.Dispose();
        }
    }
}
