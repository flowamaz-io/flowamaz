namespace Flowamaz.Core.Exceptions;

/// <summary>A Library request that can't be done as asked; every message says what to do instead.</summary>
public sealed class LibraryException : AppException
{
    private LibraryException(string code, string message, int status) : base(code, message, status) { }

    public static LibraryException ConnectorNotFound(string id) => new("LIBRARY_CONNECTOR_NOT_FOUND",
        $"There is no connector '{id}' in the Library. Run `fmz library search <words>` or open Library → Connectors to find its id.", 404);

    public static LibraryException Ambiguous(string id, IEnumerable<string> pins) => new("LIBRARY_CONNECTOR_AMBIGUOUS",
        $"More than one publisher has a connector called '{id}': {string.Join(", ", pins)}. Use publisher/{id} to say which.", 409);

    public static LibraryException NotInstalled(string id) => new("LIBRARY_CONNECTOR_NOT_INSTALLED",
        $"'{id}' isn't installed in this workspace. Install it first: `fmz library install {id}`.", 404);

    public static LibraryException AlreadyInstalled(string name, string pin, bool newerMajor) => new("LIBRARY_CONNECTOR_INSTALLED",
        newerMajor
            ? $"{name} is already installed here ({pin}). A newer major is available: run `fmz library update` with the connector id, or upgrade it from Library → Connectors."
            : $"{name} is already installed here ({pin}), and it's the latest version.", 409);

    public static LibraryException UpToDate(string name, string pin) => new("LIBRARY_CONNECTOR_UP_TO_DATE",
        $"{name} is already on the latest major ({pin}). Minor and patch releases are applied automatically.", 409);

    public static LibraryException Removed(string name, DateTime? removedOn) => new("LIBRARY_ITEM_REMOVED",
        $"{name} was deprecated and left the Library on {removedOn:yyyy-MM-dd}, so it can't be installed any more. Choose another connector or template.", 410);

    public static LibraryException TemplateNotFound(string id) => new("LIBRARY_TEMPLATE_NOT_FOUND",
        $"There is no template '{id}' in the Library. Run `fmz library search <words> --templates` or open Library → Templates to find it.", 404);

    public static LibraryException TemplatesDontUpdate() => new("LIBRARY_TEMPLATES_DONT_UPDATE",
        "Templates are copied into your workspace when you install them, so they don't update in place. " +
        "To use a newer version, install it again as a new workflow (`fmz library install <template> --templates`).", 400);

    public static LibraryException InvalidManifest(string message) => new("LIBRARY_INVALID_MANIFEST",
        message + " Fix the manifest and publish again.", 400);
}
