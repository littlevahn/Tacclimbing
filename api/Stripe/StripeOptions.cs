namespace Tacc.Api.Stripe;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string? SecretKey { get; init; }

    public string? WebhookSecret { get; init; }

    public string? CheckoutSuccessUrl { get; init; }

    public string? CheckoutCancelUrl { get; init; }

    public string[] AllowedShippingCountries { get; init; } = ["US"];

    public string? ShippingRateId { get; init; }
}
