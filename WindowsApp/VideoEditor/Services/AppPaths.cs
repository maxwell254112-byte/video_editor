namespace VideoEditor;

static class AppPaths
{
    public static string ExeDirectory
    {
        get
        {
            var process = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(process))
            {
                var dir = Path.GetDirectoryName(process);
                if (!string.IsNullOrWhiteSpace(dir)) return dir;
            }
            return AppContext.BaseDirectory;
        }
    }

    public static string Root => Path.Combine(ExeDirectory, "editor-data");
    public static string Media => Path.Combine(Root, "media");
    public static string Exports => Path.Combine(Root, "exports");
    public static string Jobs => Path.Combine(Root, "jobs");
    public static string LibraryFile => Path.Combine(Root, "library.json");
    public static string AutosaveFile => Path.Combine(Root, "autosave.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(Media);
        Directory.CreateDirectory(Exports);
        Directory.CreateDirectory(Jobs);
    }
}
