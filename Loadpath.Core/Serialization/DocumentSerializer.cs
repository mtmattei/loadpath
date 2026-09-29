using System.Text.Json;
using System.Text.Json.Serialization;
using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;

namespace Loadpath.Core.Serialization;

/// <summary>JSON document format, version 1. Small, human-readable, stable ids.</summary>
public static class DocumentSerializer
{
    public const int CurrentVersion = 1;
    public const string Extension = ".loadpath";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed class FileModel
    {
        public int Version { get; set; } = CurrentVersion;
        public string Name { get; set; } = "Untitled";
        public List<NodeModel> Nodes { get; set; } = new();
        public List<MemberModel> Members { get; set; } = new();
    }

    public sealed class NodeModel
    {
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public string? Support { get; set; }
        public double? Fx { get; set; }
        public double? Fy { get; set; }
    }

    public sealed class MemberModel
    {
        public int Id { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public string Section { get; set; } = Model.Section.Chs48.Id;
    }

    public static string Serialize(StructureDocument doc)
    {
        var model = new FileModel { Name = doc.Name };
        foreach (var n in doc.Nodes)
        {
            model.Nodes.Add(new NodeModel
            {
                Id = n.Id,
                X = n.Position.X,
                Y = n.Position.Y,
                Support = n.Support == SupportKind.None ? null : n.Support.ToString(),
                Fx = n.HasLoad ? n.Load.X : null,
                Fy = n.HasLoad ? n.Load.Y : null,
            });
        }
        foreach (var m in doc.Members)
        {
            model.Members.Add(new MemberModel { Id = m.Id, Start = m.StartNodeId, End = m.EndNodeId, Section = m.Section.Id });
        }
        return JsonSerializer.Serialize(model, Options);
    }

    public static DocumentSnapshot Deserialize(string json)
    {
        var model = JsonSerializer.Deserialize<FileModel>(json, Options) ?? throw new InvalidDataException("Empty document.");
        if (model.Version > CurrentVersion) throw new InvalidDataException($"Document version {model.Version} is newer than this app supports.");
        var nodeIds = model.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var m in model.Members)
        {
            if (!nodeIds.Contains(m.Start) || !nodeIds.Contains(m.End)) throw new InvalidDataException($"Member {m.Id} references a missing node.");
        }
        return new DocumentSnapshot
        {
            Name = string.IsNullOrWhiteSpace(model.Name) ? "Untitled" : model.Name,
            Nodes = model.Nodes.Select(n => (
                n.Id,
                new Vec2(n.X, n.Y),
                Enum.TryParse<SupportKind>(n.Support, out var s) ? s : SupportKind.None,
                new Vec2(n.Fx ?? 0, n.Fy ?? 0))).ToList(),
            Members = model.Members.Select(m => (m.Id, m.Start, m.End, m.Section)).ToList(),
        };
    }
}
