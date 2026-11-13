using System.Xml.Linq;

namespace ShellInterop.Notifications;

/// <summary>
/// Content of one toast: a title and a body. Validated on construction so invalid text never reaches the Windows API.
/// Text must be a valid XML 1.0 string (no control characters other than TAB/LF/CR, no unpaired surrogates).
/// Invalid characters are rejected, never replaced. Empty text is rejected. No length limit is imposed by this
/// library; Windows may truncate or collapse long or multi-line text (including line breaks) when it displays the toast.
/// </summary>
public sealed record NotificationDefinition
{
    /// <exception cref="ArgumentNullException">A text is null.</exception>
    /// <exception cref="ArgumentException">A text is empty or contains characters not allowed in XML 1.0.</exception>
    public NotificationDefinition(string title, string body)
    {
        ValidateText(title, nameof(title));
        ValidateText(body, nameof(body));
        Title = title;
        Body = body;
    }

    /// <summary>First text line of the toast.</summary>
    public string Title { get; }

    /// <summary>Second text element of the toast. May contain line breaks; Windows may fold them.</summary>
    public string Body { get; }

    /// <summary>
    /// When true the toast asks Windows not to play the notification sound (<c>&lt;audio silent="true"/&gt;</c>).
    /// Default false: the XML is unchanged and Windows plays its default sound. Real-Windows silence has not been verified.
    /// </summary>
    public bool Silent { get; init; }

    private string? _launchUri;

    /// <summary>
    /// Optional absolute URI launched when the toast is clicked (<c>launch="uri" activationType="protocol"</c>).
    /// Null (default): the toast XML is unchanged. The library only emits the URI; the application owns the protocol
    /// registration and must treat the URI it receives as untrusted input.
    /// </summary>
    /// <exception cref="ArgumentException">Not a well-formed absolute URI, or contains whitespace/control characters.</exception>
    public string? LaunchUri
    {
        get => _launchUri;
        init => _launchUri = value is null ? null : ValidateLaunchUri(value);
    }

    private static string ValidateLaunchUri(string uri)
    {
        ValidateText(uri, nameof(LaunchUri));
        foreach (char c in uri)
            if (c <= 0x20 || c == 0x7F || char.IsWhiteSpace(c))
                throw new ArgumentException($"{nameof(LaunchUri)} contains whitespace or a control character (U+{(int)c:X4}).", nameof(LaunchUri));
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsWellFormedOriginalString())
            throw new ArgumentException($"{nameof(LaunchUri)} is not a well-formed absolute URI.", nameof(LaunchUri));
        return uri;
    }

    /// <summary>XML 1.0 Char: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF].</summary>
    internal static void ValidateText(string? text, string name)
    {
        ArgumentNullException.ThrowIfNull(text, name);
        if (text.Length == 0) throw new ArgumentException($"{name} is empty.", name);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
                throw new ArgumentException($"{name}: unpaired high surrogate at index {i} (U+{(int)c:X4}).", name);
            }
            if (char.IsLowSurrogate(c))
                throw new ArgumentException($"{name}: unpaired low surrogate at index {i} (U+{(int)c:X4}).", name);
            bool ok = c == 0x9 || c == 0xA || c == 0xD || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD);
            if (!ok) throw new ArgumentException($"{name}: character U+{(int)c:X4} at index {i} is not allowed in XML 1.0.", name);
        }
    }

    /// <summary>Builds the toast XML via DOM construction (XElement escapes &amp; &lt; &gt; and quotes).</summary>
    internal string ToToastXml()
    {
        var xml = new XElement("toast",
            new XElement("visual",
                new XElement("binding", new XAttribute("template", "ToastGeneric"),
                    new XElement("text", Title),
                    new XElement("text", Body))));
        if (LaunchUri is not null)
        {
            xml.Add(new XAttribute("launch", LaunchUri));
            xml.Add(new XAttribute("activationType", "protocol"));
        }
        if (Silent) xml.Add(new XElement("audio", new XAttribute("silent", "true")));
        return xml.ToString(SaveOptions.DisableFormatting);
    }
}
