using Windows.System;

namespace Loadpath.Commands;

/// <summary>All commands by id, plus shortcut resolution. A shortcut label is never out of sync with its action.</summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, AppCommand> _byId = new();
    private readonly List<(Shortcut Shortcut, AppCommand Command)> _shortcuts = new();

    public IEnumerable<AppCommand> All => _byId.Values;

    public AppCommand Add(AppCommand command)
    {
        _byId[command.Id] = command;
        if (command.Shortcut is { } s)
        {
            foreach (var alt in s.Split('|'))
            {
                if (Shortcut.TryParse(alt.Trim(), out var parsed)) _shortcuts.Add((parsed, command));
            }
        }
        return command;
    }

    public AppCommand this[string id] => _byId[id];
    public AppCommand? Find(string id) => _byId.GetValueOrDefault(id);

    public bool TryExecute(string id) => _byId.TryGetValue(id, out var c) && c.TryExecute();

    /// <summary>Resolve a key chord to a command. Modifier-free single keys are tool switches and view toggles.</summary>
    public AppCommand? Resolve(VirtualKey key, bool ctrl, bool shift, bool alt)
    {
        foreach (var (s, c) in _shortcuts)
        {
            if (s.KeyCode == key && s.Ctrl == ctrl && s.Shift == shift && s.Alt == alt) return c;
        }
        return null;
    }

    public readonly record struct Shortcut(VirtualKey KeyCode, bool Ctrl, bool Shift, bool Alt)
    {
        public static bool TryParse(string text, out Shortcut shortcut)
        {
            shortcut = default;
            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;
            bool ctrl = false, shift = false, alt = false;
            VirtualKey? key = null;
            foreach (var p in parts)
            {
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": ctrl = true; break;
                    case "shift": shift = true; break;
                    case "alt": alt = true; break;
                    default: key = ParseKey(p); break;
                }
            }
            if (key is null) return false;
            shortcut = new Shortcut(key.Value, ctrl, shift, alt);
            return true;
        }

        private static VirtualKey? ParseKey(string p) => p.ToLowerInvariant() switch
        {
            "esc" => VirtualKey.Escape,
            "del" or "delete" => VirtualKey.Delete,
            "backspace" => VirtualKey.Back,
            "enter" => VirtualKey.Enter,
            "space" => VirtualKey.Space,
            "tab" => VirtualKey.Tab,
            "up" => VirtualKey.Up,
            "down" => VirtualKey.Down,
            "left" => VirtualKey.Left,
            "right" => VirtualKey.Right,
            "=" or "+" => (VirtualKey)187,
            "-" => (VirtualKey)189,
            "\\" => (VirtualKey)220,
            "0" => VirtualKey.Number0,
            "1" => VirtualKey.Number1,
            "2" => VirtualKey.Number2,
            "3" => VirtualKey.Number3,
            "4" => VirtualKey.Number4,
            "5" => VirtualKey.Number5,
            _ when p.Length == 1 && char.IsLetter(p[0]) => (VirtualKey)char.ToUpperInvariant(p[0]),
            _ when p.StartsWith('f') && int.TryParse(p[1..], out var fn) => VirtualKey.F1 + (fn - 1),
            _ => null,
        };
    }
}
