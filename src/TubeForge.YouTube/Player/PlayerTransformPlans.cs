namespace TubeForge.YouTube.Player;

internal sealed record PlayerTransformPlans(
    SignatureTransformPlan? Signature,
    SignatureTransformPlan? Throttling);
