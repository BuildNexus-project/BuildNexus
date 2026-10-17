using System.ComponentModel.DataAnnotations;
using BuildNexus.PaymentService.Contracts;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// The amount rule on the three request contracts that carry money: greater than
/// zero, and no more than the column can hold (US-15 AC-1 and AC-2, US-16 AC-1).
/// </summary>
/// <remarks>
/// ASP.NET reads the <c>[Range]</c> attributes and fills <c>ModelState</c> before
/// the controller runs, which is why the controller tests above set
/// <c>ModelState</c> by hand. This is the other half: the attributes themselves,
/// run through the same <see cref="Validator"/> the framework uses. No mocks —
/// there is nothing to isolate.
/// </remarks>
public class RequestAmountValidationTests
{
    public static TheoryData<decimal> AmountsThatMustBeRefused => new()
    {
        0m,
        -0.01m,
        -1m,
        -250_000m,
        0.001m,
        9_999_999_999_999.995m,
        10_000_000_000_000m
    };

    public static TheoryData<decimal> AmountsThatMustBeAccepted => new()
    {
        0.01m,
        0.02m,
        1m,
        250_000.75m,
        9_999_999_999_999.99m
    };

    [Theory]
    [MemberData(nameof(AmountsThatMustBeRefused))]
    public void A_zero_negative_or_oversized_amount_is_refused_on_every_money_request(decimal amount)
    {
        Assert.False(IsValid(new GenerateInvoiceRequest { Amount = amount }), $"invoice accepted {amount}");
        Assert.False(IsValid(new GenerateQuotationRequest { EstimatedTotal = amount }), $"quotation accepted {amount}");
        Assert.False(IsValid(new RecordPaymentRequest { Amount = amount }), $"payment accepted {amount}");
    }

    [Theory]
    [MemberData(nameof(AmountsThatMustBeAccepted))]
    public void A_positive_amount_up_to_the_column_limit_is_accepted_on_every_money_request(decimal amount)
    {
        Assert.True(IsValid(new GenerateInvoiceRequest { Amount = amount }), $"invoice refused {amount}");
        Assert.True(IsValid(new GenerateQuotationRequest { EstimatedTotal = amount }), $"quotation refused {amount}");
        Assert.True(IsValid(new RecordPaymentRequest { Amount = amount }), $"payment refused {amount}");
    }

    [Theory]
    [InlineData("0.01", true)]
    [InlineData("0.00", false)]
    public void The_smallest_amount_allowed_is_one_cent(string text, bool expectedValid)
    {
        // The lower boundary on its own: one cent in, zero out.
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expectedValid, IsValid(new RecordPaymentRequest { Amount = amount }));
    }

    /// <summary>Runs the request's validation attributes, the way the framework does.</summary>
    private static bool IsValid(object request) =>
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults: [],
            validateAllProperties: true);
}
