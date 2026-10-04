namespace Conch.ViewModel
{
    /// <summary>One entry in Open with.</summary>
    /// <param name="Label">What the list shows.</param>
    /// <param name="Open">Opens the file; told whether to remember this app for the file's type.</param>
    /// <param name="CanRemember">True for an app that opens the type, which "always" can apply to.</param>
    public sealed record OpenWithChoice(string Label, Action<bool> Open, bool CanRemember);
}
