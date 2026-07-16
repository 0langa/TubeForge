namespace TubeForge.Tests.Framework;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}
