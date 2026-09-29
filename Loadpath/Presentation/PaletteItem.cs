using Loadpath.Commands;

namespace Loadpath.Presentation;

public sealed partial class PaletteItem : ObservableObject
{
    public PaletteItem(AppCommand command)
    {
        Command = command;
        Title = command.Title;
        Category = command.Category;
        Shortcut = command.ShortcutDisplay.Replace("|", " · ");
        Icon = command.Icon ?? "chevron-right";
        HasIcon = command.Icon is not null;
        IsDisabled = !command.CanExecute;
    }

    public AppCommand Command { get; }
    public string Title { get; }
    public string Category { get; }
    public string Shortcut { get; }
    public string Icon { get; }
    public bool HasIcon { get; }
    public bool IsDisabled { get; }
    [ObservableProperty] private bool _isHighlighted;
}
