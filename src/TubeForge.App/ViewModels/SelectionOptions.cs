using TubeForge.Core.Media;

namespace TubeForge.App.ViewModels;

public sealed record DownloadModeOption(
    DownloadMode Value,
    string Label,
    string Description)
{
    public override string ToString() => Label;
}

public sealed record FilterOption<T>(string Label, T? Value) where T : struct
{
    public override string ToString() => Label;
}
