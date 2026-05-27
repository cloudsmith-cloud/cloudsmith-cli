using System.Text.Json;
using Spectre.Console;

namespace CloudSmith.Cli.Output;

/// <summary>
/// Writes CLI output as either a Spectre.Console table or raw JSON.
/// </summary>
public static class OutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Writes a key-value table (2 columns: key | value).
    /// </summary>
    public static void WriteTable(string format, IEnumerable<(string Key, string Value)> rows, string col1 = "Key", string col2 = "Value")
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            var dict = rows.ToDictionary(r => r.Key, r => (object)r.Value);
            Console.WriteLine(JsonSerializer.Serialize(dict, JsonOptions));
            return;
        }

        Table table = new Table()
            .AddColumn(new TableColumn(col1).LeftAligned())
            .AddColumn(new TableColumn(col2).LeftAligned());

        foreach ((string key, string value) in rows)
            table.AddRow(Markup.Escape(key), Markup.Escape(value));

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Writes an arbitrary object as JSON (for --output json).
    /// </summary>
    public static void WriteJson(object obj)
    {
        Console.WriteLine(JsonSerializer.Serialize(obj, JsonOptions));
    }

    /// <summary>
    /// Writes a plain message. For table output uses AnsiConsole, for JSON writes nothing (caller handles).
    /// </summary>
    public static void WriteMessage(string format, string message)
    {
        if (!format.Equals("json", StringComparison.OrdinalIgnoreCase))
            AnsiConsole.MarkupLine(Markup.Escape(message));
    }

    /// <summary>
    /// Writes an error message to stderr.
    /// </summary>
    public static void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
    }
}
