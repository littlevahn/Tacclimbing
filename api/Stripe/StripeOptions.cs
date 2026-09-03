namespace Tacc.Api.Stripe;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string? SecretKey { get; init; }

    public string? WebhookSecret { get; init; }
}
