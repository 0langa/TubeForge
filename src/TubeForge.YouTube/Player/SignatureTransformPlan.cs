namespace TubeForge.YouTube.Player;

internal enum SignatureOperationKind
{
    Reverse,
    RemoveFirst,
    Swap
}

internal readonly record struct SignatureOperation(SignatureOperationKind Kind, int Argument = 0);

internal sealed record SignatureTransformPlan(string Name, IReadOnlyList<SignatureOperation> Operations)
{
    public string Apply(string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var characters = signature.ToList();
        foreach (var operation in Operations)
        {
            switch (operation.Kind)
            {
                case SignatureOperationKind.Reverse:
                    characters.Reverse();
                    break;

                case SignatureOperationKind.RemoveFirst:
                    var count = Math.Clamp(operation.Argument, 0, characters.Count);
                    characters.RemoveRange(0, count);
                    break;

                case SignatureOperationKind.Swap when characters.Count > 0:
                    var index = operation.Argument % characters.Count;
                    (characters[0], characters[index]) = (characters[index], characters[0]);
                    break;
            }
        }

        return new string([.. characters]);
    }
}
